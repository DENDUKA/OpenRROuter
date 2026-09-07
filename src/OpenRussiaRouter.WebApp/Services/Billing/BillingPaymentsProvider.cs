using System.Globalization;
using BillingGrpc = OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService;

namespace OpenRussiaRouter.WebApp.Services.Billing;

/// <summary>
/// gRPC wrapper for payments, top-ups, balance, platform settings, API key limits, and promotions.
/// Extracted from <see cref="BillingProvider"/> (T-061).
/// </summary>
internal sealed class BillingPaymentsProvider(BillingGrpc.BillingServiceClient client)
{
    public async Task<BalanceInfo> GetBalanceAsync(string userId, CancellationToken cancellationToken = default)
    {
        var response = await client.GetBalanceAsync(new OpenRussiaRouter.Contracts.Grpc.Billing.V1.GetBalanceRequest { UserId = userId }, cancellationToken: cancellationToken);
        if (!string.IsNullOrWhiteSpace(response.ErrorCode))
            throw new BillingException(response.ErrorCode);
        return new BalanceInfo(ParseDecimal(response.BalanceUsd));
    }

    public async Task<IReadOnlyList<TopUpInfo>> ListTopUpsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var response = await client.ListTopUpsAsync(
            new OpenRussiaRouter.Contracts.Grpc.Billing.V1.ListTopUpsRequest { UserId = userId },
            cancellationToken: cancellationToken);
        return response.Records.Select(r => new TopUpInfo(
            ParseDecimal(r.AmountUsd),
            r.Reason,
            DateTimeOffset.Parse(r.CreatedAtUtc, CultureInfo.InvariantCulture))).ToList();
    }

    public async Task<(IReadOnlyList<AdminTopUpInfo> Items, int TotalCount)> ListAllTopUpsAsync(
        int pageNumber = 0,
        int pageSize = 50,
        IReadOnlyList<string>? userIds = null,
        decimal? minAmountUsd = null,
        decimal? maxAmountUsd = null,
        CancellationToken cancellationToken = default)
    {
        var request = new OpenRussiaRouter.Contracts.Grpc.Billing.V1.ListAllTopUpsRequest
        {
            PageNumber = pageNumber,
            PageSize = pageSize
        };
        if (userIds is { Count: > 0 }) request.UserIds.AddRange(userIds);
        if (minAmountUsd.HasValue) request.MinAmountUsd = minAmountUsd.Value.ToString(CultureInfo.InvariantCulture);
        if (maxAmountUsd.HasValue) request.MaxAmountUsd = maxAmountUsd.Value.ToString(CultureInfo.InvariantCulture);

        var response = await client.ListAllTopUpsAsync(request, cancellationToken: cancellationToken);

        var items = response.Records.Select(r => new AdminTopUpInfo(
            r.Id,
            r.UserId,
            ParseDecimal(r.AmountUsd),
            r.Reason,
            DateTimeOffset.Parse(r.CreatedAtUtc, CultureInfo.InvariantCulture))).ToList();

        return (items, response.TotalCount);
    }

    public async Task CreateVirtualTopUpAsync(string adminId, string userId, decimal amountUsd, string reason, CancellationToken cancellationToken = default)
    {
        var response = await client.CreateVirtualTopUpAsync(
            new OpenRussiaRouter.Contracts.Grpc.Billing.V1.CreateVirtualTopUpRequest
            {
                AdminId = adminId,
                UserId = userId,
                AmountUsd = amountUsd.ToString(CultureInfo.InvariantCulture),
                Reason = reason,
                IdempotencyKey = Guid.NewGuid().ToString()
            }, cancellationToken: cancellationToken);
        if (!response.Created)
            throw new BillingException(response.ErrorCode);
    }

    public async Task<bool> HasUserHadPaidTopUpAsync(string userId, CancellationToken cancellationToken = default)
    {
        var response = await client.HasUserHadPaidTopUpAsync(
            new OpenRussiaRouter.Contracts.Grpc.Billing.V1.HasUserHadPaidTopUpRequest { UserId = userId },
            cancellationToken: cancellationToken);
        return response.HasPaidTopUp;
    }

    public async Task<(bool StartBalanceEnabled, decimal StartBalanceAmountUsd, bool FirstTopUpEnabled, decimal FirstTopUpPercent)> GetPromotionSettingsAsync(CancellationToken cancellationToken = default)
    {
        var response = await client.GetPromotionSettingsAsync(
            new OpenRussiaRouter.Contracts.Grpc.Billing.V1.GetPromotionSettingsRequest(),
            cancellationToken: cancellationToken);
        return (
            response.StartBalanceEnabled,
            decimal.TryParse(response.StartBalanceAmountUsd, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var sbAmt) ? sbAmt : 1.00m,
            response.FirstTopUpEnabled,
            decimal.TryParse(response.FirstTopUpPercent, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var ftPct) ? ftPct : 20.00m
        );
    }

    public async Task GrantStartBalanceAsync(string userId, decimal amountUsd, CancellationToken cancellationToken = default)
    {
        await client.GrantStartBalanceAsync(
            new OpenRussiaRouter.Contracts.Grpc.Billing.V1.GrantStartBalanceRequest
            {
                UserId = userId,
                AmountUsd = amountUsd.ToString("F2", CultureInfo.InvariantCulture)
            },
            cancellationToken: cancellationToken);
    }

    public async Task<OpenRussiaRouter.Contracts.Billing.GrantFirstTopUpBonusResult> GrantFirstTopUpBonusAsync(string paymentId, CancellationToken cancellationToken = default)
    {
        var response = await client.GrantFirstTopUpBonusAsync(
            new OpenRussiaRouter.Contracts.Grpc.Billing.V1.GrantFirstTopUpBonusRequest { PaymentId = paymentId },
            cancellationToken: cancellationToken);
        return new OpenRussiaRouter.Contracts.Billing.GrantFirstTopUpBonusResult(
            response.Granted,
            string.IsNullOrEmpty(response.LedgerEntryId) ? null : response.LedgerEntryId,
            string.IsNullOrEmpty(response.ErrorCode) ? null : response.ErrorCode);
    }

    public async Task<ApiKeyLimitSettingsInfo> GetApiKeyLimitSettingsAsync(
        string userId,
        string apiKeyId,
        CancellationToken cancellationToken = default)
    {
        var response = await client.GetApiKeyLimitSettingsAsync(
            new OpenRussiaRouter.Contracts.Grpc.Billing.V1.GetApiKeyLimitSettingsRequest
            {
                UserId = userId,
                ApiKeyId = apiKeyId
            },
            cancellationToken: cancellationToken);

        if (!string.IsNullOrWhiteSpace(response.ErrorCode))
            throw new BillingException(response.ErrorCode);

        return new ApiKeyLimitSettingsInfo(
            apiKeyId,
            ParseOptionalDecimal(response.DailyLimitUsd),
            ParseOptionalDecimal(response.WeeklyLimitUsd));
    }

    public async Task UpsertApiKeyLimitSettingsAsync(
        string userId,
        string apiKeyId,
        decimal? dailyLimitUsd,
        decimal? weeklyLimitUsd,
        CancellationToken cancellationToken = default)
    {
        var response = await client.UpsertApiKeyLimitSettingsAsync(
            new OpenRussiaRouter.Contracts.Grpc.Billing.V1.UpsertApiKeyLimitSettingsRequest
            {
                UserId = userId,
                ApiKeyId = apiKeyId,
                DailyLimitUsd = dailyLimitUsd?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                WeeklyLimitUsd = weeklyLimitUsd?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
            },
            cancellationToken: cancellationToken);

        if (!response.Saved)
            throw new BillingException(response.ErrorCode);
    }

    public async Task<IReadOnlyDictionary<string, ApiKeyLimitSummaryInfo>> ListApiKeyLimitSummariesAsync(
        string userId,
        IReadOnlyCollection<string> apiKeyIds,
        CancellationToken cancellationToken = default)
    {
        if (apiKeyIds.Count == 0)
            return new Dictionary<string, ApiKeyLimitSummaryInfo>(StringComparer.Ordinal);

        var request = new OpenRussiaRouter.Contracts.Grpc.Billing.V1.ListApiKeyLimitSummariesRequest
        {
            UserId = userId
        };
        request.ApiKeyIds.AddRange(apiKeyIds);

        var response = await client.ListApiKeyLimitSummariesAsync(request, cancellationToken: cancellationToken);
        if (!string.IsNullOrWhiteSpace(response.ErrorCode))
            throw new BillingException(response.ErrorCode);

        return response.Summaries
            .Select(summary => new ApiKeyLimitSummaryInfo(
                summary.ApiKeyId,
                ParseOptionalDecimal(summary.DailyLimitUsd),
                ParseOptionalDecimal(summary.WeeklyLimitUsd),
                ParseDecimal(summary.DailySpentUsd),
                ParseDecimal(summary.WeeklySpentUsd),
                ParseDecimal(summary.DailyRemainingUsd),
                ParseDecimal(summary.WeeklyRemainingUsd),
                summary.DailyExceeded,
                summary.WeeklyExceeded))
            .ToDictionary(summary => summary.ApiKeyId, StringComparer.Ordinal);
    }

    public async Task<(decimal Rate, string FxSource, DateTimeOffset FxAt, string? QuoteId)> GetFxQuoteAsync(
        string providerCurrency,
        CancellationToken cancellationToken = default)
    {
        var response = await client.GetFxQuoteAsync(
            new OpenRussiaRouter.Contracts.Grpc.Billing.V1.GetFxQuoteRequest
            {
                ProviderCurrency = providerCurrency
            },
            cancellationToken: cancellationToken);

        if (!string.IsNullOrWhiteSpace(response.ErrorCode))
            throw new BillingException(response.ErrorCode);

        return (
            Rate: ParseDecimal(response.Rate),
            FxSource: response.FxSource,
            FxAt: DateTimeOffset.Parse(response.FxAt, CultureInfo.InvariantCulture),
            QuoteId: string.IsNullOrEmpty(response.QuoteId) ? null : response.QuoteId);
    }

    public async Task<CreatedPaymentInfo> CreatePaymentAsync(
        string userId,
        string provider,
        decimal providerAmount,
        string providerCurrency,
        decimal displayAmount,
        string displayCurrency,
        string clientToken,
        decimal? bonusPercent = null,
        CancellationToken cancellationToken = default)
    {
        var request = new OpenRussiaRouter.Contracts.Grpc.Billing.V1.CreatePaymentRequest
        {
            UserId = userId,
            Provider = provider,
            ProviderAmount = providerAmount.ToString(CultureInfo.InvariantCulture),
            ProviderCurrency = providerCurrency,
            DisplayAmount = displayAmount.ToString(CultureInfo.InvariantCulture),
            DisplayCurrency = displayCurrency,
            ClientToken = clientToken
        };
        if (bonusPercent.HasValue)
            request.BonusPercent = bonusPercent.Value.ToString("F2", CultureInfo.InvariantCulture);

        var response = await client.CreatePaymentAsync(request, cancellationToken: cancellationToken);

        if (!string.IsNullOrWhiteSpace(response.ErrorCode))
            throw new BillingException(response.ErrorCode);

        return new CreatedPaymentInfo(
            PaymentId: response.PaymentId,
            OrderId: response.OrderId,
            CreditedUsdAmount: ParseDecimal(response.CreditedUsdAmount),
            FxRate: ParseDecimal(response.FxRate));
    }

    public async Task AttachProviderRefAsync(
        string paymentId,
        string providerPaymentId,
        string payUrl,
        DateTimeOffset? expiresAtUtc,
        CancellationToken cancellationToken = default)
    {
        var response = await client.AttachProviderRefAsync(
            new OpenRussiaRouter.Contracts.Grpc.Billing.V1.AttachProviderRefRequest
            {
                PaymentId = paymentId,
                ProviderPaymentId = providerPaymentId,
                PayUrl = payUrl,
                ExpiresAtUtc = expiresAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty
            },
            cancellationToken: cancellationToken);

        if (!response.Ok)
            throw new BillingException(response.ErrorCode);
    }

    public async Task<ConfirmPaymentResult> ConfirmPaymentByProviderAsync(
        string provider,
        string? providerPaymentId,
        string? orderId,
        decimal confirmedAmount,
        string confirmedCurrency,
        string? providerEventId,
        string rawPayload,
        bool signatureOk,
        CancellationToken cancellationToken = default)
    {
        var response = await client.ConfirmPaymentByProviderAsync(
            new OpenRussiaRouter.Contracts.Grpc.Billing.V1.ConfirmPaymentByProviderRequest
            {
                Provider = provider,
                ProviderPaymentId = providerPaymentId ?? string.Empty,
                OrderId = orderId ?? string.Empty,
                ConfirmedAmount = confirmedAmount.ToString(CultureInfo.InvariantCulture),
                ConfirmedCurrency = confirmedCurrency,
                ProviderEventId = providerEventId ?? string.Empty,
                RawPayload = rawPayload,
                SignatureOk = signatureOk
            },
            cancellationToken: cancellationToken);

        return new ConfirmPaymentResult(
            Confirmed: response.Confirmed,
            LedgerEntryId: string.IsNullOrEmpty(response.LedgerEntryId) ? null : response.LedgerEntryId,
            BalanceUsd: string.IsNullOrEmpty(response.BalanceUsd) ? (decimal?)null : ParseDecimal(response.BalanceUsd),
            ErrorCode: string.IsNullOrEmpty(response.ErrorCode) ? null : response.ErrorCode);
    }

    public async Task<ApplyPaymentEventResult> ApplyPaymentEventAsync(
        string provider,
        string? providerPaymentId,
        string? orderId,
        string eventType,
        string? reason,
        decimal? confirmedAmount,
        string? confirmedCurrency,
        string? providerEventId,
        string rawPayload,
        bool signatureOk,
        CancellationToken cancellationToken = default)
    {
        var response = await client.ApplyPaymentEventAsync(
            new OpenRussiaRouter.Contracts.Grpc.Billing.V1.ApplyPaymentEventRequest
            {
                Provider = provider,
                ProviderPaymentId = providerPaymentId ?? string.Empty,
                OrderId = orderId ?? string.Empty,
                EventType = eventType,
                Reason = reason ?? string.Empty,
                ConfirmedAmount = confirmedAmount.HasValue ? confirmedAmount.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
                ConfirmedCurrency = confirmedCurrency ?? string.Empty,
                ProviderEventId = providerEventId ?? string.Empty,
                RawPayload = rawPayload,
                SignatureOk = signatureOk
            },
            cancellationToken: cancellationToken);

        return new ApplyPaymentEventResult(
            Applied: response.Applied,
            LedgerEntryId: string.IsNullOrEmpty(response.LedgerEntryId) ? null : response.LedgerEntryId,
            ErrorCode: string.IsNullOrEmpty(response.ErrorCode) ? null : response.ErrorCode);
    }

    public async Task<PaymentInfo?> GetPaymentAsync(string paymentId, CancellationToken cancellationToken = default)
    {
        try
        {
            var record = await client.GetPaymentAsync(
                new OpenRussiaRouter.Contracts.Grpc.Billing.V1.GetPaymentRequest { PaymentId = paymentId },
                cancellationToken: cancellationToken);
            return PaymentRecordMapper.ToInfo(record);
        }
        catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<PaymentInfo>> AcquireStalePaymentsAsync(
        string workerId,
        int leaseSeconds,
        DateTimeOffset olderThanUtc,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var response = await client.AcquireStalePaymentsAsync(
            new OpenRussiaRouter.Contracts.Grpc.Billing.V1.AcquireStalePaymentsRequest
            {
                WorkerId = workerId,
                LeaseSeconds = leaseSeconds,
                OlderThanUtc = olderThanUtc.ToString("O", CultureInfo.InvariantCulture),
                Limit = limit
            },
            cancellationToken: cancellationToken);

        return response.Records.Select(PaymentRecordMapper.ToInfo).ToList();
    }

    public async Task<string?> GetPlatformSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        var response = await client.GetPlatformSettingAsync(
            new OpenRussiaRouter.Contracts.Grpc.Billing.V1.GetPlatformSettingRequest { Key = key },
            cancellationToken: cancellationToken);
        return response.Found ? response.Value : null;
    }

    public async Task SetPlatformSettingAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        var response = await client.SetPlatformSettingAsync(
            new OpenRussiaRouter.Contracts.Grpc.Billing.V1.SetPlatformSettingRequest { Key = key, Value = value ?? string.Empty },
            cancellationToken: cancellationToken);
        if (!response.Ok)
            throw new BillingException(response.ErrorCode);
    }

    public async Task<(int TopupsCount, decimal TotalRub, decimal TotalUsd)> GetDailyStatsAsync(
        DateTimeOffset sinceUtc, CancellationToken cancellationToken = default)
    {
        var response = await client.GetDailyStatsAsync(
            new OpenRussiaRouter.Contracts.Grpc.Billing.V1.GetDailyStatsRequest
            {
                SinceUtc = sinceUtc.ToString("O", CultureInfo.InvariantCulture)
            },
            cancellationToken: cancellationToken);

        return (
            response.TopupsCount,
            ParseDecimal(response.TotalRub),
            ParseDecimal(response.TotalUsd));
    }

    private static decimal ParseDecimal(string value) =>
        string.IsNullOrWhiteSpace(value) ? 0m : decimal.Parse(value, CultureInfo.InvariantCulture);

    private static decimal? ParseOptionalDecimal(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : decimal.Parse(value, CultureInfo.InvariantCulture);
}
