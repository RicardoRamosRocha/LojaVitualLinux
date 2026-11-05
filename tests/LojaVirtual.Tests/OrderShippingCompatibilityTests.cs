using LojaVirtual.Data;
using LojaVirtual.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LojaVirtual.Tests;

public sealed class OrderShippingCompatibilityTests
{
    [Fact]
    public async Task Shipping_fields_are_nullable_and_persist_without_changing_legacy_orders()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var context = new AppDbContext(options))
        {
            context.Database.EnsureCreated();
            var legacyOrder = new Order
            {
                CustomerName = "Cliente legado",
                Email = "legacy@example.com",
                Phone = "31999999999",
                Total = 35m,
                ShippingMethod = "PAC",
                Status = "Pendente"
            };
            context.Orders.Add(legacyOrder);
            await context.SaveChangesAsync();

            Assert.Null(legacyOrder.ShippingCost);
            Assert.Null(legacyOrder.ShippingQuoteId);
        }

        await using (var context = new AppDbContext(options))
        {
            var legacyOrder = await context.Orders.SingleAsync();
            legacyOrder.ShippingCost = 12.50m;
            legacyOrder.ShippingProvider = "Melhor Envio";
            legacyOrder.ShippingServiceId = "1";
            legacyOrder.ShippingServiceName = "SEDEX";
            legacyOrder.ShippingDeliveryTime = 4;
            legacyOrder.ShippingQuotedAtUtc = DateTimeOffset.UtcNow;
            legacyOrder.ShippingQuoteId = "quote-test";
            await context.SaveChangesAsync();
        }

        await using (var context = new AppDbContext(options))
        {
            var order = await context.Orders.SingleAsync();
            Assert.Equal("PAC", order.ShippingMethod);
            Assert.Equal(12.50m, order.ShippingCost);
            Assert.Equal("Melhor Envio", order.ShippingProvider);
            Assert.Equal("1", order.ShippingServiceId);
            Assert.Equal("SEDEX", order.ShippingServiceName);
            Assert.Equal(4, order.ShippingDeliveryTime);
            Assert.Equal("quote-test", order.ShippingQuoteId);
        }
    }
}
