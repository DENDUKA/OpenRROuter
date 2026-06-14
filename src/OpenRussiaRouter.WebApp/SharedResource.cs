using System.Globalization;
using System.Resources;

namespace OpenRussiaRouter.WebApp;

/// <summary>
/// Marker class for shared server-side strings and DataAnnotations messages.
///
/// Public static properties are required by the legacy
/// <c>System.ComponentModel.DataAnnotations</c> lookup path (it searches for a public
/// static property/field with the same name as <c>ErrorMessageResourceName</c>). They
/// delegate to the same resources used by <c>IStringLocalizer&lt;SharedResource&gt;</c>.
/// </summary>
public sealed class SharedResource
{
    private static readonly ResourceManager _manager =
        new("OpenRussiaRouter.WebApp.Resources.SharedResource", typeof(SharedResource).Assembly);

    public static string Validation_Required => Lookup("Validation_Required");
    public static string Validation_Email => Lookup("Validation_Email");
    public static string Validation_PasswordMin12 => Lookup("Validation_PasswordMin12");
    public static string Validation_PasswordMin8 => Lookup("Validation_PasswordMin8");
    public static string Validation_PasswordsMismatch => Lookup("Validation_PasswordsMismatch");
    public static string Validation_AcceptLegal => Lookup("Validation_AcceptLegal");
    public static string Validation_Code6Digits => Lookup("Validation_Code6Digits");

    private static string Lookup(string name) =>
        _manager.GetString(name, CultureInfo.CurrentUICulture) ?? name;
}
