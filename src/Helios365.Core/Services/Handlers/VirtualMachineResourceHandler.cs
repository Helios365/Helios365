using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Compute.Models;
using Azure.ResourceManager.ResourceGraph.Models;
using Azure.ResourceManager.Resources;
using Helios365.Core.Models;
using Helios365.Core.Services.Clients;
using Helios365.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace Helios365.Core.Services.Handlers;

public class VirtualMachineResourceHandler : IResourceDiscovery, IResourceLifecycle, IResourceDiagnostics
{
    private const string ResourceTypeValue = "Microsoft.Compute/virtualMachines";
    private const string Query = """
        Resources
        | where type =~ 'microsoft.compute/virtualmachines'
        | project id, name, resourceGroup, location,
            vmSize = tostring(properties.hardwareProfile.vmSize),
            osType = tostring(properties.storageProfile.osDisk.osType),
            powerState = tostring(properties.extended.instanceView.powerState.code),
            tags
        """;

    private readonly IResourceGraphClient _resourceGraphClient;
    private readonly IMetricsClient _metricsClient;
    private readonly IArmClientFactory _armClientFactory;
    private readonly ILogger<VirtualMachineResourceHandler> _logger;

    public VirtualMachineResourceHandler(
        IResourceGraphClient resourceGraphClient,
        IMetricsClient metricsClient,
        IArmClientFactory armClientFactory,
        ILogger<VirtualMachineResourceHandler> logger)
    {
        _resourceGraphClient = resourceGraphClient;
        _metricsClient = metricsClient;
        _armClientFactory = armClientFactory;
        _logger = logger;
    }

    public string ResourceType => ResourceTypeValue;
    public string DisplayName => "Virtual Machines";

    public Task<IReadOnlyList<Resource>> DiscoverAsync(ServicePrincipal servicePrincipal, IReadOnlyList<string> subscriptionIds, CancellationToken cancellationToken = default) =>
        QueryAsync(servicePrincipal, subscriptionIds, Query, ResourceTypeValue, "Virtual Machines", AddVirtualMachineMetadata, cancellationToken);

    public Task<DiagnosticsResult> GetDiagnosticsAsync(ServicePrincipal servicePrincipal, Resource resource, CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, string>
        {
            ["resourceId"] = resource.ResourceId,
            ["resourceType"] = resource.ResourceType,
            ["handler"] = DisplayName,
            ["location"] = resource.Metadata.TryGetValue("location", out var loc) ? loc : string.Empty,
            ["resourceGroup"] = resource.Metadata.TryGetValue("resourceGroup", out var rg) ? rg : string.Empty,
            ["vmSize"] = resource.Metadata.TryGetValue("vmSize", out var size) ? size : string.Empty,
            ["osType"] = resource.Metadata.TryGetValue("osType", out var os) ? os : string.Empty
        };

        return Task.FromResult(new DiagnosticsResult
        {
            ResourceId = resource.ResourceId,
            ResourceType = resource.ResourceType,
            Data = data
        });
    }

    public Task<MetricsResult> GetMetricsAsync(ServicePrincipal servicePrincipal, Resource resource, CancellationToken cancellationToken = default)
    {
        var metrics = new[] { "Percentage CPU", "Available Memory Bytes" };
        return _metricsClient.QueryAsync(servicePrincipal, resource.ResourceId, resource.ResourceType, metrics, "Microsoft.Compute/virtualMachines", TimeSpan.FromHours(2), cancellationToken);
    }

    public async Task<string?> GetStatusAsync(ServicePrincipal servicePrincipal, Resource resource, CancellationToken cancellationToken = default)
    {
        try
        {
            var armClient = await _armClientFactory.CreateAsync(servicePrincipal, cancellationToken).ConfigureAwait(false);
            var vm = await armClient.GetVirtualMachineResource(new ResourceIdentifier(resource.ResourceId)).GetAsync(InstanceViewType.InstanceView, cancellationToken).ConfigureAwait(false);
            var powerState = vm.Value.Data.InstanceView?.Statuses?.FirstOrDefault(s => s.Code?.StartsWith("PowerState/", StringComparison.OrdinalIgnoreCase) == true);
            return powerState?.DisplayStatus ?? powerState?.Code;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch status for Virtual Machine {ResourceId}", resource.ResourceId);
            return null;
        }
    }

    public async Task<bool> StartAsync(ServicePrincipal servicePrincipal, Resource resource, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Start requested for Virtual Machine {ResourceId}", resource.ResourceId);

        var armClient = await _armClientFactory.CreateAsync(servicePrincipal, cancellationToken).ConfigureAwait(false);
        var vm = armClient.GetVirtualMachineResource(new ResourceIdentifier(resource.ResourceId));
        await vm.PowerOnAsync(WaitUntil.Completed, cancellationToken: cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> StopAsync(ServicePrincipal servicePrincipal, Resource resource, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stop requested for Virtual Machine {ResourceId}", resource.ResourceId);

        var armClient = await _armClientFactory.CreateAsync(servicePrincipal, cancellationToken).ConfigureAwait(false);
        var vm = armClient.GetVirtualMachineResource(new ResourceIdentifier(resource.ResourceId));
        await vm.PowerOffAsync(WaitUntil.Completed, skipShutdown: null, cancellationToken).ConfigureAwait(false);
        return true;
    }

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
        ResourceMetadataHelpers.AddMetadataIfPresent(element, "vmSize", metadata, "vmSize");
        ResourceMetadataHelpers.AddMetadataIfPresent(element, "osType", metadata, "osType");
        ResourceMetadataHelpers.AddMetadataIfPresent(element, "powerState", metadata, "powerState");
    }
}
