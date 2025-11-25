using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.ResourceGraph.Models;
using Azure.ResourceManager.Resources;
using Helios365.Core.Models;
using Helios365.Core.Services.Clients;
using Helios365.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace Helios365.Core.Services.Handlers;

public class AppServiceResourceHandler : IResourceDiscovery, IResourceLifecycle, IResourceDiagnostics
{
    private const string ResourceTypeValue = "Microsoft.Web/sites";
    private const string Query = """
        Resources
        | where type =~ 'microsoft.web/sites'
        | project id, name, resourceGroup, location, kind, tags
        """;

    private readonly IResourceGraphClient _resourceGraphClient;
    private readonly IMetricsClient _metricsClient;
    private readonly IArmClientFactory _armClientFactory;
    private readonly ILogger<AppServiceResourceHandler> _logger;

    public AppServiceResourceHandler(
        IResourceGraphClient resourceGraphClient,
        IMetricsClient metricsClient,
        IArmClientFactory armClientFactory,
        ILogger<AppServiceResourceHandler> logger)
    {
        _resourceGraphClient = resourceGraphClient;
        _metricsClient = metricsClient;
        _armClientFactory = armClientFactory;
        _logger = logger;
    }

    public string ResourceType => ResourceTypeValue;
    public string DisplayName => "App Services";

    public Task<IReadOnlyList<Resource>> DiscoverAsync(ServicePrincipal servicePrincipal, IReadOnlyList<string> subscriptionIds, CancellationToken cancellationToken = default) =>
        QueryAsync(servicePrincipal, subscriptionIds, Query, ResourceTypeValue, "App Services", null, cancellationToken);

    public Task<DiagnosticsResult> GetDiagnosticsAsync(ServicePrincipal servicePrincipal, Resource resource, CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, string>
        {
            ["resourceId"] = resource.ResourceId,
            ["resourceType"] = resource.ResourceType,
            ["handler"] = DisplayName,
            ["location"] = resource.Metadata.TryGetValue("location", out var loc) ? loc : string.Empty,
            ["resourceGroup"] = resource.Metadata.TryGetValue("resourceGroup", out var rg) ? rg : string.Empty,
            ["kind"] = resource.Metadata.TryGetValue("kind", out var kind) ? kind : string.Empty
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
        var metrics = new[] { "CpuPercentage", "MemoryWorkingSet" };
        return _metricsClient.QueryAsync(servicePrincipal, resource.ResourceId, resource.ResourceType, metrics, "Microsoft.Web/sites", TimeSpan.FromHours(1), cancellationToken);
    }

    public async Task<bool> RestartAsync(ServicePrincipal servicePrincipal, Resource resource, RestartAction action, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Restart requested for App Service {ResourceId}", resource.ResourceId);

        var armClient = await _armClientFactory.CreateAsync(servicePrincipal, cancellationToken).ConfigureAwait(false);
        var site = armClient.GetWebSiteResource(new ResourceIdentifier(resource.ResourceId));

        if (action.WaitBeforeSeconds > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(action.WaitBeforeSeconds), cancellationToken).ConfigureAwait(false);
        }

        await site.RestartAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

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
}
