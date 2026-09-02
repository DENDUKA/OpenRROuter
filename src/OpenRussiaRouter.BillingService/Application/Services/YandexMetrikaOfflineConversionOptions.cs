namespace OpenRussiaRouter.BillingService.Application.Services;

public sealed class YandexMetrikaOfflineConversionOptions
{
    public const string SectionName = "YandexMetrika:OfflineConversions";

    public bool Enabled { get; set; }
    public long CounterId { get; set; }
    public string OAuthToken { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api-metrika.yandex.net/";
    public TimeSpan WorkerInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);
    public TimeSpan MissingIdentityRetryInterval { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan UploadStatusPollInterval { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan ConfigurationRetryInterval { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan ExpirationWindow { get; set; } = TimeSpan.FromDays(20);
    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int BatchSize { get; set; } = 20;
}
