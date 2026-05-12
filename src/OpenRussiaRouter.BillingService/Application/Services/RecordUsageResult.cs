using OpenRussiaRouter.BuildingBlocks.Money;

namespace OpenRussiaRouter.BillingService.Application.Services;

public sealed record RecordUsageResult(bool Accepted, string? RequestId, MoneyAmount Cost, string? ErrorCode);