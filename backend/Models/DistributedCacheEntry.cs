namespace LojaVirtual.Models;

public sealed class DistributedCacheEntry
{
    public string Id { get; set; } = string.Empty;
    public byte[] Value { get; set; } = [];
    public DateTimeOffset? AbsoluteExpiration { get; set; }
    public TimeSpan? SlidingExpiration { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}
