using System.Net;
using LojaVirtual.Data;
using LojaVirtual.Models;
using LojaVirtual.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace LojaVirtual.Tests;

public sealed class MelhorEnvioShipmentServiceTests
{
    [Fact]
    public async Task Prepare_creates_one_cart_entry_and_is_idempotent()
    {
        await using var db = await Fixture.CreateAsync();
        var handler = new RecordingHandler();
        var service = db.CreateService(handler);

        var first = await service.PrepareAsync(db.OrderId);
        var second = await service.PrepareAsync(db.OrderId);

        Assert.Equal(OrderShipmentStatus.InCart, second.Status);
        Assert.Equal(first.ProviderShipmentId, second.ProviderShipmentId);
        Assert.Equal(1, handler.Calls);
        Assert.Equal("/api/v2/me/cart", handler.LastPath);
    }

    [Fact]
    public async Task Purchase_is_blocked_without_call_when_feature_flag_is_disabled()
    {
        await using var db = await Fixture.CreateAsync();
        var handler = new RecordingHandler();
        var service = db.CreateService(handler);
        await service.PrepareAsync(db.OrderId);

        var error = await Assert.ThrowsAsync<ShippingOperationException>(() => service.PurchaseAsync(db.OrderId));

        Assert.Equal("shipping_purchase_disabled", error.Code);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Incomplete_address_is_rejected_before_external_call()
    {
        await using var db = await Fixture.CreateAsync(incompleteAddress: true);
        var handler = new RecordingHandler();
        var service = db.CreateService(handler);

        var error = await Assert.ThrowsAsync<ShippingOperationException>(() => service.PrepareAsync(db.OrderId));

        Assert.Equal("incomplete_address", error.Code);
        Assert.Equal(0, handler.Calls);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? LastPath { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; LastPath = request.RequestUri?.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"id\":\"shipment-123\",\"price\":\"18.50\"}") });
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<AppDbContext> _options;
        public int OrderId { get; private set; }
        private Fixture(SqliteConnection connection, DbContextOptions<AppDbContext> options) { _connection = connection; _options = options; }

        public static async Task<Fixture> CreateAsync(bool incompleteAddress = false)
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            await using var context = new AppDbContext(options); await context.Database.EnsureCreatedAsync();
            var product = new Product { Name = "Brinco", Price = 50m, Stock = 2, IsActive = true };
            var order = new Order { CustomerName = "Cliente", Email = "cliente@example.com", Phone = "31999999999", Address = "Rua A", ZipCode = "30110012", AddressNumber = incompleteAddress ? null : "10", AddressComplement = "", Neighborhood = incompleteAddress ? null : "Centro", City = incompleteAddress ? null : "Belo Horizonte", State = incompleteAddress ? null : "MG", ShippingMethod = "1", ShippingServiceId = "1", ShippingServiceName = "SEDEX", Status = OrderStatusWorkflow.Preparing, Total = 50m, IdempotencyKey = Guid.NewGuid().ToString("N") };
            order.Items.Add(new OrderItem { Product = product, Quantity = 1, UnitPrice = 50m });
            order.PaymentAttempts.Add(new OrderPaymentAttempt { AttemptNumber = 1, Status = PaymentStatusWorkflow.Approved, Amount = 50m, IdempotencyKey = Guid.NewGuid().ToString("N") });
            context.Orders.Add(order); await context.SaveChangesAsync();
            return new Fixture(connection, options) { OrderId = order.Id };
        }

        public IMelhorEnvioShipmentService CreateService(HttpMessageHandler handler)
        {
            var http = new HttpClient(handler) { BaseAddress = new Uri("https://sandbox.melhorenvio.com.br") };
            var token = new FakeTokenProvider();
            var options = Options.Create(new ShippingOptions { BaseUrl = "https://sandbox.melhorenvio.com.br", PurchaseEnabled = false, StandardPackage = new StandardShippingPackage { Enabled = true, MaxItems = 2, HeightCm = 5, WidthCm = 12, LengthCm = 16, WeightKg = .2m }, Sender = new SenderShippingOptions { Name = "Angel", Email = "envio@example.com", Phone = "31999999999", PostalCode = "31615150", Address = "Rua Loja", Number = "1", District = "Centro", City = "Belo Horizonte", State = "MG", CompanyDocument = "12345678000100" } });
            return new MelhorEnvioShipmentService(http, new TestFactory(_options), token, options, NullLogger<MelhorEnvioShipmentService>.Instance);
        }
        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
    }
    private sealed class TestFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext> { public AppDbContext CreateDbContext() => new(options); public Task<AppDbContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new AppDbContext(options)); }
    private sealed class FakeTokenProvider : IMelhorEnvioAccessTokenProvider { public Task<string> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult("test-token"); }
}
