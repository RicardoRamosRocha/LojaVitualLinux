using LojaVirtual.Controllers;
using LojaVirtual.Data;
using LojaVirtual.Models;
using LojaVirtual.Services;
using LojaVirtual.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LojaVirtual.Tests;

public sealed class OrderCancellationTests
{
    [Fact]
    public async Task Cancels_pending_order_releases_stock_reservation_and_keeps_payment_attempt()
    {
        await using var database = await TestDatabase.CreateAsync();
        var order = await database.SeedOrderAsync();
        var mercadoPago = new FakeMercadoPagoService();
        var service = new OrderCancellationService(database.Context, mercadoPago);

        await service.CancelAsync(order.Id);

        var saved = await database.ReloadOrderAsync(order.Id);
        Assert.Equal(OrderStatusWorkflow.Cancelled, saved.Status);
        Assert.Equal("Liberada", Assert.Single(saved.StockReservations).Status);
        Assert.Equal(PaymentStatusWorkflow.Cancelled, Assert.Single(saved.PaymentAttempts).Status);
        Assert.Single(await database.Context.OrderPaymentAttempts.ToListAsync());
    }

    [Fact]
    public async Task Repeated_cancellation_is_idempotent_and_does_not_recheck_provider()
    {
        await using var database = await TestDatabase.CreateAsync();
        var order = await database.SeedOrderAsync();
        var mercadoPago = new FakeMercadoPagoService();
        var service = new OrderCancellationService(database.Context, mercadoPago);

        await service.CancelAsync(order.Id);
        await service.CancelAsync(order.Id);

        Assert.Equal(1, mercadoPago.SearchCalls);
        Assert.Equal(OrderStatusWorkflow.Cancelled, (await database.ReloadOrderAsync(order.Id)).Status);
    }

        [Theory]
        [InlineData("approved")]
        [InlineData("in_process")]
        [InlineData("authorized")]
        public async Task Blocks_when_remote_payment_is_approved_or_processing(string paymentStatus)
    {
        await using var database = await TestDatabase.CreateAsync();
        var order = await database.SeedOrderAsync();
        var mercadoPago = new FakeMercadoPagoService
        {
                Payments = [Payment(order, paymentStatus)]
        };
        var service = new OrderCancellationService(database.Context, mercadoPago);

        var exception = await Assert.ThrowsAsync<OrderCancellationException>(() => service.CancelAsync(order.Id));

        Assert.Equal("payment_not_cancellable", exception.Code);
        var saved = await database.ReloadOrderAsync(order.Id);
        Assert.Equal(OrderStatusWorkflow.Pending, saved.Status);
        Assert.Equal("Reservada", Assert.Single(saved.StockReservations).Status);
    }

    [Fact]
    public async Task Cancels_remote_pending_payment_before_releasing_local_reservation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var order = await database.SeedOrderAsync();
        var mercadoPago = new FakeMercadoPagoService
        {
            Payments = [Payment(order, "pending")]
        };
        var service = new OrderCancellationService(database.Context, mercadoPago);

        await service.CancelAsync(order.Id);

        Assert.Equal(["payment-1"], mercadoPago.CancelledPaymentIds);
        Assert.Equal(OrderStatusWorkflow.Cancelled, (await database.ReloadOrderAsync(order.Id)).Status);
    }

    [Fact]
    public async Task Owner_authorization_is_required_before_cancellation_service_is_called()
    {
        await using var database = await TestDatabase.CreateAsync();
        var order = await database.SeedOrderAsync();
        var cancellation = new ThrowingCancellationService();
        var controller = new ApiOrdersController(
            database.Context,
            null!,
            new FakeOrderAccessService(order.Id + 1),
            null!,
            null!,
            null!,
            cancellations: cancellation)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await controller.CancelOrder(order.Id, CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result.Result);
        Assert.False(cancellation.Called);
    }

    [Fact]
    public async Task Delayed_approved_webhook_does_not_revive_cancelled_order_or_consume_stock()
    {
        await using var database = await TestDatabase.CreateAsync();
        var order = await database.SeedOrderAsync(stock: 5, quantity: 2);
        var mercadoPago = new FakeMercadoPagoService();
        await new OrderCancellationService(database.Context, mercadoPago).CancelAsync(order.Id);
        var paymentService = new OrderPaymentService(database.Context, mercadoPago, TestConfiguration());

        Assert.True(await paymentService.ProcessPaymentAsync(Payment(order, "approved")));

        var saved = await database.ReloadOrderAsync(order.Id);
        Assert.Equal(OrderStatusWorkflow.Cancelled, saved.Status);
        Assert.Equal(5, (await database.Context.Products.SingleAsync()).Stock);
        Assert.Equal(PaymentStatusWorkflow.Approved, Assert.Single(saved.PaymentAttempts).Status);
    }

    private static IConfiguration TestConfiguration()
    {
        var configuration = new ConfigurationManager();
        configuration["MercadoPago:AccessToken"] = "test-access-token";
        return configuration;
    }

    private static MercadoPagoPaymentResult Payment(Order order, string status) =>
        new("payment-1", status, order.Id.ToString(), "pref-1", order.Total, "BRL", null, null);

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public AppDbContext Context { get; }

        private TestDatabase(SqliteConnection connection, AppDbContext context)
        {
            _connection = connection;
            Context = context;
        }

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var context = new AppDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new TestDatabase(connection, context);
        }

        public async Task<Order> SeedOrderAsync(int stock = 5, int quantity = 1)
        {
            var product = new Product { Name = "Produto teste", Price = 10m, Stock = stock, IsActive = true };
            var order = new Order
            {
                CustomerName = "Cliente teste",
                Email = "cliente@example.com",
                Phone = "11999999999",
                Total = 10m * quantity,
                Status = OrderStatusWorkflow.Pending,
                IdempotencyKey = Guid.NewGuid().ToString("N"),
                IdempotencyFingerprint = "fingerprint"
            };
            order.Items.Add(new OrderItem { Product = product, Quantity = quantity, UnitPrice = 10m });
            order.StockReservations.Add(new OrderStockReservation
            {
                Product = product,
                Quantity = quantity,
                Status = "Reservada",
                ExpiresAt = DateTime.Now.AddMinutes(20)
            });
            order.PaymentAttempts.Add(new OrderPaymentAttempt
            {
                PreferenceId = "pref-1",
                CheckoutUrl = "https://example.com/checkout",
                Amount = order.Total,
                IdempotencyKey = "attempt-1",
                Status = PaymentStatusWorkflow.AwaitingPayment
            });
            Context.Orders.Add(order);
            await Context.SaveChangesAsync();
            return await ReloadOrderAsync(order.Id);
        }

        public async Task<Order> ReloadOrderAsync(int id)
        {
            return await Context.Orders
                .Include(item => item.Items).ThenInclude(item => item.Product)
                .Include(item => item.StockReservations)
                .Include(item => item.PaymentAttempts)
                .SingleAsync(item => item.Id == id);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class FakeMercadoPagoService : IMercadoPagoService
    {
        public IReadOnlyList<MercadoPagoPaymentResult> Payments { get; init; } = [];
        public List<string> CancelledPaymentIds { get; } = [];
        public int SearchCalls { get; private set; }

        public Task<MercadoPagoPreferenceResult> CreatePreferenceAsync(Order order, string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MercadoPagoPreferenceResult("pref-1", "https://example.com/checkout"));
        public Task<MercadoPagoPaymentResult?> GetPaymentAsync(string paymentId, CancellationToken cancellationToken = default) =>
            Task.FromResult<MercadoPagoPaymentResult?>(Payments.FirstOrDefault(item => item.PaymentId == paymentId));
        public Task<IReadOnlyList<MercadoPagoPaymentResult>> SearchPaymentsByExternalReferenceAsync(string externalReference, CancellationToken cancellationToken = default)
        {
            SearchCalls++;
            return Task.FromResult(Payments);
        }
        public Task CancelPaymentAsync(string paymentId, CancellationToken cancellationToken = default)
        {
            CancelledPaymentIds.Add(paymentId);
            return Task.CompletedTask;
        }
        public bool IsValidWebhookSignature(string? signature, string? requestId, string? dataId) => true;
        public Task<string> CriarPreferenciaAsync(Order order, string? idempotencyKey = null) => Task.FromResult("https://example.com/checkout");
    }

    private sealed class ThrowingCancellationService : IOrderCancellationService
    {
        public bool Called { get; private set; }
        public Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
        {
            Called = true;
            throw new InvalidOperationException("unauthorized owner must not reach cancellation");
        }
    }

    private sealed class FakeOrderAccessService(int orderId) : IOrderAccessCredentialService
    {
        public Task<PreparedOrderAccess> PrepareAsync(int requestedOrderId, HttpRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PreparedOrderAccess([], DateTime.UtcNow, null));
        public Task<IReadOnlyList<AuthorizedOrderAccess>> ResolveAsync(HttpRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuthorizedOrderAccess>>([new AuthorizedOrderAccess("token", new OrderAccessCredential { OrderId = orderId })]);
        public Task RevokeAllAsync(HttpRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void WriteCookie(HttpResponse response, PreparedOrderAccess access) { }
        public void DeleteCookie(HttpResponse response) { }
    }
}
