namespace OpenRussiaRouter.BuildingBlocks.Money;

/// <summary>
/// Immutable decimal money amount with an explicit currency.
/// </summary>
/// <param name="Amount">Decimal amount in the specified currency.</param>
/// <param name="Currency">Currency for the amount.</param>
public readonly record struct MoneyAmount(decimal Amount, Currency Currency)
{
    /// <summary>
    /// Creates a Russian ruble amount.
    /// </summary>
    /// <param name="amount">Decimal amount in RUB.</param>
    /// <returns>Money amount in RUB.</returns>
    public static MoneyAmount Rub(decimal amount) => new(amount, Currency.Rub);

    /// <summary>
    /// Adds two amounts with the same currency.
    /// </summary>
    /// <param name="left">Left amount.</param>
    /// <param name="right">Right amount.</param>
    /// <returns>Sum of both amounts.</returns>
    /// <exception cref="InvalidOperationException">Thrown when currencies differ.</exception>
    public static MoneyAmount operator +(MoneyAmount left, MoneyAmount right)
    {
        EnsureSameCurrency(left, right);
        return new MoneyAmount(left.Amount + right.Amount, left.Currency);
    }

    /// <summary>
    /// Subtracts two amounts with the same currency.
    /// </summary>
    /// <param name="left">Left amount.</param>
    /// <param name="right">Right amount.</param>
    /// <returns>Difference between both amounts.</returns>
    /// <exception cref="InvalidOperationException">Thrown when currencies differ.</exception>
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

