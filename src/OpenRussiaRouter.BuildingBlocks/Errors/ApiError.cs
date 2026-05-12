namespace OpenRussiaRouter.BuildingBlocks.Errors;

/// <summary>
/// Stable machine-readable error returned by public HTTP APIs or internal adapters.
/// </summary>
/// <param name="Type">Broad error category.</param>
/// <param name="Code">Specific machine-readable error code.</param>
/// <param name="Message">Safe user-facing message.</param>
/// <param name="CorrelationId">Correlation id for support and log lookup.</param>
public sealed record ApiError(
    string Type,
    string Code,
    string Message,
    string? CorrelationId = null);

