using System.Net;
using System.Text.Json;

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

public sealed record RouterProviderModelsResponse
{
    public string Provider { get; init; } = string.Empty;
    public string Alias { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string? Warning { get; init; }
    public string? ConnectionId { get; init; }
    public string? NodeType { get; init; }
    public IReadOnlyList<RouterProviderModelInfo> Models { get; init; } = [];
}

public sealed record RouterProviderModelInfo
{
    public string Id { get; init; } = string.Empty;
    public string PublicId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public bool Enabled { get; init; }
    public IReadOnlyList<string> Sources { get; init; } = [];
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

public sealed record NodeModelsResponse
{
    public IReadOnlyList<NodeModelInfo> Data { get; init; } = [];
}

public sealed record NodeModelInfo
{
    public string Id { get; init; } = string.Empty;
}

public sealed class RouterNodeProviderException : Exception
{
    public RouterNodeProviderException(
        HttpStatusCode statusCode,
        string? routerError,
        string message)
        : base(message)
    {
        StatusCode = statusCode;
        RouterError = routerError;
    }

    public HttpStatusCode StatusCode { get; }

    public string? RouterError { get; }
}
