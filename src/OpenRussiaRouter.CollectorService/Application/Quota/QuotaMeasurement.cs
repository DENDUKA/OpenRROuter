namespace OpenRussiaRouter.CollectorService.Application.Quota;

public sealed record QuotaMeasurement(
    string NodeId,
    string Provider,
    string ConnectionId,
    string AccountName,
    string QuotaName,
    double Remaining,
    double? Percent,
    IReadOnlyList<string> ModelIds);
