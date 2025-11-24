using System.Text.Json;
using Helios365.Core.Models;
using Helios365.Core.Utilities;

namespace Helios365.Core.Services;

/// <summary>
/// Wraps a Resource Graph result item with the metadata needed to map it to the Helios domain.
/// </summary>
public sealed record ResourceGraphItem(
    JsonElement Element,
    string ResourceType,
    Action<JsonElement, Dictionary<string, string>>? MetadataEnricher);

/// <summary>
/// Maps Azure Resource Graph records into Helios <see cref="Resource"/> instances.
/// </summary>
public class ResourceGraphMapper : IResourceMapper<ResourceGraphItem>
{
    public Resource Map(ResourceGraphItem graphItem, string customerId, string servicePrincipalId)
    {
        ArgumentNullException.ThrowIfNull(graphItem);
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(servicePrincipalId);

        var element = graphItem.Element;
        var id = element.GetProperty("id").GetString() ?? string.Empty;
        var normalizedId = ResourceIdNormalizer.Normalize(id);
        var name = element.GetProperty("name").GetString() ?? string.Empty;
        var resourceGroup = element.TryGetProperty("resourceGroup", out var rgEl) ? rgEl.GetString() ?? string.Empty : string.Empty;
        var location = element.TryGetProperty("location", out var locEl) ? locEl.GetString() ?? string.Empty : string.Empty;
        var kind = element.TryGetProperty("kind", out var kindEl) ? kindEl.GetString() ?? string.Empty : string.Empty;

        var metadata = new Dictionary<string, string>
        {
            ["resourceGroup"] = resourceGroup,
            ["location"] = location,
            ["kind"] = kind
        };

        if (element.TryGetProperty("tags", out var tagsElement) && tagsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var tagProperty in tagsElement.EnumerateObject())
            {
                metadata[$"tag:{tagProperty.Name}"] = tagProperty.Value.GetString() ?? string.Empty;
            }
        }

        graphItem.MetadataEnricher?.Invoke(element, metadata);

        return new Resource
        {
            CustomerId = customerId,
            ServicePrincipalId = servicePrincipalId,
            Name = name,
            ResourceId = normalizedId,
            ResourceType = graphItem.ResourceType,
            Metadata = metadata
        };
    }
}
