using System.Net;
using LojaVirtual.Data;
using LojaVirtual.Models;
using LojaVirtual.Services;
using LojaVirtual.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LojaVirtual.Tests;

public sealed class PaymentFlowTests
{
    [Fact]
    public async Task Approved_payment_confirms_reservation_and_decrements_stock_once()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        var order = SeedOrder(context, stock: 5, quantity: 2);
        var service = new OrderPaymentService(context, new FakeMercadoPagoService(), TestConfiguration());

        var payment = Payment(order, "approved");
        Assert.True(await service.ProcessPaymentAsync(payment));
        Assert.Equal(OrderStatusWorkflow.Preparing, order.Status);
        Assert.Equal(3, (await context.Products.SingleAsync()).Stock);
        Assert.Equal("Confirmada", (await context.StockReservations.SingleAsync()).Status);

        Assert.True(await service.ProcessPaymentAsync(payment));
        Assert.Equal(3, (await context.Products.SingleAsync()).Stock);
    }

    [Fact]
    public async Task Refund_before_shipping_restores_stock_once()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        var order = SeedOrder(context, stock: 3, quantity: 1);
        order.Status = OrderStatusWorkflow.Preparing;
        var reservation = await context.StockReservations.SingleAsync();
        reservation.Status = "Confirmada";
        (await context.Products.SingleAsync()).Stock = 2;
        await context.SaveChangesAsync();
        var service = new OrderPaymentService(context, new FakeMercadoPagoService(), TestConfiguration());

        var payment = Payment(order, "refunded");
        Assert.True(await service.ProcessPaymentAsync(payment));
        Assert.Equal(3, (await context.Products.SingleAsync()).Stock);
        Assert.Equal("Reembolsada", (await context.StockReservations.SingleAsync()).Status);

        Assert.True(await service.ProcessPaymentAsync(payment));
        Assert.Equal(3, (await context.Products.SingleAsync()).Stock);
    }

    [Fact]
    public void Webhook_signature_accepts_current_signature_and_rejects_stale_signature()
    {
        var configuration = TestConfiguration();
        var service = new MercadoPagoService(new HttpClient(new StubHandler()), configuration);
        var requestId = "request-1";
        var dataId = "123";
        var timestampSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var timestamp = timestampSeconds.ToString();
        var manifest = $"id:{dataId};request-id:{requestId};ts:{timestamp};";
        var secret = "test-webhook-secret";
        var hash = Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(secret), System.Text.Encoding.UTF8.GetBytes(manifest))).ToLowerInvariant();

        Assert.True(service.IsValidWebhookSignature($"ts={timestamp},v1={hash}", requestId, dataId));
        Assert.False(service.IsValidWebhookSignature($"ts={timestampSeconds - 600},v1={hash}", requestId, dataId));
    }

    private static AppDbContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var context = new AppDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    private static Order SeedOrder(AppDbContext context, int stock, int quantity)
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
        context.Orders.Add(order);
        context.SaveChanges();
        return context.Orders.Include(item => item.Items).ThenInclude(item => item.Product)
            .Include(item => item.StockReservations).Include(item => item.PaymentAttempts)
            .Single(item => item.Id == order.Id);
    }

    private static MercadoPagoPaymentResult Payment(Order order, string status) =>
        new("payment-1", status, order.Id.ToString(), "pref-1", order.Total, "BRL", null, null);

    private static IConfiguration TestConfiguration()
    {
        var configuration = new ConfigurationManager();
        configuration["MercadoPago:PreferenceExpirationMinutes"] = "30";
        configuration["MercadoPago:WebhookSecret"] = "test-webhook-secret";
        return configuration;
    }

    private sealed class FakeMercadoPagoService : IMercadoPagoService
    {
        public Task<MercadoPagoPreferenceResult> CreatePreferenceAsync(Order order, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MercadoPagoPreferenceResult("pref-1", "https://example.com/checkout"));
        public Task<MercadoPagoPaymentResult?> GetPaymentAsync(string paymentId, CancellationToken cancellationToken = default) =>
            Task.FromResult<MercadoPagoPaymentResult?>(null);
        public bool IsValidWebhookSignature(string? signature, string? requestId, string? dataId) => true;
        public Task<string> CriarPreferenciaAsync(Order order) => Task.FromResult("https://example.com/checkout");
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
