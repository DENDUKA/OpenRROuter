using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.BuildingBlocks.Usage;

public sealed class UsageCostCalculator : IUsageCostCalculator
{
    private const decimal OneMillion = 1_000_000m;

    public MoneyAmount Calculate(TokenUsage usage, ModelTokenPrice price)
    {
        usage.EnsureValid();
        price.EnsureValid();

        // Invariant: InputTokens includes CacheTokens (cached tokens are a subset of
        // the prompt — see TokenUsageExtractor). Cached tokens are billed at the cache
        // rate instead of the input rate, so subtract them from the input-priced part
        // to avoid double-charging. Clamp guards against inconsistent upstream counts.
        var billableInputTokens = Math.Max(0, usage.InputTokens - usage.CacheTokens);

        var amount =
            billableInputTokens * price.InputPerMillion.Amount / OneMillion +
            usage.OutputTokens * price.OutputPerMillion.Amount / OneMillion +
            usage.CacheTokens * price.CachePerMillion.Amount / OneMillion;

        // Match numeric(18,6) precision used for cost/ledger columns so that the
        // returned cost equals what is persisted and summed into the balance.
        amount = Math.Round(amount, 6, MidpointRounding.ToEven);

        return new MoneyAmount(amount, price.InputPerMillion.Currency);
    }
}

