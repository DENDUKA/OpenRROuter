using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.BillingService.Application.Services;

namespace OpenRussiaRouter.BillingService.Infrastructure.Startup;

internal sealed class YandexMetrikaOfflineConversionOptionsValidator(IConfiguration configuration)
    : IValidateOptions<YandexMetrikaOfflineConversionOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        YandexMetrikaOfflineConversionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        if (options.CounterId <= 0)
        {
            failures.Add($"{YandexMetrikaOfflineConversionOptions.SectionName}:CounterId must be positive.");
        }
        if (string.IsNullOrWhiteSpace(options.OAuthToken))
        {
            failures.Add($"{YandexMetrikaOfflineConversionOptions.SectionName}:OAuthToken is required.");
        }
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri) ||
            (!string.Equals(baseUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            failures.Add($"{YandexMetrikaOfflineConversionOptions.SectionName}:BaseUrl must be an absolute HTTP(S) URL.");
        }
        if (string.IsNullOrWhiteSpace(configuration["ProductAnalytics:ConnectionString"]))
        {
            failures.Add("ProductAnalytics:ConnectionString is required for ClientID resolution.");
        }
        if (!configuration.GetValue("ProductAnalytics:Enabled", false) ||
            !string.Equals(configuration["ProductAnalytics:Provider"], "Postgres", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("ProductAnalytics must be enabled with Provider=Postgres for ClientID resolution.");
        }
        if (options.BatchSize <= 0 || options.WorkerInterval <= TimeSpan.Zero ||
            options.LeaseDuration <= TimeSpan.Zero || options.MissingIdentityRetryInterval <= TimeSpan.Zero ||
            options.UploadStatusPollInterval <= TimeSpan.Zero || options.BaseRetryDelay <= TimeSpan.Zero ||
            options.MaxRetryDelay < options.BaseRetryDelay || options.ConfigurationRetryInterval <= TimeSpan.Zero ||
            options.ExpirationWindow <= TimeSpan.Zero || options.HttpTimeout <= TimeSpan.Zero)
        {
            failures.Add("Yandex Metrica worker intervals and BatchSize must be positive and internally consistent.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
