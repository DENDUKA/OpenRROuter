namespace OpenRussiaRouter.BuildingBlocks.Telemetry;

/// <summary>
/// Helpers for correlation ids used in logs and responses.
/// </summary>
public static class CorrelationIds
{
    public const string HeaderName = "x-correlation-id";

    /// <summary>
    /// Creates a compact unique correlation id.
    /// </summary>
    /// <returns>New correlation id.</returns>
    public static string New() => Guid.NewGuid().ToString("N");
}

