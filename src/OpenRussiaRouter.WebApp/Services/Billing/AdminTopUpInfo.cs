namespace OpenRussiaRouter.WebApp.Services.Billing;

public sealed record AdminTopUpInfo(string Id, string UserId, decimal Amount, string Reason, DateTimeOffset CreatedAtUtc);
