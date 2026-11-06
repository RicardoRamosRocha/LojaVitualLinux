using LojaVirtual.Data;
using LojaVirtual.Controllers;
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

public sealed class MercadoPagoWebhookTests
{
    [Fact]
    public async Task Webhook_returns_ok_for_duplicate_event_without_decrementing_stock_twice()
    {
        await using var database = await TestDatabase.CreateAsync();
        var order = database.SeedOrder(stock: 5, quantity: 2);
        var payment = Payment(order, "approved");
        var mercadoPago = new FakeMercadoPagoService(payment);
        var controller = CreateController(mercadoPago, database.Context);

        var first = await controller.Webhook(payment.PaymentId, CancellationToken.None);
        var second = await controller.Webhook(payment.PaymentId, CancellationToken.None);

        Assert.IsType<OkResult>(first);
        Assert.IsType<OkResult>(second);
        Assert.Equal(3, (await database.Context.Products.SingleAsync()).Stock);
    }

    [Fact]
    public async Task Webhook_returns_bad_request_for_signed_event_that_does_not_match_an_order()
    {
        await using var database = await TestDatabase.CreateAsync();
        var payment = new MercadoPagoPaymentResult("payment-1", "approved", "not-an-order", "pref-1", 10m, "BRL", null, null);
        var controller = CreateController(new FakeMercadoPagoService(payment), database.Context);

        var result = await controller.Webhook(payment.PaymentId, CancellationToken.None);

        var response = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("payment_event_invalid", response.Value?.GetType().GetProperty("code")?.GetValue(response.Value));
    }

    [Fact]
    public async Task Webhook_returns_service_unavailable_when_payment_lookup_fails_transiently()
    {
        await using var database = await TestDatabase.CreateAsync();
        var controller = CreateController(new FakeMercadoPagoService(new MercadoPagoException("temporary provider failure")), database.Context);

        var result = await controller.Webhook("payment-1", CancellationToken.None);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsType<StatusCodeResult>(result).StatusCode);
    }

    private static MercadoPagoWebhookController CreateController(IMercadoPagoService mercadoPago, AppDbContext context)
    {
        var controller = new MercadoPagoWebhookController(
            mercadoPago,
            new OrderPaymentService(context, mercadoPago, new ConfigurationManager()));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.HttpContext.Request.Headers["x-signature"] = "valid";
        controller.HttpContext.Request.Headers["x-request-id"] = "request-1";
        return controller;
    }

    private static MercadoPagoPaymentResult Payment(Order order, string status) =>
        new("payment-1", status, order.Id.ToString(), "pref-1", order.Total, "BRL", null, null);

    private sealed class TestDatabase : IAsyncDisposable
    {
        private TestDatabase(SqliteConnection connection, AppDbContext context)
        {
            Connection = connection;
            Context = context;
        }

        public SqliteConnection Connection { get; }
        public AppDbContext Context { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var context = new AppDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new TestDatabase(connection, context);
        }

        public Order SeedOrder(int stock, int quantity)
        {
            var product = new Product { Name = "Teste", Price = 10m, Stock = stock, IsActive = true };
            var order = new Order
            {
                CustomerName = "Cliente Teste",
                Email = "cliente@example.com",
                Phone = "11999999999",
                Total = 10m * quantity,
                Status = OrderStatusWorkflow.Pending,
                IdempotencyKey = Guid.NewGuid().ToString("N"),
                IdempotencyFingerprint = "fingerprint"
            };
            order.Items.Add(new OrderItem { Product = product, ProductId = 0, Quantity = quantity, UnitPrice = 10m });
            order.StockReservations.Add(new OrderStockReservation
            {
                Product = product,
                ProductId = 0,
                Quantity = quantity,
                Status = "Reservada",
                ExpiresAt = DateTime.Now.AddMinutes(20)
            });
            order.PaymentAttempts.Add(new OrderPaymentAttempt
            {
                PreferenceId = "pref-1",
                CheckoutUrl = "https://example.com/checkout",
                Amount = order.Total,
                IdempotencyKey = "attempt-1"
            });
            Context.Orders.Add(order);
            Context.SaveChanges();
            return Context.Orders.Include(item => item.Items).ThenInclude(item => item.Product)
                .Include(item => item.StockReservations).Include(item => item.PaymentAttempts)
                .Single(item => item.Id == order.Id);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    private sealed class FakeMercadoPagoService : IMercadoPagoService
    {
        private readonly MercadoPagoPaymentResult? _payment;
        private readonly MercadoPagoException? _exception;

        public FakeMercadoPagoService(MercadoPagoPaymentResult payment) => _payment = payment;
        public FakeMercadoPagoService(MercadoPagoException exception) => _exception = exception;

        public Task<MercadoPagoPreferenceResult> CreatePreferenceAsync(Order order, string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<MercadoPagoPaymentResult?> GetPaymentAsync(string paymentId, CancellationToken cancellationToken = default) =>
            _exception is not null ? Task.FromException<MercadoPagoPaymentResult?>(_exception) : Task.FromResult(_payment);
        public Task<IReadOnlyList<MercadoPagoPaymentResult>> SearchPaymentsByExternalReferenceAsync(string externalReference, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MercadoPagoPaymentResult>>([]);
        public Task CancelPaymentAsync(string paymentId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public bool IsValidWebhookSignature(string? signature, string? requestId, string? dataId) => true;
        public Task<string> CriarPreferenciaAsync(Order order, string? idempotencyKey = null) => throw new NotSupportedException();
    }
}
