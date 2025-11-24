using Azure.ResourceManager.Resources;
using Helios365.Core.Models;
using Helios365.Core.Utilities;

namespace Helios365.Core.Services;

/// <summary>
/// Default Azure ARM resource -> Helios Resource mapper that can be reused across hosts.
/// </summary>
public class ResourceMapper : IResourceMapper<GenericResourceData>
{
    public Resource Map(GenericResourceData azureResource, string customerId, string servicePrincipalId)
    {
        ArgumentNullException.ThrowIfNull(azureResource);
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(servicePrincipalId);

        var resourceId = azureResource.Id?.ToString() ?? string.Empty;
        var resourceGroup = azureResource.Id?.ResourceGroupName ?? string.Empty;
        var location = azureResource.Location.ToString() ?? string.Empty;

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
            ResourceId = ResourceIdNormalizer.Normalize(resourceId),
            ResourceType = azureResource.ResourceType.ToString(),
            Metadata = metadata
        };
    }
}
