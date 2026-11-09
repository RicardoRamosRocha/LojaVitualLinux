namespace LojaVirtual.Models;

public static class OrderShipmentStatus
{
    public const string Preparing = "Preparing";
    public const string InCart = "InCart";
    public const string Purchased = "Purchased";
    public const string Generated = "Generated";
    public const string Printed = "Printed";
    public const string Unknown = "Unknown";
    public const string Failed = "Failed";
}

public sealed class OrderShipment
{
    public long Id { get; set; }
    public int OrderId { get; set; }
    public string Provider { get; set; } = "Melhor Envio";
    public string Status { get; set; } = OrderShipmentStatus.Preparing;
    public string? ProviderShipmentId { get; set; }
    public string? ProviderOrderId { get; set; }
    public string? ServiceId { get; set; }
    public string? ServiceName { get; set; }
    public string? TrackingCode { get; set; }
    public string? TrackingStatus { get; set; }
    public string? LabelUrl { get; set; }
    public decimal? PurchaseAmount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? LastError { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? PurchasedAtUtc { get; set; }
    public DateTime? GeneratedAtUtc { get; set; }
    public DateTime? PrintedAtUtc { get; set; }
    public Order Order { get; set; } = null!;
}
