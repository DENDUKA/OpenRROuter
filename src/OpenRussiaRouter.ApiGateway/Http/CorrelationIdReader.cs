using Microsoft.AspNetCore.Http;
using OpenRussiaRouter.BuildingBlocks.Telemetry;

namespace OpenRussiaRouter.ApiGateway.Http;

public static class CorrelationIdReader
{

    public static string Get(HttpRequest request)
    {
        if (request.Headers.TryGetValue(CorrelationIds.HeaderName, out var values))
        {
            var value = values.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value!;
            }
        }

        return CorrelationIds.New();
    }
}

