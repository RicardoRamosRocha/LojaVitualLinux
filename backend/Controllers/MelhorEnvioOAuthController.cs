using LojaVirtual.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LojaVirtual.Controllers;

[ApiController]
[Route("api/integrations/melhorenvio")]
public sealed class MelhorEnvioOAuthController : ControllerBase
{
    private readonly IMelhorEnvioOAuthService _oauth;

    public MelhorEnvioOAuthController(IMelhorEnvioOAuthService oauth) => _oauth = oauth;

    [HttpPost("authorize")]
    [Authorize(Policy = "AdminOnly")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AuthorizeProvider(CancellationToken cancellationToken)
    {
        try
        {
            var authorizationUri = await _oauth.CreateAuthorizationUriAsync(cancellationToken);
            return Redirect(authorizationUri.ToString());
        }
        catch (ShippingOAuthException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                code = "oauth_unavailable",
                message = "A autorização do provedor não está disponível."
            });
        }
    }

    [HttpGet("callback")]
    [AllowAnonymous]
    public async Task<IActionResult> Callback(
        [FromQuery] string? state,
        [FromQuery] string? code,
        [FromQuery] string? error,
        CancellationToken cancellationToken)
    {
        try
        {
            await _oauth.ExchangeAuthorizationCodeAsync(state ?? string.Empty, code ?? string.Empty, cancellationToken);
            return Ok(new
            {
                message = "Autorização concluída com sucesso."
            });
        }
        catch (ShippingOAuthException exception)
        {
            var statusCode = exception.StatusCode is >= 400 and <= 499
                ? StatusCodes.Status400BadRequest
                : StatusCodes.Status503ServiceUnavailable;
            return StatusCode(statusCode, new
            {
                code = "oauth_failed",
                message = string.IsNullOrWhiteSpace(error)
                    ? "Não foi possível concluir a autorização."
                    : "A autorização foi recusada ou não pôde ser validada."
            });
        }
    }
}
