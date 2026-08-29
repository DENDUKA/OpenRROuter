using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using OpenRussiaRouter.ApiGateway.Billing;

namespace OpenRussiaRouter.ApiGateway.Application.Endpoints;

public sealed class ModelsHandler(IBillingProvider billingProvider, IMemoryCache cache)
{
    private const string CacheKey = "v1_models_response";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public async Task HandleAsync(HttpContext context)
    {
        if (cache.TryGetValue<byte[]>(CacheKey, out var cachedBytes))
        {
            context.Response.ContentType = "application/json";
            await context.Response.Body.WriteAsync(cachedBytes, context.RequestAborted);
            return;
        }

        var models = await billingProvider.ListPublicModelsAsync(context.RequestAborted);
        var responseBody = new
        {
            @object = "list",
            data = models.Select(model => BuildModel(model))
        };

        await using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, responseBody, JsonOptions);
        var bytes = stream.ToArray();
        cache.Set(CacheKey, bytes, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheTtl,
            Size = bytes.Length
        });

        context.Response.ContentType = "application/json";
        await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
    }

    private static object BuildModel(GatewayPublicModel model)
    {
        var supportsReasoning = model.SupportsReasoning && model.AllowedReasoningEfforts is { Count: > 0 };
        var supportedParameters = new List<string>
        {
            "frequency_penalty", "max_tokens", "presence_penalty", "response_format",
            "seed", "stop", "temperature", "tool_choice", "tools", "top_p"
        };
        if (supportsReasoning)
        {
            supportedParameters.Add("include_reasoning");
            supportedParameters.Add("reasoning");
            supportedParameters.Add("reasoning_effort");
        }
        supportedParameters.Sort(StringComparer.Ordinal);

        Dictionary<string, object?>? reasoning = null;
        Dictionary<string, object?> capabilities = new(StringComparer.Ordinal);
        if (supportsReasoning)
        {
            reasoning = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["mandatory"] = false,
                ["supported_efforts"] = model.AllowedReasoningEfforts,
                ["default_effort"] = model.DefaultReasoningEffort
            };
            capabilities["reasoning"] = new Dictionary<string, object?>
            {
                ["supported"] = true,
                ["efforts"] = model.AllowedReasoningEfforts,
                ["default"] = model.DefaultReasoningEffort
            };
        }

        return new
        {
            id = model.Id,
            name = model.DisplayName,
            @object = "model",
            created = 0,
            owned_by = "openrussiarouter",
            supported_parameters = supportedParameters,
            reasoning,
            capabilities
        };
    }
}
