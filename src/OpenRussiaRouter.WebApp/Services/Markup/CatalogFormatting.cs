namespace OpenRussiaRouter.WebApp.Services.Markup;

public static class CatalogFormatting
{
    public static string FormatContext(int? tokens) =>
        tokens is > 0
            ? tokens >= 1_000_000 ? $"{tokens / 1_000_000M:0.#}M"
              : tokens >= 1_000 ? $"{tokens / 1_000}K"
              : tokens.ToString()!
            : "—";

    public static string GetInitials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return "AI";
        return string.Concat(parts.Take(2).Select(p => char.ToUpperInvariant(p[0])));
    }
}
