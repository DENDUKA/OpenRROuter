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
            data = models.Select(m =>
            {
                // OpenRouter-style supported_parameters — this is what OpenAI-compatible clients
                // (Kilo Code / Cline / Roo, etc.) read to decide whether to show a reasoning picker.
                // The custom `capabilities.reasoning` below is kept for our own UI; clients ignore it.
                // ORR is a passthrough proxy, so the standard chat params are all forwarded as-is.
                var supportedParameters = new List<string>
                {
                    "tools", "tool_choice", "max_tokens", "temperature", "top_p",
                    "stop", "frequency_penalty", "presence_penalty", "seed", "response_format"
                };
                if (m.SupportsReasoning)
                {
                    supportedParameters.Add("reasoning");
                    supportedParameters.Add("reasoning_effort");
                }

                return new
                {
                    id = m.Id,
                    @object = "model",
                    created = 0,
                    owned_by = "openrussiarouter",
                    supported_parameters = supportedParameters,
                    capabilities = new Dictionary<string, object?>
                    {
                        ["reasoning"] = m.SupportsReasoning
                            ? new Dictionary<string, object?>
                            {
                                ["supported"] = true,
                                ["efforts"] = m.AllowedReasoningEfforts ?? [],
                                ["default"] = m.DefaultReasoningEffort
                            }
                            : new Dictionary<string, object?>
                            {
                                ["supported"] = false
                            }
                    }
                };
            })
        };

        context.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(context.Response.Body, responseBody);
    }
}
