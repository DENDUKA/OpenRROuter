using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.BuildingBlocks.Health;

namespace OpenRussiaRouter.BillingService.Infrastructure.Http;

[ApiController]
[Route("[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet("/healthz")]
    public IActionResult Healthz() => Ok(ServiceHealth.Ok("billing-service"));

    [HttpGet("/ready")]
    public IActionResult Ready() => Ok(ServiceHealth.Ok("billing-service"));

    [HttpGet("/")]
    public IActionResult Info() => Ok(new
    {
        service = "OpenRussiaRouter Billing / Usage Service",
        contracts = new[] { "CheckBalanceAndModel", "RecordUsage", "GetApiKeyLimitSettings", "UpsertApiKeyLimitSettings", "ListApiKeyLimitSummaries", "ListPublicModels", "GetBalance", "CreateVirtualTopUp", "UpsertModelPrice", "ListUsage", "ListBillingAnomalies" }
    });
}
