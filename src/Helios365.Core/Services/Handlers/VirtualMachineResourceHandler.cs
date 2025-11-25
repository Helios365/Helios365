using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.ResourceGraph.Models;
using Azure.ResourceManager.Resources;
using Helios365.Core.Models;
using Helios365.Core.Services.Clients;
using Helios365.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace Helios365.Core.Services.Handlers;

public class VirtualMachineResourceHandler : IResourceDiscovery, IResourceLifecycle
{
    private const string ResourceTypeValue = "Microsoft.Compute/virtualMachines";
    private const string Query = """
        Resources
        | where type =~ 'microsoft.compute/virtualmachines'
        | project id, name, resourceGroup, location,
            vmSize = tostring(properties.hardwareProfile.vmSize),
            osType = tostring(properties.storageProfile.osDisk.osType),
            tags
        """;

    private readonly IResourceGraphClient _resourceGraphClient;
    private readonly IArmClientFactory _armClientFactory;
    private readonly ILogger<VirtualMachineResourceHandler> _logger;

    public VirtualMachineResourceHandler(
        IResourceGraphClient resourceGraphClient,
        IArmClientFactory armClientFactory,
        ILogger<VirtualMachineResourceHandler> logger)
    {
        _resourceGraphClient = resourceGraphClient;
        _armClientFactory = armClientFactory;
        _logger = logger;
    }

    public string ResourceType => ResourceTypeValue;
    public string DisplayName => "Virtual Machines";

    public Task<IReadOnlyList<Resource>> DiscoverAsync(ServicePrincipal servicePrincipal, IReadOnlyList<string> subscriptionIds, CancellationToken cancellationToken = default) =>
        QueryAsync(servicePrincipal, subscriptionIds, Query, ResourceTypeValue, "Virtual Machines", AddVirtualMachineMetadata, cancellationToken);

    public async Task<bool> RestartAsync(ServicePrincipal servicePrincipal, Resource resource, RestartAction action, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Restart requested for Virtual Machine {ResourceId}", resource.ResourceId);

        var armClient = await _armClientFactory.CreateAsync(servicePrincipal, cancellationToken).ConfigureAwait(false);
        var vm = armClient.GetVirtualMachineResource(new ResourceIdentifier(resource.ResourceId));

        if (action.WaitBeforeSeconds > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(action.WaitBeforeSeconds), cancellationToken).ConfigureAwait(false);
        }

        await vm.RestartAsync(WaitUntil.Completed, cancellationToken).ConfigureAwait(false);

        if (action.WaitAfterSeconds > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(action.WaitAfterSeconds), cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

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

    private static void AddVirtualMachineMetadata(JsonElement element, Dictionary<string, string> metadata)
    {
        AddMetadataIfPresent(element, "vmSize", metadata, "vmSize");
        AddMetadataIfPresent(element, "osType", metadata, "osType");
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
