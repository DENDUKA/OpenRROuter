using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.BuildingBlocks.Health;

namespace OpenRussiaRouter.ControlPlane.Infrastructure.Http;

[ApiController]
[Route("[controller]")]
public sealed class HealthController : ControllerBase
{
    [HttpGet("/healthz")]
    public IActionResult Healthz() => Ok(ServiceHealth.Ok("control-plane"));

    [HttpGet("/ready")]
    public IActionResult Ready() => Ok(ServiceHealth.Ok("control-plane"));

    [HttpGet("/")]
    public IActionResult Info() => Ok(new
    {
        service = "OpenRussiaRouter Router Control Plane",
        contracts = new[] { "SelectRouterNode", "ReleaseRouterNode", "RegisterRouterNode", "DisableRouterNode", "ListRouterNodes" }
    });
}