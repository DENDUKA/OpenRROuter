using OpenRussiaRouter.BillingService.Domain.Entities;
using System.Text.Json.Serialization;

namespace OpenRussiaRouter.BillingService.Infrastructure.YandexMetrika;

internal interface IYandexMetrikaClient
{
    Task<YandexMetrikaUploading> UploadAsync(
        YandexMetrikaOfflineConversion conversion,
        string clientId,
        CancellationToken ct);

    Task<YandexMetrikaUploading?> FindByCommentAsync(string comment, CancellationToken ct);

    Task<YandexMetrikaUploading> GetUploadingAsync(long uploadId, CancellationToken ct);
}

internal sealed record YandexMetrikaUploading(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("source_quantity")] int SourceQuantity,
    [property: JsonPropertyName("line_quantity")] int LineQuantity,
    [property: JsonPropertyName("comment")] string Comment,
    [property: JsonPropertyName("status")] string Status);
