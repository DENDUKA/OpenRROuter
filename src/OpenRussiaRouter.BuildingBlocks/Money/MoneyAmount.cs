namespace OpenRussiaRouter.BuildingBlocks.Money;

public readonly record struct MoneyAmount(decimal Amount, Currency Currency)
{

    public static MoneyAmount Usd(decimal amount) => new(amount, Currency.Usd);

    public static MoneyAmount operator +(MoneyAmount left, MoneyAmount right)
    {
        EnsureSameCurrency(left, right);
        return new MoneyAmount(left.Amount + right.Amount, left.Currency);
    }

    public static MoneyAmount operator -(MoneyAmount left, MoneyAmount right)
    {
        EnsureSameCurrency(left, right);
        return new MoneyAmount(left.Amount - right.Amount, left.Currency);
    }

    private static void EnsureSameCurrency(MoneyAmount left, MoneyAmount right)
    {
        if (left.Currency != right.Currency)
        {
            throw new InvalidOperationException("Money amounts must use the same currency.");
        }
    }
}

