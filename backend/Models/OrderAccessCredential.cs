namespace LojaVirtual.Models;

public sealed class OrderAccessCredential
{
    public long Id { get; set; }
    public int OrderId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public Order Order { get; set; } = null!;
}
