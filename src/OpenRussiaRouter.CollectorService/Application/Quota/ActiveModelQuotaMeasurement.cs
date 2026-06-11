namespace OpenRussiaRouter.CollectorService.Application.Quota;

public sealed record ActiveModelQuotaMeasurement(
    string NodeId,
    string Provider,
    string ConnectionId,
    string ModelId,
    string RouterModelId,
    double Percent);
