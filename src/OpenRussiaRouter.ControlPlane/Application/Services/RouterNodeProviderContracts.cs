using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenRussiaRouter.ControlPlane.Application.Services;

public sealed record RouterProviderQuotaResponse
{
    public IReadOnlyList<RouterProviderQuotaEntry> Providers { get; init; } = [];
}

public sealed record RouterProviderQuotaEntry
{
    public string ConnectionId { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string AuthType { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public string? TestStatus { get; init; }
    public bool QuotaSupported { get; init; }
    public JsonElement? Usage { get; init; }
    public string? Error { get; init; }
}

public sealed record RouterProviderStatusResponse
{
    public RouterProviderConnectionInfo? Connection { get; init; }
}

public sealed record RouterProviderConnectionInfo
{
    public string Id { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string AuthType { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public string? TestStatus { get; init; }
    public JsonElement? ProviderSpecificData { get; init; }
}

public sealed record RouterIntegrationKeysResponse
{
    public IReadOnlyList<RouterIntegrationKeyInfo> Keys { get; init; } = [];
}

public sealed record RouterIntegrationKeyInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string KeyPreview { get; init; } = string.Empty;
    public string MachineId { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public string ManagedBy { get; init; } = string.Empty;
    public string CreatedAt { get; init; } = string.Empty;
}

public sealed record RouterIntegrationKeyCreateResponse
{
    public bool Created { get; init; }
    public string Id { get; init; } = string.Empty;
}

public sealed record RouterIntegrationKeyDeleteResponse
{
    public bool Deleted { get; init; }
    public string Id { get; init; } = string.Empty;
}

public sealed record RouterIntegrationKeySyncEntry
{
    public string Name { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
}

public sealed record RouterIntegrationKeysSyncResponse
{
    [JsonPropertyName("synced")]
    public int AddedCount { get; init; }
}

public sealed class RouterNodeProviderException : Exception
{
    public RouterNodeProviderException(
        HttpStatusCode statusCode,
        string? routerError,
        string message,
        string? requestMethod = null,
        string? requestUri = null,
        string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        RouterError = routerError;
        RequestMethod = requestMethod;
        RequestUri = requestUri;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }

    public string? RouterError { get; }

    public string? RequestMethod { get; }

    public string? RequestUri { get; }

    public string? ResponseBody { get; }
}
