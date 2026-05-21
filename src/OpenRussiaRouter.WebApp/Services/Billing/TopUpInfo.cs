namespace OpenRussiaRouter.WebApp.Services.Billing;

public sealed record TopUpInfo(decimal Amount, string Reason, DateTimeOffset CreatedAtUtc);
