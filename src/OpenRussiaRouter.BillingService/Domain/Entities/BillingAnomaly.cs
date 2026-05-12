namespace OpenRussiaRouter.BillingService.Domain.Entities;

public sealed record BillingAnomaly(
    string Id,
    string RequestId,
    string Type,
    string Message,
    DateTimeOffset CreatedAtUtc);

