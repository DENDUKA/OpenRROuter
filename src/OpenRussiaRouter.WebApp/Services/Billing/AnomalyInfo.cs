namespace OpenRussiaRouter.WebApp.Services.Billing;

public sealed record AnomalyInfo(string Id, string RequestId, string Type, string Message, DateTimeOffset CreatedAt);