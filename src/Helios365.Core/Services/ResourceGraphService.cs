using System.Text.Json;
using Azure;
using Azure.ResourceManager.ResourceGraph.Models;
using Helios365.Core.Models;
using Helios365.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace Helios365.Core.Services;

public interface IResourceGraphService
{
    Task<IReadOnlyList<Resource>> GetAppServicesAsync(
        ServicePrincipal servicePrincipal,
        IEnumerable<string> subscriptionIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Resource>> GetVirtualMachinesAsync(
        ServicePrincipal servicePrincipal,
        IEnumerable<string> subscriptionIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Resource>> GetMySqlFlexibleServersAsync(
        ServicePrincipal servicePrincipal,
        IEnumerable<string> subscriptionIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Resource>> GetServiceBusNamespacesAsync(
        ServicePrincipal servicePrincipal,
        IEnumerable<string> subscriptionIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Resource>> GetFunctionAppsAsync(
        ServicePrincipal servicePrincipal,
        IEnumerable<string> subscriptionIds,
        CancellationToken cancellationToken = default);
}

public class ResourceGraphService : IResourceGraphService
{
    private static readonly ResourceGraphQuery AppServices = new(
        Query: """
            Resources
            | where type =~ 'microsoft.web/sites'
            | project id, name, resourceGroup, location, kind, tags
            """,
        ResourceType: "Microsoft.Web/sites",
        Description: "App Services",
        MetadataEnricher: null);

    private static readonly ResourceGraphQuery FunctionApps = new(
        Query: """
            Resources
            | where type =~ 'microsoft.web/sites'
            | where tolower(kind) contains 'functionapp'
            | project id, name, resourceGroup, location, kind, tags
            """,
        ResourceType: "Microsoft.Web/sites",
        Description: "Azure Functions",
        MetadataEnricher: null);

    private static readonly ResourceGraphQuery VirtualMachines = new(
        Query: """
            Resources
            | where type =~ 'microsoft.compute/virtualmachines'
            | project id, name, resourceGroup, location,
                vmSize = tostring(properties.hardwareProfile.vmSize),
                osType = tostring(properties.storageProfile.osDisk.osType),
                tags
            """,
        ResourceType: "Microsoft.Compute/virtualMachines",
        Description: "Virtual Machines",
        MetadataEnricher: AddVirtualMachineMetadata);

    private static readonly ResourceGraphQuery MySqlFlexibleServers = new(
        Query: """
            Resources
            | where type =~ 'microsoft.dbformysql/flexibleservers'
            | project id, name, resourceGroup, location,
                skuName = tostring(sku.name),
                engineVersion = tostring(properties.version),
                tags
            """,
        ResourceType: "Microsoft.DBforMySQL/flexibleServers",
        Description: "MySQL Flexible Servers",
        MetadataEnricher: AddMySqlMetadata);

    private static readonly ResourceGraphQuery ServiceBusNamespaces = new(
        Query: """
            Resources
            | where type =~ 'microsoft.servicebus/namespaces'
            | project id, name, resourceGroup, location,
                skuName = tostring(sku.name),
                capacity = tostring(properties.capacity),
                tags
            """,
        ResourceType: "Microsoft.ServiceBus/namespaces",
        Description: "Service Bus namespaces",
        MetadataEnricher: AddServiceBusMetadata);

    private readonly IResourceGraphClient _resourceGraphClient;
    private readonly ILogger<ResourceGraphService> _logger;

    public ResourceGraphService(IResourceGraphClient resourceGraphClient, ILogger<ResourceGraphService> logger)
    {
        _resourceGraphClient = resourceGraphClient;
        _logger = logger;
    }

    public Task<IReadOnlyList<Resource>> GetAppServicesAsync(
        ServicePrincipal servicePrincipal,
        IEnumerable<string> subscriptionIds,
        CancellationToken cancellationToken = default) =>
        QueryResourcesAsync(servicePrincipal, subscriptionIds, AppServices, cancellationToken);

    public Task<IReadOnlyList<Resource>> GetFunctionAppsAsync(
        ServicePrincipal servicePrincipal,
        IEnumerable<string> subscriptionIds,
        CancellationToken cancellationToken = default) =>
        QueryResourcesAsync(servicePrincipal, subscriptionIds, FunctionApps, cancellationToken);

    public Task<IReadOnlyList<Resource>> GetVirtualMachinesAsync(
        ServicePrincipal servicePrincipal,
        IEnumerable<string> subscriptionIds,
        CancellationToken cancellationToken = default) =>
        QueryResourcesAsync(servicePrincipal, subscriptionIds, VirtualMachines, cancellationToken);

    public Task<IReadOnlyList<Resource>> GetMySqlFlexibleServersAsync(
        ServicePrincipal servicePrincipal,
        IEnumerable<string> subscriptionIds,
        CancellationToken cancellationToken = default) =>
        QueryResourcesAsync(servicePrincipal, subscriptionIds, MySqlFlexibleServers, cancellationToken);

    public Task<IReadOnlyList<Resource>> GetServiceBusNamespacesAsync(
        ServicePrincipal servicePrincipal,
        IEnumerable<string> subscriptionIds,
        CancellationToken cancellationToken = default) =>
        QueryResourcesAsync(servicePrincipal, subscriptionIds, ServiceBusNamespaces, cancellationToken);

    private async Task<IReadOnlyList<Resource>> QueryResourcesAsync(
        ServicePrincipal servicePrincipal,
        IEnumerable<string> subscriptionIds,
        ResourceGraphQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(servicePrincipal);
        ArgumentNullException.ThrowIfNull(subscriptionIds);

        var subscriptionList = subscriptionIds
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToArray();

        if (subscriptionList.Length == 0)
        {
            _logger.LogWarning("No subscriptionIds provided for ServicePrincipal {ServicePrincipalId}", servicePrincipal.Id);
            return Array.Empty<Resource>();
        }

        try
        {
            _logger.LogInformation(
                "Querying Azure Resource Graph for {ResourceDescription} using ServicePrincipal {ServicePrincipalId} across {SubscriptionCount} subscriptions",
                query.Description,
                servicePrincipal.Id,
                subscriptionList.Length);

            var response = await _resourceGraphClient
                .QueryAsync(servicePrincipal, subscriptionList, query.Query, cancellationToken)
                .ConfigureAwait(false);

            return MapToResources(response, servicePrincipal, query.ResourceType, query.MetadataEnricher);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(
                ex,
                "Azure Resource Graph request failed for ServicePrincipal {ServicePrincipalId} when querying {ResourceDescription}",
                servicePrincipal.Id,
                query.Description);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error while querying Azure Resource Graph for ServicePrincipal {ServicePrincipalId} when querying {ResourceDescription}",
                servicePrincipal.Id,
                query.Description);
            throw;
        }
    }

    private IReadOnlyList<Resource> MapToResources(
        ResourceQueryResult response,
        ServicePrincipal servicePrincipal,
        string resourceType,
        Action<JsonElement, Dictionary<string, string>>? metadataEnricher)
    {
        if (response.Data is null)
        {
            return Array.Empty<Resource>();
        }

        using var document = JsonDocument.Parse(response.Data.ToString());

        if (document.RootElement.ValueKind == JsonValueKind.Object &&
            document.RootElement.TryGetProperty("data", out var dataElement) &&
            dataElement.ValueKind == JsonValueKind.Array)
        {
            return MapResourceArray(dataElement, servicePrincipal, resourceType, metadataEnricher);
        }

        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            return MapResourceArray(document.RootElement, servicePrincipal, resourceType, metadataEnricher);
        }

        _logger.LogWarning("Unexpected Azure Resource Graph response format - expected JSON array");
        return Array.Empty<Resource>();
    }

    private IReadOnlyList<Resource> MapResourceArray(
        JsonElement arrayElement,
        ServicePrincipal servicePrincipal,
        string resourceType,
        Action<JsonElement, Dictionary<string, string>>? metadataEnricher)
    {
        var results = new List<Resource>();

        foreach (var element in arrayElement.EnumerateArray())
        {
            try
            {
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

                metadataEnricher?.Invoke(element, metadata);

                var resource = new Resource
                {
                    CustomerId = servicePrincipal.CustomerId,
                    ServicePrincipalId = servicePrincipal.Id,
                    Name = name,
                    ResourceId = normalizedId,
                    ResourceType = resourceType,
                    Metadata = metadata
                };

                results.Add(resource);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to map Azure Resource Graph result item to Resource model");
            }
        }

        return results;
    }

    private static void AddVirtualMachineMetadata(JsonElement element, Dictionary<string, string> metadata)
    {
        AddMetadataIfPresent(element, "vmSize", metadata, "vmSize");
        AddMetadataIfPresent(element, "osType", metadata, "osType");
    }

    private static void AddMySqlMetadata(JsonElement element, Dictionary<string, string> metadata)
    {
        AddMetadataIfPresent(element, "skuName", metadata, "skuName");
        AddMetadataIfPresent(element, "engineVersion", metadata, "engineVersion");
    }

    private static void AddServiceBusMetadata(JsonElement element, Dictionary<string, string> metadata)
    {
        AddMetadataIfPresent(element, "skuName", metadata, "skuName");
        AddMetadataIfPresent(element, "capacity", metadata, "capacity");
    }

    private static void AddMetadataIfPresent(
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

    private sealed record ResourceGraphQuery(
        string Query,
        string ResourceType,
        string Description,
        Action<JsonElement, Dictionary<string, string>>? MetadataEnricher);
}
