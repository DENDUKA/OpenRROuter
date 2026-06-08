using System.Text.Json;
using Microsoft.AspNetCore.Http;
using OpenRussiaRouter.ApiGateway.Auth;
using OpenRussiaRouter.ApiGateway.Billing;
using OpenRussiaRouter.ApiGateway.Http;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.BuildingBlocks.Telemetry;

namespace OpenRussiaRouter.ApiGateway.Application.Endpoints;

/// <summary>Обработчик GET /v1/models — возвращает публичный каталог моделей из BillingService.</summary>
public sealed class ModelsHandler(IBillingProvider billingProvider)
{
    public async Task HandleAsync(HttpContext context)
    {
        var requestId = CorrelationIdReader.Get(context.Request);

        if (!BearerTokenReader.TryRead(context.Request, out _))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await GatewayErrors.WriteAsync(context, "authentication_error", ErrorCodes.AuthenticationRequired, "Bearer token required", requestId);
            return;
        }

        var models = await billingProvider.ListPublicModelsAsync(context.RequestAborted);

        var responseBody = new
        {
            @object = "list",
            data = models.Select(m => new
            {
                id = m.Id,
                @object = "model",
                created = 0,
                owned_by = "openrussiarouter",
                capabilities = new Dictionary<string, object?>
                {
                    ["reasoning"] = m.SupportsReasoning
                        ? new Dictionary<string, object?>
                        {
                            ["supported"] = true,
                            ["efforts"] = m.AllowedReasoningEfforts ?? []
                        }
                        : new Dictionary<string, object?>
                        {
                            ["supported"] = false
                        }
                }
            })
        };

        context.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(context.Response.Body, responseBody);
    }
}
