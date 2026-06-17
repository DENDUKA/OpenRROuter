using System.Threading;
using OpenRussiaRouter.BuildingBlocks.Common;
using OpenRussiaRouter.Contracts.ControlPlane;

namespace OpenRussiaRouter.ControlPlane.Domain.Entities;

public sealed class RouterNode
{
    private int _inFlightRequests;

    public RouterNode(
        string id,
        string name,
        Uri endpointUrl,
        string controlToken,
        int capacityWeight,
        DateTimeOffset createdAtUtc)
    {
        Id = Preconditions.RequireValue(id, nameof(id));
        Name = Preconditions.RequireValue(name, nameof(name));
        EndpointUrl = ValidateEndpoint(endpointUrl);
        ControlToken = Preconditions.RequireValue(controlToken, nameof(controlToken));
        CapacityWeight = capacityWeight > 0 ? capacityWeight : throw new ArgumentOutOfRangeException(nameof(capacityWeight));
        CreatedAtUtc = createdAtUtc;
    }

    public string Id { get; }

    public string Name { get; private set; }

    public Uri EndpointUrl { get; private set; }

    public string ControlToken { get; private set; }

    public int CapacityWeight { get; private set; }

    public bool Enabled { get; private set; } = true;

    public RouterNodeStatus Status { get; private set; } = RouterNodeStatus.Unhealthy;

    public int InFlightRequests => Volatile.Read(ref _inFlightRequests);

    public DateTimeOffset? LastCheckedAtUtc { get; private set; }

    public int? LastHealthLatencyMs { get; private set; }

    public string? LastHealthErrorCode { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; }

    public void Update(string name, Uri endpointUrl, string controlToken, int capacityWeight)
    {
        Name = Preconditions.RequireValue(name, nameof(name));
        EndpointUrl = ValidateEndpoint(endpointUrl);
        ControlToken = Preconditions.RequireValue(controlToken, nameof(controlToken));
        CapacityWeight = capacityWeight > 0 ? capacityWeight : throw new ArgumentOutOfRangeException(nameof(capacityWeight));
    }

    public void UpdateControlToken(string controlToken)
    {
        ControlToken = Preconditions.RequireValue(controlToken, nameof(controlToken));
    }

    public void Enable() => Enabled = true;

    public void Disable()
    {
        Enabled = false;
        Status = RouterNodeStatus.Disabled;
    }

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

    public void Lease() => Interlocked.Increment(ref _inFlightRequests);

    public void Release()
    {
        var current = Volatile.Read(ref _inFlightRequests);
        while (current > 0)
        {
            var original = Interlocked.CompareExchange(ref _inFlightRequests, current - 1, current);
            if (original == current) break;
            current = Volatile.Read(ref _inFlightRequests);
        }
    }

    // HTTP is allowed for internal cluster services (e.g. k8s ClusterIP endpoints).
    private static Uri ValidateEndpoint(Uri endpointUrl)
    {
        ArgumentNullException.ThrowIfNull(endpointUrl);
        var scheme = endpointUrl.Scheme;
        var isHttps = string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        var isHttp = string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
        if (!isHttps && !isHttp)
        {
            throw new ArgumentException("Router node endpoint must use HTTP or HTTPS.", nameof(endpointUrl));
        }

        return endpointUrl;
    }

}
