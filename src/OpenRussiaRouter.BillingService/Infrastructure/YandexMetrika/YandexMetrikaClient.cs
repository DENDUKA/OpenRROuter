using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.BillingService.Application.Services;
using OpenRussiaRouter.BillingService.Domain.Entities;

namespace OpenRussiaRouter.BillingService.Infrastructure.YandexMetrika;

internal sealed class YandexMetrikaClient(
    HttpClient httpClient,
    IOptions<YandexMetrikaOfflineConversionOptions> options) : IYandexMetrikaClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly YandexMetrikaOfflineConversionOptions _options = options.Value;

    public async Task<YandexMetrikaUploading> UploadAsync(
        YandexMetrikaOfflineConversion conversion,
        string clientId,
        CancellationToken ct)
    {
        var path = $"management/v1/counter/{_options.CounterId}/offline_conversions/upload" +
            $"?type=BASIC&comment={Uri.EscapeDataString(conversion.Comment)}";
        using var request = CreateRequest(HttpMethod.Post, path);
        using var content = new MultipartFormDataContent();
        var csvContent = new ByteArrayContent(Encoding.UTF8.GetBytes(BuildCsv(conversion, clientId)));
        csvContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv")
        {
            CharSet = "utf-8"
        };
        content.Add(csvContent, "file", "offline-conversion.csv");
        request.Content = content;

        using var response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);
        var payload = await ReadPayloadAsync(response, ct).ConfigureAwait(false);
        return payload.Uploading
            ?? throw new InvalidDataException("Yandex Metrica upload response did not contain uploading.");
    }

    public async Task<YandexMetrikaUploading?> FindByCommentAsync(string comment, CancellationToken ct)
    {
        var path = $"management/v1/counter/{_options.CounterId}/offline_conversions/uploadings" +
            "?type=BASIC&limit=10000&offset=0";
        using var request = CreateRequest(HttpMethod.Get, path);
        using var response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);
        var payload = await ReadPayloadAsync(response, ct).ConfigureAwait(false);
        return payload.Uploadings?.FirstOrDefault(
            item => string.Equals(item.Comment, comment, StringComparison.Ordinal));
    }

    public async Task<YandexMetrikaUploading> GetUploadingAsync(long uploadId, CancellationToken ct)
    {
        var path = $"management/v1/counter/{_options.CounterId}/offline_conversions/uploading/{uploadId}";
        using var request = CreateRequest(HttpMethod.Get, path);
        using var response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);
        var payload = await ReadPayloadAsync(response, ct).ConfigureAwait(false);
        return payload.Uploading
            ?? throw new InvalidDataException("Yandex Metrica status response did not contain uploading.");
    }

    internal static string BuildCsv(YandexMetrikaOfflineConversion conversion, string clientId)
    {
        var timestamp = conversion.OccurredAtUtc.ToUnixTimeSeconds();
        var price = conversion.Price?.ToString("0.######", CultureInfo.InvariantCulture) ?? string.Empty;
        var currency = conversion.Currency ?? string.Empty;
        return string.Join(
            '\n',
            "ClientId,Target,DateTime,Price,Currency",
            string.Join(
                ',',
                EscapeCsv(clientId),
                EscapeCsv(conversion.Target),
                timestamp.ToString(CultureInfo.InvariantCulture),
                price,
                EscapeCsv(currency))) + "\n";
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", _options.OAuthToken);
        return request;
    }

    private static async Task<UploadingPayload> ReadPayloadAsync(
        HttpResponseMessage response,
        CancellationToken ct)
    {
        var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new YandexMetrikaApiException(
                response.StatusCode,
                $"Yandex Metrica API returned {(int)response.StatusCode}: {Truncate(content, 256)}",
                GetRetryAfter(response));
        }

        return JsonSerializer.Deserialize<UploadingPayload>(content, JsonOptions)
            ?? throw new InvalidDataException("Yandex Metrica returned an empty JSON payload.");
    }

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            return delta;
        }

        if (response.Headers.RetryAfter?.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }

        return null;
    }

    private static string EscapeCsv(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;

    private static string Truncate(string value, int maximumLength) =>
        value.Length > maximumLength ? value[..maximumLength] : value;

    private sealed class UploadingPayload
    {
        [JsonPropertyName("uploading")]
        public YandexMetrikaUploading? Uploading { get; set; }

        [JsonPropertyName("uploadings")]
        public IReadOnlyList<YandexMetrikaUploading>? Uploadings { get; set; }
    }
}
