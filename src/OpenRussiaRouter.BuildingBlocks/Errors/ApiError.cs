namespace OpenRussiaRouter.BuildingBlocks.Errors;

public sealed record ApiError(
    string Type,
    string Code,
    string Message,
    string? CorrelationId = null);

