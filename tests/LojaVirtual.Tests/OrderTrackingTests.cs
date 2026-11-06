using LojaVirtual.Controllers;
using LojaVirtual.Data;
using LojaVirtual.Models;
using LojaVirtual.Services;
using LojaVirtual.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LojaVirtual.Tests;

public sealed class OrderTrackingTests
{
    [Theory]
    [InlineData(OrderStatusWorkflow.Pending, OrderStatusWorkflow.Preparing)]
    [InlineData(OrderStatusWorkflow.Preparing, OrderStatusWorkflow.Shipped)]
    [InlineData(OrderStatusWorkflow.Shipped, OrderStatusWorkflow.Delivered)]
    public void Allows_only_the_next_operational_transition(string current, string next)
    {
        Assert.True(OrderStatusWorkflow.IsAllowedTransition(current, next));
        Assert.False(OrderStatusWorkflow.IsAllowedTransition(next, current));
    }

    [Fact]
    public async Task Admin_status_update_is_persisted_and_returned_only_to_authorized_owner()
    {
        await using var database = await TestDatabase.CreateAsync();
        var order = await database.SeedOrderAsync(OrderStatusWorkflow.Preparing);
        var admin = new OrdersController(database.Context)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            TempData = new TempDataDictionary(new DefaultHttpContext(), new TestTempDataProvider())
        };

        var update = await admin.UpdateStatus(order.Id, OrderStatusWorkflow.Shipped);

        Assert.IsType<RedirectToActionResult>(update);
        database.Context.ChangeTracker.Clear();
        Assert.Equal(OrderStatusWorkflow.Shipped, (await database.Context.Orders.SingleAsync(item => item.Id == order.Id)).Status);

        var authorizedApi = new ApiOrdersController(
            database.Context, null!, new FakeOrderAccessService(order.Id), null!, null!, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var authorized = await authorizedApi.GetAuthorizedOrders(CancellationToken.None);
        var payload = Assert.IsAssignableFrom<IReadOnlyList<ApiOrderResponse>>(Assert.IsType<OkObjectResult>(authorized.Result).Value);
        Assert.Equal(OrderStatusWorkflow.Shipped, Assert.Single(payload).Status);

        var unauthorizedApi = new ApiOrdersController(
            database.Context, null!, new FakeOrderAccessService(null), null!, null!, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var unauthorized = await unauthorizedApi.GetAuthorizedOrders(CancellationToken.None);
        Assert.IsType<UnauthorizedResult>(unauthorized.Result);
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public AppDbContext Context { get; }

        private TestDatabase(SqliteConnection connection, AppDbContext context)
        {
            this.connection = connection;
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

        public async Task<Order> SeedOrderAsync(string status)
        {
            var product = new Product { Name = "Produto de acompanhamento", Price = 10m, Stock = 5, IsActive = true };
            var order = new Order
            {
                CustomerName = "Cliente teste",
                Email = "cliente@example.com",
                Phone = "11999999999",
                Address = "Rua de teste, 20",
                ShippingMethod = "PAC",
                ShippingCost = 15m,
                Total = 25m,
                Status = status,
                IdempotencyKey = Guid.NewGuid().ToString("N"),
                IdempotencyFingerprint = "tracking-test"
            };
            order.Items.Add(new OrderItem { Product = product, Quantity = 1, UnitPrice = 10m });
            order.PaymentAttempts.Add(new OrderPaymentAttempt
            {
                AttemptNumber = 1,
                Status = PaymentStatusWorkflow.Approved,
                Amount = order.Total,
                IdempotencyKey = "tracking-attempt"
            });
            Context.Orders.Add(order);
            await Context.SaveChangesAsync();
            return order;
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class FakeOrderAccessService(int? orderId) : IOrderAccessCredentialService
    {
        public Task<PreparedOrderAccess> PrepareAsync(int requestedOrderId, HttpRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PreparedOrderAccess([], DateTime.UtcNow, null));

        public Task<IReadOnlyList<AuthorizedOrderAccess>> ResolveAsync(HttpRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuthorizedOrderAccess>>(orderId is null
                ? []
                : [new AuthorizedOrderAccess("test-token", new OrderAccessCredential { OrderId = orderId.Value })]);

        public Task RevokeAllAsync(HttpRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void WriteCookie(HttpResponse response, PreparedOrderAccess access) { }
        public void DeleteCookie(HttpResponse response) { }
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
