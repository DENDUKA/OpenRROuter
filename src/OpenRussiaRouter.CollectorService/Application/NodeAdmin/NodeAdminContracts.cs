using System.Net;

namespace OpenRussiaRouter.CollectorService.Application.NodeAdmin;

public sealed record NodeProvidersWithModelsResponse
{
    public IReadOnlyList<NodeProviderWithModels> Connections { get; init; } = [];
}

public sealed record NodeProviderWithModels
{
    public string Id { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string AuthType { get; init; } = string.Empty;
    public string? Name { get; init; }
    public bool IsActive { get; init; }
    public string? TestStatus { get; init; }
    public IReadOnlyList<NodeProviderModelInfo> Models { get; init; } = [];
}

public sealed record NodeProviderModelInfo
{
    public string Id { get; init; } = string.Empty;
    public string PublicId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public bool Enabled { get; init; }
    public IReadOnlyList<string> Sources { get; init; } = [];
    public NodeCapsInfo? Caps { get; init; }
}

public sealed record NodeCapsInfo
{
    public bool Reasoning { get; init; }
    public string? ThinkingFormat { get; init; }
    public bool ThinkingCanDisable { get; init; }
    public IReadOnlyList<string> ThinkingOptions { get; init; } = [];
}

public sealed record NodeModelsResponse
{
    public IReadOnlyList<NodeModelInfo> Data { get; init; } = [];
}

public sealed record NodeModelInfo
{
    public string Id { get; init; } = string.Empty;
}

public sealed class NodeAdminException : Exception
{
    public NodeAdminException(
        HttpStatusCode statusCode,
        string message,
        string? requestMethod = null,
        string? requestUri = null,
        string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        RequestMethod = requestMethod;
        RequestUri = requestUri;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }
    public string? RequestMethod { get; }
    public string? RequestUri { get; }
    public string? ResponseBody { get; }
}
