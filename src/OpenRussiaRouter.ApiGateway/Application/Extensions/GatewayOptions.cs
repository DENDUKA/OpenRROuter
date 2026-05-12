namespace OpenRussiaRouter.ApiGateway.Application.Extensions;

/// <summary>
/// Configuration options for API Gateway gRPC service URLs.
/// </summary>
public sealed class GatewayGrpcOptions
{
    /// <summary>
    /// Auth Service gRPC URL.
    /// </summary>
    public string Auth { get; set; } = "http://localhost:5001";

    /// <summary>
    /// Billing Service gRPC URL.
    /// </summary>
    public string Billing { get; set; } = "http://localhost:5002";

    /// <summary>
    /// Control Plane Service gRPC URL.
    /// </summary>
    public string ControlPlane { get; set; } = "http://localhost:5003";
}

/// <summary>
/// Configuration options for API Gateway HTTP client settings.
/// </summary>
public sealed class GatewayHttpClientOptions
{
    /// <summary>
    /// Pooled connection idle timeout.
    /// </summary>
    public TimeSpan PooledConnectionIdleTimeout { get; set; } = Timeout.InfiniteTimeSpan;

    /// <summary>
    /// Keep-alive ping delay.
    /// </summary>
    public TimeSpan KeepAlivePingDelay { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Keep-alive ping timeout.
    /// </summary>
    public TimeSpan KeepAlivePingTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
