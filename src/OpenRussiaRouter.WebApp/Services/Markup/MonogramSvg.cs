using System.Security.Cryptography;
using System.Text;

namespace OpenRussiaRouter.WebApp.Services.Markup;

/// <summary>
/// Deterministic monogram badge used as the fallback when an entity has no uploaded logo.
/// Shared by the provider and tool logo endpoints so both look the same and a colour tweak
/// lands in one place.
/// </summary>
public static class MonogramSvg
{
    // Muted, tasteful palette — deterministically picked per id so the same entity always
    // gets the same colour across pages and reloads.
    private static readonly string[] Palette =
    [
        "#4F6BED", "#7C5CDB", "#0E9F6E", "#D97706", "#DC2626",
        "#0891B2", "#DB2777", "#5B6472", "#2563EB", "#65A30D",
    ];

    public static string Build(string id, string label)
    {
        var letter = FirstLetter(label);
        // Stable hash → colour index (no Math.Random; must be identical every request).
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(id));
        var color = Palette[hash[0] % Palette.Length];
        var text = System.Security.SecurityElement.Escape(letter);

        return
            $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64" role="img" aria-hidden="true">
              <rect width="64" height="64" rx="14" fill="{color}"/>
              <text x="32" y="33" fill="#FFFFFF" font-family="-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif" font-size="34" font-weight="600" text-anchor="middle" dominant-baseline="central">{text}</text>
            </svg>
            """;
    }

    private static string FirstLetter(string label)
    {
        foreach (var ch in label.Trim())
        {
            if (char.IsLetterOrDigit(ch))
            {
                return char.ToUpperInvariant(ch).ToString();
            }
        }

        return "?";
    }
}
