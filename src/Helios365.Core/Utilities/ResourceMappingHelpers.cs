using System.Text.Json;
using Azure.ResourceManager.Resources;
using Helios365.Core.Models;

namespace Helios365.Core.Utilities;

/// <summary>
/// Centralized mapping helpers to convert Azure SDK payloads to the Helios domain Resource model.
/// Keeps mapping logic reusable without DI indirection.
/// </summary>
public static class ResourceMappingHelpers
{
    public static Resource FromArm(GenericResourceData azureResource, string customerId, string servicePrincipalId)
    {
        ArgumentNullException.ThrowIfNull(azureResource);
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(servicePrincipalId);

        var resourceId = azureResource.Id?.ToString() ?? string.Empty;
        var resourceGroup = azureResource.Id?.ResourceGroupName ?? string.Empty;
        var location = azureResource.Location.ToString();

        var metadata = new Dictionary<string, string>
        {
            ["resourceGroup"] = resourceGroup,
            ["location"] = location
        };

        foreach (var tag in azureResource.Tags)
        {
            metadata[$"tag:{tag.Key}"] = tag.Value ?? string.Empty;
        }

        return new Resource
        {
            CustomerId = customerId,
            ServicePrincipalId = servicePrincipalId,
            Name = azureResource.Name ?? string.Empty,
            ResourceId = Normalizers.NormalizeResourceId(resourceId),
            ResourceType = azureResource.ResourceType.ToString(),
            Metadata = metadata
        };
    }

    public static Resource FromResourceGraphItem(
        JsonElement element,
        string resourceType,
        string customerId,
        string servicePrincipalId,
        Action<JsonElement, Dictionary<string, string>>? metadataEnricher = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(servicePrincipalId);

        var id = element.GetProperty("id").GetString() ?? string.Empty;
        var normalizedId = Normalizers.NormalizeResourceId(id);
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

        metadataEnricher?.Invoke(element, metadata);

        return new Resource
        {
            CustomerId = customerId,
            ServicePrincipalId = servicePrincipalId,
            Name = name,
            ResourceId = normalizedId,
            ResourceType = resourceType,
            Metadata = metadata
        };
    }
}
