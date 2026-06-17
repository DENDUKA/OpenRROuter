using System.ComponentModel.DataAnnotations;

namespace OpenRussiaRouter.ApiGateway.Application.Extensions;

public sealed class GatewayGrpcOptions
{
    [Required]
    public string Auth { get; set; } = "http://localhost:5001";

    [Required]
    public string Billing { get; set; } = "http://localhost:5002";

    [Required]
    public string ControlPlane { get; set; } = "http://localhost:5003";
}

public sealed class GatewayHttpClientOptions
{
    public TimeSpan PooledConnectionIdleTimeout { get; set; } = Timeout.InfiniteTimeSpan;

    public TimeSpan KeepAlivePingDelay { get; set; } = TimeSpan.FromSeconds(60);

    public TimeSpan KeepAlivePingTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

public sealed class GatewayRetryOptions
{
    [Range(1, 20)]
    public int MaxRouterAttempts { get; set; } = 5;
}
