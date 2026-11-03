using LojaVirtual.Data;
using LojaVirtual.Models;
using LojaVirtual.Services;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LojaVirtual.Tests;

public sealed class ReliabilityTests
{
    [Fact]
    public async Task MySql_distributed_cache_preserves_values_and_isolates_keys()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        var cache = new MySqlDistributedCache(new TestContextFactory(() => CreateContext(connection)));

        await cache.SetAsync("session-a", [1, 2, 3], new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(2)
        });
        await cache.SetAsync("session-b", [9], new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(2)
        });

        Assert.Equal(new byte[] { 1, 2, 3 }, await cache.GetAsync("session-a"));
        Assert.Equal(new byte[] { 9 }, await cache.GetAsync("session-b"));
        Assert.Null(await cache.GetAsync("missing-session"));
    }

    [Fact]
    public async Task MySql_distributed_cache_discards_expired_values()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        var cache = new MySqlDistributedCache(new TestContextFactory(() => CreateContext(connection)));

        await cache.SetAsync("expired", [1], new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = DateTimeOffset.UtcNow.AddMinutes(-1)
        });

        Assert.Null(await cache.GetAsync("expired"));
    }

    [Fact]
    public async Task Sliding_expiration_never_extends_past_absolute_expiration()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        var cache = new MySqlDistributedCache(new TestContextFactory(() => CreateContext(connection)));
        var absoluteExpiration = DateTimeOffset.UtcNow.AddMinutes(1);

        await cache.SetAsync("bounded", [1], new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = absoluteExpiration,
            SlidingExpiration = TimeSpan.FromHours(1)
        });

        var stored = await context.Set<DistributedCacheEntry>().SingleAsync(item => item.Id == "bounded");
        Assert.True(stored.ExpiresAt <= absoluteExpiration);
    }

    [Fact]
    public void Data_protection_key_entity_is_part_of_the_application_model()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        var context = CreateContext(connection);
        Assert.NotNull(context.Model.FindEntityType(typeof(DataProtectionKey)));
    }

    private static AppDbContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var context = new AppDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    private sealed class TestContextFactory(Func<AppDbContext> create) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => create();
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(create());
    }
}
