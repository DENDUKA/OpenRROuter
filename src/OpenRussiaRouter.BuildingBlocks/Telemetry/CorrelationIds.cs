namespace OpenRussiaRouter.BuildingBlocks.Telemetry;

public static class CorrelationIds
{
    public const string HeaderName = "x-correlation-id";

    public static string New() => Guid.NewGuid().ToString("N");
}

