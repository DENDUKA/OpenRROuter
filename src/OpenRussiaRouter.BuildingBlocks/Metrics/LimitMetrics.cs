using System.Diagnostics.Metrics;

namespace OpenRussiaRouter.BuildingBlocks.Metrics;

/// <summary>
/// Счётчик отказов по лимитам платформы — единая точка для всех видов лимитов (rate limit,
/// суточный/недельный spend-лимит ключа, нулевой баланс). Экспортируется в Prometheus как
/// <c>orr_limit_rejections_total</c>.
/// </summary>
/// <remarks>
/// Метки намеренно узкие (<see cref="LimitTypes"/> × <see cref="Subjects"/> — максимум 5 серий):
/// в лейблы нельзя класть api key id / user id / ip — это неограниченная кардинальность на
/// публичном эндпоинте. Разрезы «кто именно упёрся» берутся SQL-панелями поверх
/// <c>billing.usage_records</c>, а не метриками.
/// </remarks>
public static class LimitMetrics
{
    /// <summary>Значения метки <c>limit_type</c>.</summary>
    public static class LimitTypes
    {
        public const string RateLimit = "rate_limit";
        public const string SpendDaily = "spend_daily";
        public const string SpendWeekly = "spend_weekly";
        public const string Balance = "balance";
    }

    /// <summary>Значения метки <c>subject</c> — по какой сущности лимит был посчитан.</summary>
    public static class Subjects
    {
        public const string ApiKey = "api_key";
        public const string Ip = "ip";
        public const string User = "user";
    }

    private static readonly Meter Meter = new(OpenTelemetryExtensions.DefaultMeterName);

    private static readonly Counter<long> Rejections = Meter.CreateCounter<long>(
        "orr_limit_rejections_total",
        description: "Requests rejected because a platform limit was reached");

    /// <summary>Зафиксировать отказ по лимиту. <paramref name="limitType"/> — из <see cref="LimitTypes"/>, <paramref name="subject"/> — из <see cref="Subjects"/>.</summary>
    public static void RecordRejection(string limitType, string subject) =>
        Rejections.Add(
            1,
            new KeyValuePair<string, object?>("limit_type", limitType),
            new KeyValuePair<string, object?>("subject", subject));
}
