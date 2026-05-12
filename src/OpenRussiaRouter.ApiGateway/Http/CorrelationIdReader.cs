using Microsoft.AspNetCore.Http;
using OpenRussiaRouter.BuildingBlocks.Telemetry;

namespace OpenRussiaRouter.ApiGateway.Http;

/// <summary>
/// Reads or creates correlation ids for public HTTP requests.
/// </summary>
public static class CorrelationIdReader
{
    /// <summary>
    /// Gets a correlation id from the request or creates a new one.
    /// </summary>
    /// <param name="request">HTTP request.</param>
    /// <returns>Correlation id.</returns>
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

