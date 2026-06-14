using System.Globalization;

namespace OpenRussiaRouter.WebApp.Services.Localization;

/// <summary>
/// Single source of truth for cultures supported by the WebApp UI.
/// Used by <c>Program.cs</c> to configure <c>RequestLocalizationOptions</c>,
/// by <c>PreferencesController</c> to validate the <c>culture</c> form value,
/// and by tests to assert the supported set without duplicating string lists.
/// </summary>
public static class SupportedCultures
{
    public const string DefaultCulture = "ru";

    public static readonly CultureInfo[] All =
    [
        new("ru"),
        new("en"),
    ];

    public static readonly HashSet<string> Codes = new(All.Select(c => c.TwoLetterISOLanguageName), StringComparer.OrdinalIgnoreCase);

    public static bool IsSupported(string? code) =>
        !string.IsNullOrWhiteSpace(code) && Codes.Contains(code);
}
