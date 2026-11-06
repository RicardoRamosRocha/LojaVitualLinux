namespace LojaVirtual.Services;

public sealed record MercadoPagoPreferenceResult(string PreferenceId, string InitPoint);

public sealed record MercadoPagoPaymentResult(
    string PaymentId,
    string Status,
    string? ExternalReference,
    string? PreferenceId,
    decimal Amount,
    string Currency,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt);

public enum PaymentProcessingResult
{
    Processed,
    Duplicate,
    Invalid
}

public static class PaymentStatusWorkflow
{
    public const string CreatingPreference = "CriandoPreferencia";
    public const string AwaitingPayment = "AguardandoPagamento";
    public const string Processing = "EmProcessamento";
    public const string Approved = "Aprovado";
    public const string Rejected = "Recusado";
    public const string Cancelled = "Cancelado";
    public const string Expired = "Expirado";
    public const string Refunded = "Reembolsado";
    public const string StockReview = "AprovadoAguardandoEstoque";

    public static string FromMercadoPago(string status) => status switch
    {
        "approved" => Approved,
        "in_process" or "authorized" => Processing,
        "pending" => AwaitingPayment,
        "rejected" => Rejected,
        "cancelled" => Cancelled,
        "refunded" or "charged_back" => Refunded,
        _ => Processing
    };

    public static bool IsTerminal(string status) => status is Rejected or Cancelled or Expired or Refunded;
}
