namespace LojaVirtual.Models;

public sealed class OrderAccessRecoveryToken
{
    public long Id { get; set; }
    public int OrderId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public Order Order { get; set; } = null!;
}
