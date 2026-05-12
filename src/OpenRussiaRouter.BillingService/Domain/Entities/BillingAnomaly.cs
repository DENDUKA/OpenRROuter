namespace OpenRussiaRouter.BillingService.Domain.Entities;

/// <summary>
/// Billing anomaly requiring admin review.
/// </summary>
/// <param name="Id">Anomaly id.</param>
/// <param name="RequestId">Related request id.</param>
/// <param name="Type">Anomaly type.</param>
/// <param name="Message">Safe admin-facing message.</param>
/// <param name="CreatedAtUtc">Creation timestamp.</param>
public sealed record BillingAnomaly(
    string Id,
    string RequestId,
    string Type,
    string Message,
    DateTimeOffset CreatedAtUtc);

