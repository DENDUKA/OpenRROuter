using System.Text.Json;
using Microsoft.AspNetCore.Http;
using OpenRussiaRouter.ApiGateway.Auth;
using OpenRussiaRouter.ApiGateway.Billing;
using OpenRussiaRouter.ApiGateway.Http;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.BuildingBlocks.Telemetry;

namespace OpenRussiaRouter.ApiGateway.Application.Endpoints;

/// <summary>Обработчик GET /v1/models — возвращает публичный каталог моделей из BillingService.
/// Формат ответа выровнен с OpenRouter /v1/models для авто-обнаружения reasoning (top-level
/// `reasoning` объект + paired `reasoning`/`include_reasoning` в supported_parameters).</summary>
public sealed class ModelsHandler(IBillingProvider billingProvider)
{
    // Null values are omitted to match OpenRouter's omit-if-absent convention — in particular
    // the top-level `reasoning` field must not appear at all on non-reasoning models (its
    // presence IS the signal that a model can reason).
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

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
                // OpenRouter-style `supported_parameters`. The base set is the common
                // chat-completions subset; reasoning-capable models additionally advertise
                // `reasoning` + `include_reasoning` (always paired — every OpenRouter model
                // that exposes one exposes the other) plus `reasoning_effort` for the few
                // clients that key off that string (GLM / Kimi / MiniMax style). The list is
                // emitted alphabetically to match OpenRouter's deterministic ordering.
                var supportedParameters = new List<string>
                {
                    "frequency_penalty", "max_tokens", "presence_penalty", "response_format",
                    "seed", "stop", "temperature", "tool_choice", "tools", "top_p"
                };
                if (m.SupportsReasoning)
                {
                    supportedParameters.Add("include_reasoning");
                    supportedParameters.Add("reasoning");
                    supportedParameters.Add("reasoning_effort");
                }
                supportedParameters.Sort(StringComparer.Ordinal);

                // OpenRouter-style top-level `reasoning` object — the canonical signal that
                // Cline / Roo Code / Kilo Code (and anything else that follows the OpenRouter
                // /v1/models contract) look for to decide whether to render a reasoning picker.
                // Optional sub-fields are omitted entirely when not applicable (OpenRouter
                // convention), never serialized as null.
                Dictionary<string, object?>? reasoningObject = null;
                if (m.SupportsReasoning)
                {
                    reasoningObject = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        // ORR never forces reasoning on — the client may always omit it.
                        ["mandatory"] = false
                    };
                    if (m.AllowedReasoningEfforts is { Count: > 0 })
                    {
                        reasoningObject["supported_efforts"] = m.AllowedReasoningEfforts;
                    }
                    if (!string.IsNullOrWhiteSpace(m.DefaultReasoningEffort))
                    {
                        reasoningObject["default_effort"] = m.DefaultReasoningEffort;
                    }
                }

                return new
                {
                    id = m.Id,
                    name = m.DisplayName,
                    @object = "model",
                    created = 0,
                    owned_by = "openrussiarouter",
                    supported_parameters = supportedParameters,
                    // OpenRouter-style top-level reasoning object (omitted on non-reasoning models).
                    reasoning = reasoningObject,
                    // Custom `capabilities.reasoning` block kept for the ORR opencode plugin
                    // and our own WebApp UI. OpenRouter-compatible clients ignore this —
                    // they key off the top-level `reasoning` object above instead.
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
        await JsonSerializer.SerializeAsync(context.Response.Body, responseBody, JsonOptions);
    }
}
