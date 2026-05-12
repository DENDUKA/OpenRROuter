namespace OpenRussiaRouter.ApiGateway.Application.Extensions;

public sealed class GatewayGrpcOptions
{

    public string Auth { get; set; } = "http://localhost:5001";

    public string Billing { get; set; } = "http://localhost:5002";

    public string ControlPlane { get; set; } = "http://localhost:5003";
}

public sealed class GatewayHttpClientOptions
{

    public TimeSpan PooledConnectionIdleTimeout { get; set; } = Timeout.InfiniteTimeSpan;

    public TimeSpan KeepAlivePingDelay { get; set; } = TimeSpan.FromSeconds(60);

    public TimeSpan KeepAlivePingTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
