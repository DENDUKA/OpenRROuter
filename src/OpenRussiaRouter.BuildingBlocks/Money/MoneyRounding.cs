namespace OpenRussiaRouter.BuildingBlocks.Money;

public static class MoneyRounding
{
    public const int DecimalPlaces = 6;

    public const MidpointRounding Mode = MidpointRounding.ToEven;

    public static decimal Round(decimal value) => decimal.Round(value, DecimalPlaces, Mode);

    public static MoneyAmount Round(MoneyAmount amount) =>
        new(decimal.Round(amount.Amount, DecimalPlaces, Mode), amount.Currency);
}