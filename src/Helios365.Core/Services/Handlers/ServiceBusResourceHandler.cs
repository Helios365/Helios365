using System.Text.Json;
using Azure;
using Azure.ResourceManager.ResourceGraph.Models;
using Helios365.Core.Models;
using Helios365.Core.Services.Clients;
using Helios365.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace Helios365.Core.Services.Handlers;

public class ServiceBusResourceHandler : IResourceDiscovery
{
    private const string ResourceTypeValue = "Microsoft.ServiceBus/namespaces";
    private const string Query = """
        Resources
        | where type =~ 'microsoft.servicebus/namespaces'
        | project id, name, resourceGroup, location,
            skuName = tostring(sku.name),
            capacity = tostring(properties.capacity),
            tags
        """;

    private readonly IResourceGraphClient _resourceGraphClient;
    private readonly ILogger<ServiceBusResourceHandler> _logger;

    public ServiceBusResourceHandler(
        IResourceGraphClient resourceGraphClient,
        ILogger<ServiceBusResourceHandler> logger)
    {
        _resourceGraphClient = resourceGraphClient;
        _logger = logger;
    }

    public string ResourceType => ResourceTypeValue;
    public string DisplayName => "Service Bus namespaces";

    public Task<IReadOnlyList<Resource>> DiscoverAsync(ServicePrincipal servicePrincipal, IReadOnlyList<string> subscriptionIds, CancellationToken cancellationToken = default) =>
        QueryAsync(servicePrincipal, subscriptionIds, Query, ResourceTypeValue, "Service Bus namespaces", AddServiceBusMetadata, cancellationToken);

    private async Task<IReadOnlyList<Resource>> QueryAsync(
        ServicePrincipal servicePrincipal,
        IReadOnlyList<string> subscriptionIds,
        string query,
        string resourceType,
        string description,
        Action<JsonElement, Dictionary<string, string>>? metadataEnricher,
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
                description,
                servicePrincipal.Id,
                subscriptionList.Length);

            var response = await _resourceGraphClient
                .QueryAsync(servicePrincipal, subscriptionList, query, cancellationToken)
                .ConfigureAwait(false);

            return MapToResources(response, servicePrincipal, resourceType, metadataEnricher);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(
                ex,
                "Azure Resource Graph request failed for ServicePrincipal {ServicePrincipalId} when querying {ResourceDescription}",
                servicePrincipal.Id,
                description);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error while querying Azure Resource Graph for ServicePrincipal {ServicePrincipalId} when querying {ResourceDescription}",
                servicePrincipal.Id,
                description);
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

        var arrayElement = document.RootElement;

        if (document.RootElement.ValueKind == JsonValueKind.Object &&
            document.RootElement.TryGetProperty("data", out var dataElement) &&
            dataElement.ValueKind == JsonValueKind.Array)
        {
            arrayElement = dataElement;
        }

        if (arrayElement.ValueKind != JsonValueKind.Array)
        {
            _logger.LogWarning("Unexpected Azure Resource Graph response format - expected JSON array");
            return Array.Empty<Resource>();
        }

        var results = new List<Resource>();

        foreach (var element in arrayElement.EnumerateArray())
        {
            try
            {
                var resource = ResourceMappingHelpers.FromResourceGraphItem(
                    element,
                    resourceType,
                    servicePrincipal.CustomerId,
                    servicePrincipal.Id,
                    metadataEnricher);

                results.Add(resource);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to map Azure Resource Graph result item to Resource model");
            }
        }

        return results;
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
}
