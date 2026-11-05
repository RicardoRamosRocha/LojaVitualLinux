using LojaVirtual.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LojaVirtual.Controllers;

[ApiController]
[Route("api/integrations/melhorenvio")]
[Authorize(Policy = "AdminOnly")]
public sealed class MelhorEnvioDiagnosticsController : ControllerBase
{
    private readonly IMelhorEnvioConnectivityDiagnostics _diagnostics;

    public MelhorEnvioDiagnosticsController(IMelhorEnvioConnectivityDiagnostics diagnostics) => _diagnostics = diagnostics;

    [HttpGet("connectivity-diagnostics")]
    [EnableRateLimiting("melhorenvio-diagnostics")]
    public async Task<ActionResult<MelhorEnvioConnectivityDiagnostic>> Connectivity(CancellationToken cancellationToken)
    {
        return Ok(await _diagnostics.CheckAsync(cancellationToken));
    }
}
