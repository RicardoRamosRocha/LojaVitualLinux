using LojaVirtual.Models;
using LojaVirtual.Services;



namespace LojaVirtual.Web.Services;

public interface IMercadoPagoService
{
    Task<MercadoPagoPreferenceResult> CreatePreferenceAsync(
        Order order,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);
    Task<MercadoPagoPaymentResult?> GetPaymentAsync(string paymentId, CancellationToken cancellationToken = default);
    bool IsValidWebhookSignature(string? signature, string? requestId, string? dataId);
    Task<string> CriarPreferenciaAsync(Order order, string? idempotencyKey = null);
}
