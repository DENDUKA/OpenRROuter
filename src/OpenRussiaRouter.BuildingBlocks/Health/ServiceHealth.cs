namespace OpenRussiaRouter.BuildingBlocks.Health;

/// <summary>
/// Health response returned by service health endpoints.
/// </summary>
/// <param name="Service">Service name.</param>
/// <param name="Status">Health status.</param>
/// <param name="Utc">UTC timestamp.</param>
public sealed record ServiceHealth(string Service, string Status, DateTimeOffset Utc)
{
    /// <summary>
    /// Creates an "ok" health response for a service.
    /// </summary>
    /// <param name="service">Service name.</param>
    /// <returns>Healthy response.</returns>
    public static ServiceHealth Ok(string service) => new(service, "ok", DateTimeOffset.UtcNow);
}

