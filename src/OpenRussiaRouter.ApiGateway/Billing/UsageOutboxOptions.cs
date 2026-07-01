namespace OpenRussiaRouter.ApiGateway.Billing;

public sealed class UsageOutboxOptions
{
    public int BatchSize { get; set; } = 50;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    public int MaxAttempts { get; set; } = 10;

    public TimeSpan BaseBackoff { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(1);
}
