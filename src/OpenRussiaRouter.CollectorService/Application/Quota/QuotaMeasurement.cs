namespace OpenRussiaRouter.CollectorService.Application.Quota;

public sealed record QuotaMeasurement(
    string NodeId,
    string Provider,
    string ConnectionId,
    double Remaining,
    double? Percent,
    IReadOnlyList<string> ModelIds);
