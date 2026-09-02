using System.Net;

namespace OpenRussiaRouter.BillingService.Infrastructure.YandexMetrika;

internal sealed class YandexMetrikaApiException(
    HttpStatusCode statusCode,
    string message,
    TimeSpan? retryAfter = null) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    public TimeSpan? RetryAfter { get; } = retryAfter;
}
