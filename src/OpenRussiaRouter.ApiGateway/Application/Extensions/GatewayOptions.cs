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

public sealed class UsageOutboxOptions
{
    public bool Enabled { get; set; } = true;

    public int BatchSize { get; set; } = 50;

    public int PollIntervalSeconds { get; set; } = 5;

    public int BaseBackoffSeconds { get; set; } = 2;

    public int MaxBackoffSeconds { get; set; } = 300;

    public int MaxAttempts { get; set; } = 20;

    public int LeaseSeconds { get; set; } = 120;
}
