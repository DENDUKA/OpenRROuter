namespace OpenRussiaRouter.BuildingBlocks.Analytics;

/// <summary>
/// Default <see cref="IProductAnalytics"/> implementation. Discards every event.
/// Used when product analytics is disabled, unconfigured, or in environments that
/// must not emit telemetry (development, tests).
/// </summary>
public sealed class NoOpProductAnalytics : IProductAnalytics
{
    public void UserVerified(string userId, string signupMethod)
    {
    }

    public void ApiKeyCreated(string userId)
    {
    }

    public void ApiRequestCompleted(string userId, ApiRequestAnalytics data)
    {
    }

    public void PaymentCompleted(string userId, PaymentAnalytics data)
    {
    }
}