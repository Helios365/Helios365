using System.Text.Json;
using Azure;
using Azure.ResourceManager.ResourceGraph.Models;
using Helios365.Core.Contracts.Diagnostics;
using Helios365.Core.Models;
using Helios365.Core.Services.Clients;
using Helios365.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace Helios365.Core.Services.Handlers;

public class MySqlResourceHandler : IResourceDiscovery, IResourceDiagnostics
{
    private const string ResourceTypeValue = "Microsoft.DBforMySQL/flexibleServers";
    private const string Query = """
        Resources
        | where type =~ 'microsoft.dbformysql/flexibleservers'
        | project id, name, resourceGroup, location,
            skuName = tostring(sku.name),
            engineVersion = tostring(properties.version),
            tags
        """;

    private readonly IResourceGraphClient _resourceGraphClient;
    private readonly IMetricsClient _metricsClient;
    private readonly ILogger<MySqlResourceHandler> _logger;

    public MySqlResourceHandler(
        IResourceGraphClient resourceGraphClient,
        IMetricsClient metricsClient,
        ILogger<MySqlResourceHandler> logger)
    {
        _resourceGraphClient = resourceGraphClient;
        _metricsClient = metricsClient;
        _logger = logger;
    }

    public string ResourceType => ResourceTypeValue;
    public string DisplayName => "MySQL Flexible Servers";

    public Task<IReadOnlyList<Resource>> DiscoverAsync(ServicePrincipal servicePrincipal, IReadOnlyList<string> subscriptionIds, CancellationToken cancellationToken = default) =>
        QueryAsync(servicePrincipal, subscriptionIds, Query, ResourceTypeValue, "MySQL Flexible Servers", AddMySqlMetadata, cancellationToken);

    public Task<DiagnosticsResult> GetDiagnosticsAsync(ServicePrincipal servicePrincipal, Resource resource, CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, string>
        {
            ["resourceId"] = resource.ResourceId,
            ["resourceType"] = resource.ResourceType,
            ["handler"] = DisplayName,
            ["location"] = resource.Metadata.TryGetValue("location", out var loc) ? loc : string.Empty,
            ["resourceGroup"] = resource.Metadata.TryGetValue("resourceGroup", out var rg) ? rg : string.Empty,
            ["skuName"] = resource.Metadata.TryGetValue("skuName", out var sku) ? sku : string.Empty,
            ["engineVersion"] = resource.Metadata.TryGetValue("engineVersion", out var ver) ? ver : string.Empty
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
        var metrics = new[] { "cpu_percent", "memory_percent" };
        return _metricsClient.QueryAsync(servicePrincipal, resource.ResourceId, resource.ResourceType, metrics, "Microsoft.DBforMySQL/flexibleServers", TimeSpan.FromHours(1), cancellationToken);
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

    private static void AddMySqlMetadata(JsonElement element, Dictionary<string, string> metadata)
    {
        AddMetadataIfPresent(element, "skuName", metadata, "skuName");
        AddMetadataIfPresent(element, "engineVersion", metadata, "engineVersion");
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
