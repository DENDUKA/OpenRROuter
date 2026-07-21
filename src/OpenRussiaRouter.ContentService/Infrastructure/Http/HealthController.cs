using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.BuildingBlocks.Health;

namespace OpenRussiaRouter.ContentService.Infrastructure.Http;

[ApiController]
[Route("[controller]")]
public sealed class HealthController : ControllerBase
{
    [HttpGet("/healthz")]
    public IActionResult Healthz() => Ok(ServiceHealth.Ok("content"));

    [HttpGet("/ready")]
    public IActionResult Ready() => Ok(ServiceHealth.Ok("content"));

    [HttpGet("/")]
    public IActionResult Info() => Ok(new
    {
        service = "OpenRussiaRouter Content Service",
        contracts = new[] { "ListTools", "GetTool", "UpsertTool", "UploadToolImage", "ListToolCategories" }
    });
}
