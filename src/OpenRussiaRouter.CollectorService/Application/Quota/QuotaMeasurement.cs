namespace OpenRussiaRouter.CollectorService.Application.Quota;

/// <param name="Total">Потолок окна (<c>total</c>/<c>limit</c> из ответа ноды), если он есть.</param>
/// <param name="ResetAt">
/// Когда окно обнулится (<c>resetAt</c> ноды). Длину окна («5-часовой», «недельный») ни один
/// провайдер отдельным полем не отдаёт — она читается только из горизонта до сброса, поэтому
/// наружу отдаём сам момент, а не выведенный из него тип.
/// </param>
public sealed record QuotaMeasurement(
    string NodeId,
    string Provider,
    string ConnectionId,
    string AccountName,
    string QuotaName,
    double Remaining,
    double? Percent,
    IReadOnlyList<string> ModelIds,
    double? Total = null,
    DateTimeOffset? ResetAt = null);
