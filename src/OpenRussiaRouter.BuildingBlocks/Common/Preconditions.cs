namespace OpenRussiaRouter.BuildingBlocks.Common;

public static class Preconditions
{
    public static string RequireValue(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be empty.", name);
        }

        return value.Trim();
    }

    public static T RequireNotNull<T>(T? value, string name) where T : class
    {
        ArgumentNullException.ThrowIfNull(value, name);
        return value;
    }
}