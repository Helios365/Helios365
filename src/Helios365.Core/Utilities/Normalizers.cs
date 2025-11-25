using Azure.Core;

namespace Helios365.Core.Utilities;

public static class Normalizers
{
    public static string NormalizeResourceId(string? resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return string.Empty;
        }

        try
        {
            var identifier = ResourceIdentifier.Parse(resourceId);
            return identifier.ToString().ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return resourceId.Trim().ToLowerInvariant();
        }
        catch (FormatException)
        {
            return resourceId.Trim().ToLowerInvariant();
        }
    }
}
