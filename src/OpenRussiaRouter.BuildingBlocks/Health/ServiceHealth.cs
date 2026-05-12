namespace OpenRussiaRouter.BuildingBlocks.Health;

public sealed record ServiceHealth(string Service, string Status, DateTimeOffset Utc)
{

    public static ServiceHealth Ok(string service) => new(service, "ok", DateTimeOffset.UtcNow);
}

