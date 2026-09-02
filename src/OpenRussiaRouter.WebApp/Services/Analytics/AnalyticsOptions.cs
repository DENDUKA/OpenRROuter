namespace OpenRussiaRouter.WebApp.Services.Analytics;

/// <summary>
/// Web-analytics tags rendered in the shared layout. Bind from configuration section
/// "Analytics". Both values are optional: with no tag/counter id configured nothing is
/// emitted (dev and tests stay clean), with one configured only that snippet is rendered.
/// </summary>
public sealed class AnalyticsOptions
{
    public const string SectionName = "Analytics";

    /// <summary>Google Analytics 4 measurement id (G-XXXXXXX). Empty disables the gtag snippet.</summary>
    public string? GoogleTagId { get; set; }

    /// <summary>Yandex Metrika counter id (digits). Empty disables the Metrika snippet.</summary>
    public string? YandexCounterId { get; set; }

    public bool YandexTopUpCompletedClientEnabled { get; set; } = true;
}
