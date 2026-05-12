using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.BuildingBlocks.Usage;

public interface IUsageCostCalculator
{

    MoneyAmount Calculate(TokenUsage usage, ModelTokenPrice price);
}
