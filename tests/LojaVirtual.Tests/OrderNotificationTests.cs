using LojaVirtual.Data;
using LojaVirtual.Models;
using LojaVirtual.Services;
using LojaVirtual.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace LojaVirtual.Tests;

public sealed class OrderNotificationTests
{
    [Fact]
    public async Task Enqueue_is_idempotent_and_ignores_invalid_email()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();

        var valid = new Order { Email = "cliente@example.com", Total = 10m };
        var invalid = new Order { Email = "", Total = 10m };
        context.Orders.AddRange(valid, invalid);
        await context.SaveChangesAsync();

        var outbox = new OrderNotificationOutboxService(context);
        await outbox.EnqueueAsync(valid, OrderNotificationEventTypes.OrderCreated);
        await outbox.EnqueueAsync(valid, OrderNotificationEventTypes.OrderCreated);
        await outbox.EnqueueAsync(invalid, OrderNotificationEventTypes.OrderCreated);
        await context.SaveChangesAsync();

        Assert.Single(await context.OrderNotificationOutbox.ToListAsync());
    }

    [Fact]
    public async Task Approved_payment_creates_one_combined_notification_and_duplicate_webhook_does_not_add_another()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();

        var product = new Product { Name = "Produto", Price = 10m, Stock = 2, IsActive = true };
        var order = new Order
        {
            Email = "cliente@example.com",
            Total = 10m,
            Status = OrderStatusWorkflow.Pending,
            IdempotencyKey = Guid.NewGuid().ToString("N")
        };
        order.Items.Add(new OrderItem { Product = product, Quantity = 1, UnitPrice = 10m });
        order.StockReservations.Add(new OrderStockReservation
        {
            Product = product,
            Quantity = 1,
            Status = "Reservada",
            CreatedAt = DateTime.Now,
            ExpiresAt = DateTime.Now.AddMinutes(10)
        });
        order.PaymentAttempts.Add(new OrderPaymentAttempt
        {
            AttemptNumber = 1,
            PreferenceId = "preference-1",
            Status = PaymentStatusWorkflow.AwaitingPayment,
            Amount = 10m,
            IdempotencyKey = "attempt-1"
        });
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var service = new OrderPaymentService(
            context,
            new StubMercadoPagoService(),
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            new OrderNotificationOutboxService(context));
        var payment = new MercadoPagoPaymentResult(
            "payment-1", "approved", order.Id.ToString(), "preference-1", 10m, "BRL", null, null);

        Assert.Equal(PaymentProcessingResult.Processed, await service.ProcessPaymentResultAsync(payment));
        Assert.Equal(PaymentProcessingResult.Duplicate, await service.ProcessPaymentResultAsync(payment));
        Assert.Single(await context.OrderNotificationOutbox
            .Where(item => item.EventType == OrderNotificationEventTypes.PaymentApprovedPreparing)
            .ToListAsync());
    }

    [Fact]
    public async Task Two_workers_claim_one_event_and_a_temporary_failure_is_retryable_after_restart()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var setup = CreateContext(connection);
        await setup.Database.EnsureCreatedAsync();
        var order = new Order { Email = "cliente@example.com", CustomerName = "Cliente", Total = 10m };
        setup.Orders.Add(order);
        await setup.SaveChangesAsync();
        var outbox = new OrderNotificationOutboxService(setup);
        await outbox.EnqueueAsync(order, OrderNotificationEventTypes.OrderCreated);
        await setup.SaveChangesAsync();

        var sender = new RecordingSender { FailFirst = true };
        var factory = new TestContextFactory(connection);
        var worker1 = CreateWorker(factory, sender);
        var worker2 = CreateWorker(factory, sender);
        using var firstRun = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await Task.WhenAll(worker1.StartAsync(firstRun.Token), worker2.StartAsync(firstRun.Token));
        await sender.FirstAttempt.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await worker1.StopAsync(CancellationToken.None);
        await worker2.StopAsync(CancellationToken.None);

        await using (var reset = CreateContext(connection))
        {
            await reset.OrderNotificationOutbox.ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.NextAttemptAtUtc, DateTime.UtcNow.AddSeconds(-1))
                .SetProperty(item => item.LeaseToken, (string?)null)
                .SetProperty(item => item.LeaseExpiresAtUtc, (DateTime?)null));
        }

        var workerAfterRestart = CreateWorker(factory, sender);
        using var restart = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await workerAfterRestart.StartAsync(restart.Token);
        await sender.Success.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await workerAfterRestart.StopAsync(CancellationToken.None);

        Assert.Equal(2, sender.Attempts);
        await using var verify = CreateContext(connection);
        Assert.NotNull(await verify.OrderNotificationOutbox.Select(item => item.ProcessedAtUtc).SingleAsync());
    }

    [Fact]
    public async Task Disabled_worker_does_not_create_context_query_outbox_or_send()
    {
        var factory = new CountingContextFactory();
        var sender = new RecordingSender();
        var worker = new OrderNotificationOutboxWorker(
            factory,
            sender,
            new OrderNotificationComposer(),
            Options.Create(new OrderNotificationsOptions { Enabled = false }),
            Options.Create(new ResendOptions { Enabled = true }),
            NullLogger<OrderNotificationOutboxWorker>.Instance);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await worker.StartAsync(cancellation.Token);
        await Task.Delay(50);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(0, factory.CreateCalls);
        Assert.Equal(0, sender.Attempts);
    }

    private static OrderNotificationOutboxWorker CreateWorker(
        TestContextFactory factory,
        RecordingSender sender) =>
        new(
            factory,
            sender,
            new OrderNotificationComposer(),
            Options.Create(new OrderNotificationsOptions { Enabled = true }),
            Options.Create(new ResendOptions { Enabled = true }),
            NullLogger<OrderNotificationOutboxWorker>.Instance);

    private static AppDbContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);

    private sealed class TestContextFactory(SqliteConnection connection) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => CreateContext(connection);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateContext(connection));
    }

    private sealed class CountingContextFactory : IDbContextFactory<AppDbContext>
    {
        public int CreateCalls { get; private set; }

        public AppDbContext CreateDbContext()
        {
            CreateCalls++;
            throw new InvalidOperationException("The disabled worker must not create a DbContext.");
        }

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            throw new InvalidOperationException("The disabled worker must not create a DbContext.");
        }
    }

    private sealed class RecordingSender : ITransactionalEmailSender
    {
        public bool FailFirst { get; set; }
        public int Attempts { get; private set; }
        public TaskCompletionSource<bool> FirstAttempt { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Success { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task SendAsync(TransactionalEmailMessage message, CancellationToken cancellationToken = default)
        {
            Attempts++;
            FirstAttempt.TrySetResult(true);
            if (FailFirst)
            {
                FailFirst = false;
                throw new TransactionalEmailException("temporary");
            }

            Success.TrySetResult(true);
            return Task.CompletedTask;
        }
    }

    private sealed class StubMercadoPagoService : IMercadoPagoService
    {
        public Task<MercadoPagoPreferenceResult> CreatePreferenceAsync(Order order, string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MercadoPagoPreferenceResult("preference", "https://example.com"));
        public Task<MercadoPagoPaymentResult?> GetPaymentAsync(string paymentId, CancellationToken cancellationToken = default) =>
            Task.FromResult<MercadoPagoPaymentResult?>(null);
        public Task<IReadOnlyList<MercadoPagoPaymentResult>> SearchPaymentsByExternalReferenceAsync(string externalReference, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MercadoPagoPaymentResult>>([]);
        public Task CancelPaymentAsync(string paymentId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public bool IsValidWebhookSignature(string? signature, string? requestId, string? dataId) => true;
        public Task<string> CriarPreferenciaAsync(Order order, string? idempotencyKey = null) =>
            Task.FromResult("https://example.com");
    }
}
