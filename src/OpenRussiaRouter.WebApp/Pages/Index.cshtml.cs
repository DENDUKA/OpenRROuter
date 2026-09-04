using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Services;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.Catalog;
using OpenRussiaRouter.WebApp.Services.Markup;
using OpenRussiaRouter.Contracts.Auth;
using System.Globalization;
using System.Threading.Tasks;

namespace OpenRussiaRouter.WebApp.Pages;

public class IndexModel : PageModelBase 
{
    private readonly BillingProvider _billingProvider;
    private readonly AuthProvider _authProvider;
    private readonly ApiBaseUrlResolver _baseUrlResolver;
    private readonly ILogger<IndexModel> _logger;

    public bool HasBalance { get; set; }
    public bool HasApiKey { get; set; }
    public string ApiBaseUrl { get; private set; } = "";
    public string V1BaseUrl => $"{ApiBaseUrl}/v1";
    public IReadOnlyList<ProviderInfo> Providers { get; private set; } = [];
    public IReadOnlyList<FlagshipModelCard> FlagshipModels { get; private set; } = [];

    public bool StartBalanceEnabled { get; set; }
    public decimal StartBalanceAmountUsd { get; set; }
    public bool FirstTopUpEnabled { get; set; }
    public decimal FirstTopUpPercent { get; set; }
    public bool HasPaidTopUp { get; set; }
    public bool FirstTopUpBonusAvailable { get; set; }

    public IndexModel(
        BillingProvider billingProvider,
        AuthProvider authProvider,
        ApiBaseUrlResolver baseUrlResolver,
        ILogger<IndexModel> logger)
    {
        _billingProvider = billingProvider;
        _authProvider = authProvider;
        _baseUrlResolver = baseUrlResolver;
        _logger = logger;
    }

    public async Task OnGetAsync()
    {
        var ct = HttpContext.RequestAborted;

        // Fire all tasks eagerly to maximize parallelism
        var providersTask = _billingProvider.ListModelProvidersAsync(ct);
        var promoTask = _billingProvider.GetPromotionSettingsAsync(ct);
        var modelsTask = _billingProvider.ListPublicModelsAsync(ct);
        var baseUrlTask = _baseUrlResolver.ResolveAsync(Request, ct);

        // Await each with its own error handling
        try { Providers = await providersTask; }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to load providers for landing page"); }

        try { (StartBalanceEnabled, StartBalanceAmountUsd, FirstTopUpEnabled, FirstTopUpPercent) = await promoTask; }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to load promotion settings for landing page"); }

        try
        {
            FlagshipModels = (await modelsTask)
                .OrderByDescending(static model => model.Priority)
                .ThenBy(static model => model.Id, StringComparer.Ordinal)
                .Select(CreateFlagshipCard)
                .OfType<FlagshipModelCard>()
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load flagship models for landing page");
        }

        try { ApiBaseUrl = await baseUrlTask; }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to resolve API base URL for landing page"); }

        var user = CurrentUser;
        if (user != null)
        {
            // Fire user-specific tasks eagerly
            var balanceTask = _billingProvider.GetBalanceAsync(user.UserId);
            var keysTask = _authProvider.ListApiKeysAsync(user.UserId);
            var hasPaidTopUpTask = _billingProvider.HasUserHadPaidTopUpAsync(user.UserId, ct);

            try
            {
                var balance = await balanceTask;
                HasBalance = balance.Amount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load balance for user {UserId} on landing page", user.UserId);
            }

            try
            {
                var keys = await keysTask;
                HasApiKey = keys.Count > 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load API keys for user {UserId} on landing page", user.UserId);
            }

            try
            {
                HasPaidTopUp = await hasPaidTopUpTask;
                FirstTopUpBonusAvailable = FirstTopUpEnabled && !HasPaidTopUp;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check paid top-up status for user {UserId} on landing page", user.UserId);
            }
        }
    }

    public static string FormatUsdPrice(decimal value) =>
        $"${value.ToString("0.######", CultureInfo.InvariantCulture)}";

    public static string GetInitials(string value) => CatalogFormatting.GetInitials(value);

    private static FlagshipModelCard? CreateFlagshipCard(ModelInfo model)
    {
        if (model is not
            {
                IsFlagship: true,
                OpenRouterInputPrice: { } openRouterInputPrice,
                OpenRouterOutputPrice: { } openRouterOutputPrice,
                OpenRouterCachePrice: { } openRouterCachePrice
            })
        {
            return null;
        }

        return new FlagshipModelCard(
            model.Id,
            model.DisplayName,
            string.IsNullOrWhiteSpace(model.Provider) ? "AI" : model.Provider,
            model.ProviderId,
            ModelDetailRoute.TryBuildPath(model.Id, model.ProviderId, model.Provider) ?? "/models",
            model.InputPrice,
            model.OutputPrice,
            model.CachePrice,
            openRouterInputPrice,
            openRouterOutputPrice,
            openRouterCachePrice);
    }

    public sealed record FlagshipModelCard(
        string Id,
        string DisplayName,
        string Provider,
        string? ProviderId,
        string Path,
        decimal InputPrice,
        decimal OutputPrice,
        decimal CachePrice,
        decimal OpenRouterInputPrice,
        decimal OpenRouterOutputPrice,
        decimal OpenRouterCachePrice)
    {
        public int? MaxDiscountPercent
        {
            get
            {
                var maximum = Math.Max(
                    CalculateDiscountPercent(InputPrice, OpenRouterInputPrice),
                    Math.Max(
                        CalculateDiscountPercent(OutputPrice, OpenRouterOutputPrice),
                        CalculateDiscountPercent(CachePrice, OpenRouterCachePrice)));
                var rounded = (int)Math.Round(maximum, MidpointRounding.AwayFromZero);
                return rounded > 0 ? rounded : null;
            }
        }

        private static decimal CalculateDiscountPercent(decimal price, decimal referencePrice) =>
            referencePrice > 0 && price >= 0 && price < referencePrice
                ? (referencePrice - price) / referencePrice * 100m
                : 0m;
    }
}
