using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.BuildingBlocks.Health;

namespace OpenRussiaRouter.AuthService.Infrastructure.Http;

[ApiController]
[Route("[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet("/healthz")]
    public IActionResult Healthz() => Ok(ServiceHealth.Ok("auth-service"));

    [HttpGet("/ready")]
    public IActionResult Ready() => Ok(ServiceHealth.Ok("auth-service"));

    [HttpGet("/")]
    public IActionResult Info() => Ok(new
    {
        service = "OpenRussiaRouter Auth Service",
        contracts = new[] { "ValidateApiKey", "CreateInvite", "CompleteInvite", "CreateApiKey", "DisableApiKey" }
    });
}