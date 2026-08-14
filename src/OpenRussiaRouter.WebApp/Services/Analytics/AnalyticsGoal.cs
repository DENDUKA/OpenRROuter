namespace OpenRussiaRouter.WebApp.Services.Analytics;

/// <summary>
/// One conversion goal to fire on the next render, optionally carrying what it was worth.
/// </summary>
/// <remarks>
/// The value is deliberately vendor-neutral: page models state "this top-up was worth 1500 RUB"
/// and the layout runner translates that into each counter's own shape (GA4 wants
/// <c>value</c>/<c>currency</c>, Metrika wants <c>order_price</c>/<c>currency</c>). Encoding the
/// vendor here would mean every caller repeating itself once per counter.
/// </remarks>
/// <param name="Name">A constant from <see cref="AnalyticsGoals"/>, never an inline string.</param>
/// <param name="Value">Goal value in <paramref name="Currency"/>, or null for a plain goal.</param>
/// <param name="Currency">ISO code of <paramref name="Value"/>. Meaningless without it.</param>
public sealed record AnalyticsGoal(string Name, decimal? Value = null, string? Currency = null)
{
    /// <summary>Key under which pending goals accumulate in <c>ViewData</c> for the layout.</summary>
    public const string ViewDataKey = "AnalyticsGoals";

    public static AnalyticsGoal Named(string name) => new(name);

    public static AnalyticsGoal Valued(string name, decimal value, string currency) =>
        new(name, value, currency);
}
