using System.Text.Json;

namespace Helios365.Core.Utilities;

public static class ResourceMetadataHelpers
{
    public static void AddMetadataIfPresent(
        JsonElement element,
        string propertyName,
        Dictionary<string, string> metadata,
        string metadataKey)
    {
        if (element.TryGetProperty(propertyName, out var value) &&
            value.ValueKind != JsonValueKind.Null &&
            value.ValueKind != JsonValueKind.Undefined)
        {
            metadata[metadataKey] = value.GetString() ?? string.Empty;
        }
    }
}
