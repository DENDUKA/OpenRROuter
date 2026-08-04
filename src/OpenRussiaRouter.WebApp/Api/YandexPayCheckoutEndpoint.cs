using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.Payments;
using OpenRussiaRouter.WebApp.Services.State;

namespace OpenRussiaRouter.WebApp.Api;

/// <summary>
/// Administrator-only endpoint that creates a Yandex Pay checkout session.
/// </summary>
public static class YandexPayCheckoutEndpoint
{
    /// <summary>The browser endpoint used by the Yandex Pay SDK.</summary>
    public const string Route = "/workspace/credits/yandexpay/checkout";

    /// <summary>Maps the Yandex Pay checkout endpoint.</summary>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(Route, HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { Roles = "Admin" });
    }

    /// <summary>
    /// Validates a checkout request and delegates its creation to the provider-agnostic payment
    /// checkout flow. The route metadata enforces authentication and the Administrator role.
    /// </summary>
    public static async Task<IResult> HandleAsync(
        YandexPayCheckoutRequest request,
        HttpContext http,
        PaymentCheckoutService checkoutService,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        if (!TryValidate(request, out var amount, out var currency, out var error))
        {
            return Results.BadRequest(new { error });
        }

        var userId = http.User.GetUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Results.Unauthorized();
        }

        try
        {
            var checkout = await checkoutService.CreateAsync(
                userId,
                amount,
                currency,
                providerName: "yandexpay",
                clientToken: Guid.NewGuid().ToString("N"),
                ct);

            return Results.Ok(new { payUrl = checkout.PayUrl });
        }
        catch (BillingException ex)
        {
            return Results.BadRequest(new { error = ex.ErrorCode });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            loggerFactory.CreateLogger("YandexPayCheckoutEndpoint").LogError(
                ex,
                "Yandex Pay checkout creation failed for user {UserId}.",
                userId);
            return Results.Problem(
                title: "Unable to create Yandex Pay checkout",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static bool TryValidate(
        YandexPayCheckoutRequest request,
        out decimal amount,
        out string currency,
        out string error)
    {
        amount = request.Amount;
        currency = request.Currency?.Trim().ToUpperInvariant() ?? string.Empty;

        if (currency is not ("RUB" or "USD" or "EUR"))
        {
            error = "unsupported_currency";
            return false;
        }

        if (amount <= 0 || decimal.Round(amount, 2, MidpointRounding.AwayFromZero) != amount)
        {
            error = "invalid_amount";
            return false;
        }

        if (!request.LegalConsent)
        {
            error = "legal_consent_required";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

/// <summary>JSON payload sent by the same-origin Yandex Pay SDK integration.</summary>
public sealed record YandexPayCheckoutRequest(decimal Amount, string? Currency, bool LegalConsent);
