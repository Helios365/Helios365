using System.Net;
using Helios365.Core.Models;
using Helios365.Core.Repositories;
using Helios365.Core.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Helios365.Functions.Triggers;

public class ResourceDiscoveryTrigger
{
    private readonly IServicePrincipalRepository _servicePrincipalRepository;
    private readonly IResourceRepository _resourceRepository;
    private readonly IAzureResourceGraphService _resourceGraphService;
    private readonly IAzureResourceService _azureResourceService;
    private readonly ILogger<ResourceDiscoveryTrigger> _logger;

    public ResourceDiscoveryTrigger(
        IServicePrincipalRepository servicePrincipalRepository,
        IResourceRepository resourceRepository,
        IAzureResourceGraphService resourceGraphService,
        IAzureResourceService azureResourceService,
        ILogger<ResourceDiscoveryTrigger> logger)
    {
        _servicePrincipalRepository = servicePrincipalRepository;
        _resourceRepository = resourceRepository;
        _resourceGraphService = resourceGraphService;
        _azureResourceService = azureResourceService;
        _logger = logger;
    }

    [Function(nameof(SyncAppServices))]
    public async Task<HttpResponseData> SyncAppServices(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "resources/sync/app-services")] HttpRequestData req)
    {
        var cancellationToken = req.FunctionContext.CancellationToken;
        var summary = new DiscoverySummary();
        var processedResourceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var servicePrincipals = await _servicePrincipalRepository.ListAsync(limit: 1000, cancellationToken: cancellationToken);

        foreach (var servicePrincipal in servicePrincipals)
        {
            if (!servicePrincipal.Active)
            {
                summary.SkippedPrincipals++;
                continue;
            }

            IReadOnlyList<string> subscriptionIds;
            try
            {
                subscriptionIds = await _azureResourceService
                    .GetAccessibleSubscriptionsAsync(servicePrincipal, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                summary.Errors.Add($"ServicePrincipal {servicePrincipal.Id}: failed to list subscriptions - {ex.Message}");
                _logger.LogError(ex, "Failed to enumerate subscriptions for service principal {ServicePrincipalId}", servicePrincipal.Id);
                summary.SkippedPrincipals++;
                continue;
            }

            if (subscriptionIds.Count == 0)
            {
                summary.SkippedPrincipals++;
                _logger.LogWarning("Service principal {ServicePrincipalId} has no accessible subscriptions; skipping discovery", servicePrincipal.Id);
                continue;
            }

            try
            {
                var appServices = await _resourceGraphService.GetAppServicesAsync(
                    servicePrincipal,
                    subscriptionIds,
                    cancellationToken);

                summary.ProcessedPrincipals++;

                foreach (var discovered in appServices)
                {
                    if (!processedResourceIds.Add(discovered.ResourceId))
                    {
                        continue;
                    }

                    await UpsertResourceAsync(discovered, summary, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                summary.Errors.Add($"ServicePrincipal {servicePrincipal.Id}: {ex.Message}");
                _logger.LogError(ex, "Failed to sync resources for service principal {ServicePrincipalId}", servicePrincipal.Id);
            }
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(summary, cancellationToken);
        return response;
    }

    private async Task UpsertResourceAsync(Resource discovered, DiscoverySummary summary, CancellationToken cancellationToken)
    {
        var existing = await _resourceRepository.GetByResourceIdAsync(discovered.CustomerId, discovered.ResourceId, cancellationToken);

        if (existing is null)
        {
            discovered.Id = Guid.NewGuid().ToString("N");
            discovered.Active = true;
            discovered.UseDefaultActions = true;
            discovered.CreatedAt = DateTime.UtcNow;
            discovered.UpdatedAt = DateTime.UtcNow;

            await _resourceRepository.CreateAsync(discovered, cancellationToken);
            summary.CreatedResources++;
            return;
        }

        if (NeedsUpdate(existing, discovered))
        {
            existing.Name = discovered.Name;
            existing.ResourceType = discovered.ResourceType;
            existing.ServicePrincipalId = discovered.ServicePrincipalId;
            existing.Metadata = discovered.Metadata ?? new Dictionary<string, string>();
            existing.UpdatedAt = DateTime.UtcNow;

            await _resourceRepository.UpdateAsync(existing.Id, existing, cancellationToken);
            summary.UpdatedResources++;
        }
        else
        {
            summary.UnchangedResources++;
        }
    }

    private static bool NeedsUpdate(Resource existing, Resource incoming)
    {
        if (!string.Equals(existing.Name, incoming.Name, StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.Equals(existing.ServicePrincipalId, incoming.ServicePrincipalId, StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.Equals(existing.ResourceType, incoming.ResourceType, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!DictionaryEquals(existing.Metadata, incoming.Metadata))
        {
            return true;
        }

        return false;
    }

    private static bool DictionaryEquals(
        Dictionary<string, string>? current,
        Dictionary<string, string>? incoming)
    {
        current ??= new Dictionary<string, string>();
        incoming ??= new Dictionary<string, string>();

        if (ReferenceEquals(current, incoming))
        {
            return true;
        }

        if (current.Count != incoming.Count)
        {
            return false;
        }

        foreach (var kvp in current)
        {
            if (!incoming.TryGetValue(kvp.Key, out var incomingValue))
            {
                return false;
            }

            if (!string.Equals(kvp.Value, incomingValue, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private sealed class DiscoverySummary
    {
        public int ProcessedPrincipals { get; set; }
        public int SkippedPrincipals { get; set; }
        public int CreatedResources { get; set; }
        public int UpdatedResources { get; set; }
        public int UnchangedResources { get; set; }
        public List<string> Errors { get; } = new();
    }
}
