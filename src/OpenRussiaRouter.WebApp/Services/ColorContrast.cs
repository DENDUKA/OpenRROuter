using System.Globalization;

namespace OpenRussiaRouter.WebApp.Services;

/// <summary>
/// Picks a readable foreground color (black or white) for an arbitrary
/// background hex color, so admin-defined tag colors stay legible.
/// </summary>
public static class ColorContrast
{
    /// <summary>
    /// Returns "#000" or "#fff" depending on the perceived brightness of
    /// <paramref name="backgroundHex"/>. Falls back to white for unparseable input.
    /// </summary>
    public static string TextColor(string? backgroundHex)
    {
        if (!TryParseHex(backgroundHex, out var r, out var g, out var b))
        {
            return "#fff";
        }

        // YIQ perceived brightness (0-255). Threshold 140 keeps mid-grays readable with dark text.
        var brightness = (r * 299 + g * 587 + b * 114) / 1000;
        return brightness >= 140 ? "#000" : "#fff";
    }

    private static bool TryParseHex(string? hex, out int r, out int g, out int b)
    {
        r = g = b = 0;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var value = hex.Trim().TrimStart('#');
        if (value.Length == 3)
        {
            value = string.Concat(value[0], value[0], value[1], value[1], value[2], value[2]);
        }

        if (value.Length != 6
            || !int.TryParse(value.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)
            || !int.TryParse(value.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)
            || !int.TryParse(value.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b))
        {
            r = g = b = 0;
            return false;
        }

        return true;
    }
}
