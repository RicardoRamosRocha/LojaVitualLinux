using System.Net;
using System.Globalization;
using LojaVirtual.Data;
using LojaVirtual.Models;
using LojaVirtual.Services;
using LojaVirtual.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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
    public async Task Pending_payment_keeps_order_and_reservation_awaiting_confirmation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        var order = SeedOrder(context, stock: 5, quantity: 2);
        var service = new OrderPaymentService(context, new FakeMercadoPagoService(), TestConfiguration());

        Assert.True(await service.ProcessPaymentAsync(Payment(order, "pending")));

        var attempt = await context.OrderPaymentAttempts.SingleAsync();
        Assert.Equal(PaymentStatusWorkflow.AwaitingPayment, attempt.Status);
        Assert.Equal(OrderStatusWorkflow.Pending, order.Status);
        Assert.Equal(5, (await context.Products.SingleAsync()).Stock);
        Assert.Equal("Reservada", (await context.StockReservations.SingleAsync()).Status);
    }

    [Fact]
    public async Task Rejected_payment_cancels_order_and_releases_reservation_without_stock_change()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        var order = SeedOrder(context, stock: 5, quantity: 2);
        var service = new OrderPaymentService(context, new FakeMercadoPagoService(), TestConfiguration());

        Assert.True(await service.ProcessPaymentAsync(Payment(order, "rejected")));

        var attempt = await context.OrderPaymentAttempts.SingleAsync();
        Assert.Equal(PaymentStatusWorkflow.Rejected, attempt.Status);
        Assert.Equal(OrderStatusWorkflow.Cancelled, order.Status);
        Assert.Equal(5, (await context.Products.SingleAsync()).Stock);
        Assert.Equal("Liberada", (await context.StockReservations.SingleAsync()).Status);
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
    public async Task Expired_reservation_is_renewed_before_preference_creation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        var order = SeedOrder(context, stock: 5, quantity: 2);
        var reservation = await context.StockReservations.SingleAsync();
        reservation.ExpiresAt = DateTime.Now.AddMinutes(-1);
        var existingAttempt = await context.OrderPaymentAttempts.SingleAsync();
        context.OrderPaymentAttempts.Remove(existingAttempt);
        await context.SaveChangesAsync();

        var mercadoPago = new CountingMercadoPagoService();
        var service = new OrderPaymentService(context, mercadoPago, TestConfiguration());

        var attempt = await service.EnsurePreferenceAsync(order);

        Assert.Equal(PaymentStatusWorkflow.AwaitingPayment, attempt.Status);
        Assert.Equal(1, mercadoPago.CreateCalls);
        Assert.True((await context.StockReservations.SingleAsync()).ExpiresAt > DateTime.Now);
    }

    [Fact]
    public async Task Expired_reservation_is_rejected_when_stock_is_no_longer_available()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        var order = SeedOrder(context, stock: 1, quantity: 2);
        var reservation = await context.StockReservations.SingleAsync();
        reservation.ExpiresAt = DateTime.Now.AddMinutes(-1);
        var existingAttempt = await context.OrderPaymentAttempts.SingleAsync();
        context.OrderPaymentAttempts.Remove(existingAttempt);
        await context.SaveChangesAsync();

        var mercadoPago = new CountingMercadoPagoService();
        var service = new OrderPaymentService(context, mercadoPago, TestConfiguration());

        var exception = await Assert.ThrowsAsync<PaymentEligibilityException>(() => service.EnsurePreferenceAsync(order));

        Assert.Equal("insufficient_stock", exception.Code);
        Assert.Equal(0, mercadoPago.CreateCalls);
        Assert.Empty(await context.OrderPaymentAttempts.ToListAsync());
    }

    [Fact]
    public async Task Repeated_resume_reuses_the_same_local_attempt_and_idempotency_key()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        var order = SeedOrder(context, stock: 5, quantity: 2);
        var existingAttempt = await context.OrderPaymentAttempts.SingleAsync();
        context.OrderPaymentAttempts.Remove(existingAttempt);
        await context.SaveChangesAsync();

        var mercadoPago = new CountingMercadoPagoService();
        var service = new OrderPaymentService(context, mercadoPago, TestConfiguration());

        var first = await service.EnsurePreferenceAsync(order);
        var second = await service.EnsurePreferenceAsync(order);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.IdempotencyKey, mercadoPago.LastIdempotencyKey);
        Assert.Equal(1, mercadoPago.CreateCalls);
    }

    [Fact]
    public async Task Approved_payment_cannot_be_resumed_or_create_a_new_preference()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        var order = SeedOrder(context, stock: 5, quantity: 2);
        var attempt = await context.OrderPaymentAttempts.SingleAsync();
        attempt.Status = PaymentStatusWorkflow.Approved;
        attempt.PaymentId = "payment-1";
        await context.SaveChangesAsync();

        var mercadoPago = new CountingMercadoPagoService();
        var service = new OrderPaymentService(context, mercadoPago, TestConfiguration());

        var exception = await Assert.ThrowsAsync<PaymentEligibilityException>(() => service.EnsurePreferenceAsync(order));

        Assert.Equal("payment_not_eligible", exception.Code);
        Assert.Equal(0, mercadoPago.CreateCalls);
    }

    [Fact]
    public async Task Authorized_resume_endpoint_creates_payment_for_the_authorized_order()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        var order = SeedOrder(context, stock: 5, quantity: 2);
        order.ShippingMethod = "PAC";
        order.Total = 35m;
        await context.SaveChangesAsync();
        var existingAttempt = await context.OrderPaymentAttempts.SingleAsync();
        context.OrderPaymentAttempts.Remove(existingAttempt);
        await context.SaveChangesAsync();

        var mercadoPago = new CountingMercadoPagoService();
        var controller = new LojaVirtual.Controllers.ApiOrdersController(
            context,
            null!,
            new FakeOrderAccessService(order.Id),
            new OrderPaymentService(context, mercadoPago, TestConfiguration()),
            null!,
            null!)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext()
            }
        };

        var result = await controller.ResumePayment(order.Id, CancellationToken.None);
        var response = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result.Result);
        var body = Assert.IsType<LojaVirtual.Controllers.ApiOrderResponse>(response.Value);

        Assert.Equal(order.Id, body.Id);
        Assert.Equal("PAC", body.ShippingMethod);
        Assert.Equal(35m, body.Total);
        Assert.Equal("https://example.com/counting", body.CheckoutUrl);
        Assert.Equal(1, mercadoPago.CreateCalls);
    }

    [Fact]
    public async Task External_failure_keeps_intent_for_a_same_key_retry()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        var order = SeedOrder(context, stock: 5, quantity: 2);
        var existingAttempt = await context.OrderPaymentAttempts.SingleAsync();
        context.OrderPaymentAttempts.Remove(existingAttempt);
        await context.SaveChangesAsync();

        var mercadoPago = new FailingOnceMercadoPagoService();
        var service = new OrderPaymentService(context, mercadoPago, TestConfiguration());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnsurePreferenceAsync(order));
        var pending = await context.OrderPaymentAttempts.SingleAsync();
        var firstKey = pending.IdempotencyKey;

        var attempt = await service.EnsurePreferenceAsync(order);

        Assert.Equal(firstKey, mercadoPago.Keys[1]);
        Assert.Equal(firstKey, attempt.IdempotencyKey);
        Assert.Equal(PaymentStatusWorkflow.AwaitingPayment, attempt.Status);
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

    [Fact]
    public async Task Preference_error_includes_marketplace_code_and_cause_without_request_secrets()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        var order = SeedOrder(context, stock: 5, quantity: 1);
        var configuration = TestConfiguration();
        configuration["Storefront:BaseUrl"] = "https://store.example.test";
        configuration["MercadoPago:WebhookUrl"] = "https://hooks.example.test/mercadopago";

        var service = new MercadoPagoService(
            new HttpClient(new ErrorResponseHandler()),
            configuration);

        var exception = await Assert.ThrowsAsync<MercadoPagoException>(() =>
            service.CreatePreferenceAsync(order, "safe-test-idempotency-key"));

        Assert.Contains("invalid_back_urls", exception.Message);
        Assert.Contains("back_urls must use HTTPS", exception.Message);
        Assert.DoesNotContain("test-access-token", exception.Message);
        Assert.DoesNotContain("safe-test-idempotency-key", exception.Message);
    }

    [Fact]
    public async Task Preference_error_logs_only_safe_date_and_api_diagnostics()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        var order = SeedOrder(context, stock: 5, quantity: 1);
        var configuration = TestConfiguration();
        configuration["Storefront:BaseUrl"] = "https://store.example.test";
        configuration["MercadoPago:WebhookUrl"] = "https://hooks.example.test/mercadopago";
        var handler = new DiagnosticErrorResponseHandler();
        var logger = new RecordingLogger<MercadoPagoService>();
        var service = new MercadoPagoService(new HttpClient(handler), configuration, logger);

        await Assert.ThrowsAsync<MercadoPagoException>(() =>
            service.CreatePreferenceAsync(order, "safe-test-idempotency-key"));

        var message = Assert.Single(logger.Messages);
        Assert.Contains("StatusCode=400", message);
        Assert.Contains("RequestId=safe-request-id", message);
        Assert.Contains("ExpiresJsonType=Boolean", message);
        Assert.Contains("ExpirationDateFromJsonType=String", message);
        Assert.Contains("ExpirationDateToJsonType=String", message);
        Assert.Contains("bad_request", message);
        Assert.Contains("error_parsing_date", message);
        Assert.Contains("date-code: date invalid", message);
        Assert.DoesNotContain("test-access-token", message);
        Assert.DoesNotContain(order.Email, message);
        Assert.DoesNotContain("Teste", message);
    }

    [Fact]
    public async Task Local_http_storefront_url_is_rejected_before_marketplace_call()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        var order = SeedOrder(context, stock: 5, quantity: 1);
        var configuration = TestConfiguration();
        configuration["Storefront:BaseUrl"] = "http://localhost:3001";
        configuration["MercadoPago:WebhookUrl"] = "https://hooks.example.test/mercadopago";

        var service = new MercadoPagoService(
            new HttpClient(new ThrowingHandler()),
            configuration);

        var exception = await Assert.ThrowsAsync<MercadoPagoException>(() =>
            service.CreatePreferenceAsync(order, "safe-test-idempotency-key"));

        Assert.Contains("localhost não é aceito", exception.Message);
    }

    [Fact]
    public async Task Preference_dates_use_milliseconds_and_explicit_utc_offset()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        var order = SeedOrder(context, stock: 5, quantity: 1);
        var expectedExpiration = DateTimeOffset.UtcNow.AddMinutes(5);
        var attempt = await context.OrderPaymentAttempts.SingleAsync();
        attempt.IdempotencyKey = "safe-test-idempotency-key";
        attempt.ExpiresAtUtc = expectedExpiration.UtcDateTime;
        await context.SaveChangesAsync();

        var configuration = TestConfiguration();
        configuration["Storefront:BaseUrl"] = "https://store.example.test";
        configuration["MercadoPago:WebhookUrl"] = "https://hooks.example.test/mercadopago";
        var handler = new CapturingErrorResponseHandler();
        var service = new MercadoPagoService(new HttpClient(handler), configuration);

        await Assert.ThrowsAsync<MercadoPagoException>(() =>
            service.CreatePreferenceAsync(order, "safe-test-idempotency-key"));

        using var document = System.Text.Json.JsonDocument.Parse(handler.RequestBody!);
        var root = document.RootElement;
        Assert.True(root.GetProperty("expires").GetBoolean());
        var fromText = root.GetProperty("expiration_date_from").GetString()!;
        var toText = root.GetProperty("expiration_date_to").GetString()!;
        var from = DateTimeOffset.ParseExact(fromText, "yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture);
        var to = DateTimeOffset.ParseExact(toText, "yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture);

        Assert.Matches(
            "^\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}\\.\\d{3}-03:00$",
            fromText);
        Assert.Matches(
            "^\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}\\.\\d{3}-03:00$",
            toText);
        Assert.True(from <= handler.ReceivedAtUtc);
        Assert.True(to > handler.ReceivedAtUtc);
        Assert.True(to > from);
        Assert.Equal(
            expectedExpiration.ToOffset(TimeSpan.FromHours(-3))
                .ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture),
            toText);
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
        configuration["MercadoPago:AccessToken"] = "test-access-token";
        configuration["MercadoPago:WebhookSecret"] = "test-webhook-secret";
        return configuration;
    }

    private class FakeMercadoPagoService : IMercadoPagoService
    {
        public virtual Task<MercadoPagoPreferenceResult> CreatePreferenceAsync(Order order, string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MercadoPagoPreferenceResult("pref-1", "https://example.com/checkout"));
        public Task<MercadoPagoPaymentResult?> GetPaymentAsync(string paymentId, CancellationToken cancellationToken = default) =>
            Task.FromResult<MercadoPagoPaymentResult?>(null);
        public bool IsValidWebhookSignature(string? signature, string? requestId, string? dataId) => true;
        public Task<string> CriarPreferenciaAsync(Order order, string? idempotencyKey = null) => Task.FromResult("https://example.com/checkout");
    }

    private class CountingMercadoPagoService : FakeMercadoPagoService
    {
        public int CreateCalls { get; private set; }
        public string? LastIdempotencyKey { get; private set; }

        public override Task<MercadoPagoPreferenceResult> CreatePreferenceAsync(
            Order order,
            string? idempotencyKey = null,
            CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            LastIdempotencyKey = idempotencyKey;
            return Task.FromResult(new MercadoPagoPreferenceResult("pref-counted", "https://example.com/counting"));
        }
    }

    private sealed class FailingOnceMercadoPagoService : CountingMercadoPagoService
    {
        public List<string?> Keys { get; } = [];

        public override Task<MercadoPagoPreferenceResult> CreatePreferenceAsync(
            Order order,
            string? idempotencyKey = null,
            CancellationToken cancellationToken = default)
        {
            Keys.Add(idempotencyKey);
            if (Keys.Count == 1) throw new InvalidOperationException("simulated external failure");
            return Task.FromResult(new MercadoPagoPreferenceResult("pref-retry", "https://example.com/retry"));
        }
    }

    private sealed class FakeOrderAccessService(int orderId) : IOrderAccessCredentialService
    {
        public Task<PreparedOrderAccess> PrepareAsync(int requestedOrderId, Microsoft.AspNetCore.Http.HttpRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PreparedOrderAccess([], DateTime.UtcNow, null));

        public Task<IReadOnlyList<AuthorizedOrderAccess>> ResolveAsync(Microsoft.AspNetCore.Http.HttpRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuthorizedOrderAccess>>([new AuthorizedOrderAccess("token", new LojaVirtual.Models.OrderAccessCredential { OrderId = orderId })]);

        public Task RevokeAllAsync(Microsoft.AspNetCore.Http.HttpRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void WriteCookie(Microsoft.AspNetCore.Http.HttpResponse response, PreparedOrderAccess access) { }
        public void DeleteCookie(Microsoft.AspNetCore.Http.HttpResponse response) { }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    private sealed class ErrorResponseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"error\":\"invalid_back_urls\",\"message\":\"back_urls invalid\",\"cause\":[{\"code\":\"invalid_back_urls\",\"description\":\"back_urls must use HTTPS\"}]}")
            });
    }

    private sealed class DiagnosticErrorResponseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"error\":\"bad_request\",\"message\":\"error_parsing_date\",\"cause\":[{\"code\":\"date-code\",\"description\":\"date invalid\"}]}")
            };
            response.Headers.Add("x-request-id", "safe-request-id");
            return Task.FromResult(response);
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The marketplace call should not happen.");
    }

    private sealed class CapturingErrorResponseHandler : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }
        public DateTimeOffset ReceivedAtUtc { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ReceivedAtUtc = DateTimeOffset.UtcNow;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"error\":\"test\"}")
            };
        }
    }
}
