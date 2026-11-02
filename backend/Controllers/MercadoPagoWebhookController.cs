using LojaVirtual.Services;
using LojaVirtual.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LojaVirtual.Controllers;

[ApiController]
[Route("api/payments/mercadopago")]
public sealed class MercadoPagoWebhookController : ControllerBase
{
    private readonly IMercadoPagoService _mercadoPago;
    private readonly OrderPaymentService _payments;

    public MercadoPagoWebhookController(IMercadoPagoService mercadoPago, OrderPaymentService payments)
    {
        _mercadoPago = mercadoPago;
        _payments = payments;
    }

    [AllowAnonymous]
    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook([FromQuery(Name = "data.id")] string? dataId, CancellationToken cancellationToken)
    {
        var signature = Request.Headers["x-signature"].FirstOrDefault();
        var requestId = Request.Headers["x-request-id"].FirstOrDefault();
        if (!_mercadoPago.IsValidWebhookSignature(signature, requestId, dataId)) return Unauthorized();

        var payment = await _mercadoPago.GetPaymentAsync(dataId!, cancellationToken);
        if (payment is null) return NotFound();

        await _payments.ProcessPaymentAsync(payment, cancellationToken);
        return Ok();
    }
}
