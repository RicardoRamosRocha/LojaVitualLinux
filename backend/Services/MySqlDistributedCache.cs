using LojaVirtual.Data;
using LojaVirtual.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace LojaVirtual.Services;

public sealed class MySqlDistributedCache : IDistributedCache
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public MySqlDistributedCache(IDbContextFactory<AppDbContext> contextFactory) => _contextFactory = contextFactory;

    public byte[]? Get(string key) => GetAsync(key).GetAwaiter().GetResult();

    public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(token);
        var entry = await context.Set<DistributedCacheEntry>().SingleOrDefaultAsync(item => item.Id == key, token);
        if (entry is null) return null;
        if (IsExpired(entry))
        {
            context.Remove(entry);
            await context.SaveChangesAsync(token);
            return null;
        }

        if (entry.SlidingExpiration is { } sliding)
        {
            entry.ExpiresAt = GetSlidingExpiration(entry.AbsoluteExpiration, sliding);
            await context.SaveChangesAsync(token);
        }
        return entry.Value;
    }

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => SetAsync(key, value, options).GetAwaiter().GetResult();

    public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(token);
        var entry = await context.Set<DistributedCacheEntry>().SingleOrDefaultAsync(item => item.Id == key, token);
        if (entry is null)
        {
            entry = new DistributedCacheEntry { Id = key };
            context.Add(entry);
        }

        entry.Value = value;
        entry.AbsoluteExpiration = options.AbsoluteExpiration;
        entry.SlidingExpiration = options.SlidingExpiration;
        entry.ExpiresAt = GetExpiration(options);
        await context.SaveChangesAsync(token);
    }

    public void Refresh(string key) => RefreshAsync(key).GetAwaiter().GetResult();

    public async Task RefreshAsync(string key, CancellationToken token = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(token);
        var entry = await context.Set<DistributedCacheEntry>().SingleOrDefaultAsync(item => item.Id == key, token);
        if (entry is null || entry.SlidingExpiration is not { } sliding) return;
        entry.ExpiresAt = GetSlidingExpiration(entry.AbsoluteExpiration, sliding);
        await context.SaveChangesAsync(token);
    }

    public void Remove(string key) => RemoveAsync(key).GetAwaiter().GetResult();

    public async Task RemoveAsync(string key, CancellationToken token = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(token);
        var entry = await context.Set<DistributedCacheEntry>().SingleOrDefaultAsync(item => item.Id == key, token);
        if (entry is null) return;
        context.Remove(entry);
        await context.SaveChangesAsync(token);
    }

    private static DateTimeOffset? GetExpiration(DistributedCacheEntryOptions options)
    {
        var absolute = options.AbsoluteExpiration ??
            (options.AbsoluteExpirationRelativeToNow is { } relative
                ? DateTimeOffset.UtcNow.Add(relative)
                : null);
        if (options.SlidingExpiration is { } sliding)
        {
            return GetSlidingExpiration(absolute, sliding);
        }

        if (absolute is { } absoluteExpiration) return absoluteExpiration;
        return null;
    }

    private static DateTimeOffset GetSlidingExpiration(DateTimeOffset? absoluteExpiration, TimeSpan slidingExpiration)
    {
        var slidingExpirationAt = DateTimeOffset.UtcNow.Add(slidingExpiration);
        return absoluteExpiration is { } absolute && absolute < slidingExpirationAt
            ? absolute
            : slidingExpirationAt;
    }

    private static bool IsExpired(DistributedCacheEntry entry) => entry.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow;
}
