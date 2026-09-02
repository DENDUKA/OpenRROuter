namespace OpenRussiaRouter.BillingService.Domain;

public static class YandexMetrikaOfflineConversionTargets
{
    public const string TopUpCompleted = "topup_completed";

    public const string FirstApiRequest = "first_api_request";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(
        [TopUpCompleted, FirstApiRequest],
        StringComparer.Ordinal);
}
