using LojaVirtual.Services;
using LojaVirtual.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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

        MercadoPagoPaymentResult? payment;
        try
        {
            payment = await _mercadoPago.GetPaymentAsync(dataId!, cancellationToken);
        }
        catch (MercadoPagoException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (payment is null) return NotFound();

        try
        {
            var result = await _payments.ProcessPaymentResultAsync(payment, cancellationToken);
            return result switch
            {
                PaymentProcessingResult.Processed => Ok(),
                PaymentProcessingResult.Duplicate => Ok(),
                PaymentProcessingResult.Invalid => BadRequest(new
                {
                    code = "payment_event_invalid",
                    message = "O evento de pagamento não corresponde a um pedido válido."
                }),
                _ => StatusCode(StatusCodes.Status503ServiceUnavailable)
            };
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        catch (DbUpdateException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }
}
