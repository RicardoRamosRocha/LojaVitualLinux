namespace LojaVirtual.Models;

public sealed class OrderPaymentAttempt
{
    public long Id { get; set; }
    public int OrderId { get; set; }
    public string Provider { get; set; } = "MercadoPago";
    public int AttemptNumber { get; set; }
    public string? PreferenceId { get; set; }
    public string? CheckoutUrl { get; set; }
    public string? PaymentId { get; set; }
    public string Status { get; set; } = "AguardandoPagamento";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "BRL";
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAtUtc { get; set; }
    public Order Order { get; set; } = null!;
}
