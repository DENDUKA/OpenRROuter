namespace OpenRussiaRouter.BuildingBlocks.Common;

public static partial class EmailValidator
{
    public static string Normalize(string email)
    {
        var value = Preconditions.RequireValue(email, nameof(email)).Trim().ToLowerInvariant();
        if (!value.Contains('@', StringComparison.Ordinal))
        {
            throw new ArgumentException("Email must contain '@'.", nameof(email));
        }

        return value;
    }
}