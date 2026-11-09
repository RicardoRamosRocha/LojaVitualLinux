namespace LojaVirtual.Models;

public sealed class OrderNotificationOutbox
{
    public long Id { get; set; }
    public int OrderId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public DateTime NextAttemptAtUtc { get; set; } = DateTime.UtcNow;
    public string? LeaseToken { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public string? LastError { get; set; }

    public Order Order { get; set; } = null!;
}
