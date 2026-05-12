using OpenRussiaRouter.Contracts.ControlPlane;

namespace OpenRussiaRouter.ControlPlane.Domain.Entities;

/// <summary>
/// External 9Router node registered by an admin.
/// </summary>
public sealed class RouterNode
{
    /// <summary>
    /// Creates a router node.
    /// </summary>
    /// <param name="id">Node id.</param>
    /// <param name="name">Display name.</param>
    /// <param name="endpointUrl">HTTPS endpoint.</param>
    /// <param name="tokenSecretName">Secret reference for node token.</param>
    /// <param name="capacityWeight">Relative capacity weight.</param>
    /// <param name="createdAtUtc">Creation timestamp.</param>
    public RouterNode(
        string id,
        string name,
        Uri endpointUrl,
        string tokenSecretName,
        int capacityWeight,
        DateTimeOffset createdAtUtc)
    {
        Id = RequireValue(id, nameof(id));
        Name = RequireValue(name, nameof(name));
        EndpointUrl = ValidateEndpoint(endpointUrl);
        TokenSecretName = RequireValue(tokenSecretName, nameof(tokenSecretName));
        CapacityWeight = capacityWeight > 0 ? capacityWeight : throw new ArgumentOutOfRangeException(nameof(capacityWeight));
        CreatedAtUtc = createdAtUtc;
    }

    /// <summary>
    /// Node id.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Display name.
    /// </summary>
    public string Name { get; private set; }

    /// <summary>
    /// HTTPS endpoint.
    /// </summary>
    public Uri EndpointUrl { get; private set; }

    /// <summary>
    /// Token secret reference.
    /// </summary>
    public string TokenSecretName { get; private set; }

    /// <summary>
    /// Relative capacity weight.
    /// </summary>
    public int CapacityWeight { get; private set; }

    /// <summary>
    /// Whether the node participates in routing.
    /// </summary>
    public bool Enabled { get; private set; } = true;

    /// <summary>
    /// Current health status.
    /// </summary>
    public RouterNodeStatus Status { get; private set; } = RouterNodeStatus.Unhealthy;

    /// <summary>
    /// Current in-flight request count.
    /// </summary>
    public int InFlightRequests { get; private set; }

    /// <summary>
    /// Last health check timestamp.
    /// </summary>
    public DateTimeOffset? LastCheckedAtUtc { get; private set; }

    /// <summary>
    /// Last health check latency in milliseconds.
    /// </summary>
    public int? LastHealthLatencyMs { get; private set; }

    /// <summary>
    /// Last health check error code.
    /// </summary>
    public string? LastHealthErrorCode { get; private set; }

    /// <summary>
    /// Creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>
    /// Updates editable fields.
    /// </summary>
    /// <param name="name">Display name.</param>
    /// <param name="endpointUrl">HTTPS endpoint.</param>
    /// <param name="tokenSecretName">Token secret reference.</param>
    /// <param name="capacityWeight">Relative capacity weight.</param>
    public void Update(string name, Uri endpointUrl, string tokenSecretName, int capacityWeight)
    {
        Name = RequireValue(name, nameof(name));
        EndpointUrl = ValidateEndpoint(endpointUrl);
        TokenSecretName = RequireValue(tokenSecretName, nameof(tokenSecretName));
        CapacityWeight = capacityWeight > 0 ? capacityWeight : throw new ArgumentOutOfRangeException(nameof(capacityWeight));
    }

    /// <summary>
    /// Enables the node.
    /// </summary>
    public void Enable() => Enabled = true;

    /// <summary>
    /// Disables the node and removes it from routing.
    /// </summary>
    public void Disable()
    {
        Enabled = false;
        Status = RouterNodeStatus.Disabled;
    }

    /// <summary>
    /// Applies latest health check status.
    /// </summary>
    /// <param name="status">Health status.</param>
    /// <param name="checkedAtUtc">Health check timestamp.</param>
    public void ApplyHealth(
        RouterNodeStatus status,
        DateTimeOffset checkedAtUtc,
        int? latencyMs = null,
        string? errorCode = null)
    {
        if (!Enabled)
        {
            Status = RouterNodeStatus.Disabled;
            LastCheckedAtUtc = checkedAtUtc;
            LastHealthLatencyMs = latencyMs;
            LastHealthErrorCode = errorCode;
            return;
        }

        Status = status == RouterNodeStatus.Disabled ? RouterNodeStatus.Unhealthy : status;
        LastCheckedAtUtc = checkedAtUtc;
        LastHealthLatencyMs = latencyMs;
        LastHealthErrorCode = string.IsNullOrWhiteSpace(errorCode) ? null : errorCode.Trim();
    }

    /// <summary>
    /// Increments in-flight requests.
    /// </summary>
    public void Lease() => InFlightRequests++;

    /// <summary>
    /// Decrements in-flight requests.
    /// </summary>
    public void Release()
    {
        if (InFlightRequests > 0)
        {
            InFlightRequests--;
        }
    }

    private static Uri ValidateEndpoint(Uri endpointUrl)
    {
        ArgumentNullException.ThrowIfNull(endpointUrl);
        if (!string.Equals(endpointUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Router node endpoint must use HTTPS.", nameof(endpointUrl));
        }

        return endpointUrl;
    }

    private static string RequireValue(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be empty.", name);
        }

        return value.Trim();
    }
}
