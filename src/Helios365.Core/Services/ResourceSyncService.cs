using Helios365.Core.Models;
using Helios365.Core.Repositories;
using Helios365.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace Helios365.Core.Services;

public interface IResourceSyncService
{
    Task<ResourceDiscoverySummary> SyncAppServicesAsync(CancellationToken cancellationToken = default);
}

public sealed class ResourceDiscoverySummary
{
    public int ProcessedPrincipals { get; set; }
    public int SkippedPrincipals { get; set; }
    public int CreatedResources { get; set; }
    public int UpdatedResources { get; set; }
    public int UnchangedResources { get; set; }
    public List<string> Errors { get; } = new();
}

public class ResourceSyncService : IResourceSyncService
{
    private readonly IServicePrincipalRepository _servicePrincipalRepository;
    private readonly IResourceRepository _resourceRepository;
    private readonly IResourceGraphService _resourceGraphService;
    private readonly IResourceService _azureResourceService;
    private readonly ILogger<ResourceSyncService> _logger;

    public ResourceSyncService(
        IServicePrincipalRepository servicePrincipalRepository,
        IResourceRepository resourceRepository,
        IResourceGraphService resourceGraphService,
        IResourceService azureResourceService,
        ILogger<ResourceSyncService> logger)
    {
        _servicePrincipalRepository = servicePrincipalRepository;
        _resourceRepository = resourceRepository;
        _resourceGraphService = resourceGraphService;
        _azureResourceService = azureResourceService;
        _logger = logger;
    }

    public async Task<ResourceDiscoverySummary> SyncAppServicesAsync(CancellationToken cancellationToken = default)
    {
        var summary = new ResourceDiscoverySummary();
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
                subscriptionIds = await _azureResourceService.GetAccessibleSubscriptionsAsync(servicePrincipal, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enumerate subscriptions for service principal {ServicePrincipalId}", servicePrincipal.Id);
                summary.Errors.Add($"ServicePrincipal {servicePrincipal.Id}: failed to list subscriptions - {ex.Message}");
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
                var discovered = await _resourceGraphService.GetAppServicesAsync(
                    servicePrincipal,
                    subscriptionIds,
                    cancellationToken).ConfigureAwait(false);

                summary.ProcessedPrincipals++;

                foreach (var resource in discovered)
                {
                    // Ensure normalized IDs are unique per sync run.
                    var normalizedId = ResourceIdNormalizer.Normalize(resource.ResourceId);
                    if (!processedResourceIds.Add(normalizedId))
                    {
                        continue;
                    }

                    resource.ResourceId = normalizedId;
                    resource.CustomerId = servicePrincipal.CustomerId;

                    await UpsertResourceAsync(resource, summary, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync resources for service principal {ServicePrincipalId}", servicePrincipal.Id);
                summary.Errors.Add($"ServicePrincipal {servicePrincipal.Id}: {ex.Message}");
            }
        }

        return summary;
    }

    private async Task UpsertResourceAsync(Resource discovered, ResourceDiscoverySummary summary, CancellationToken cancellationToken)
    {
        var existing = await _resourceRepository.GetByResourceIdAsync(discovered.CustomerId, discovered.ResourceId, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            discovered.Id = Guid.NewGuid().ToString("N");
            discovered.Active = true;
            discovered.UseDefaultActions = true;
            discovered.CreatedAt = DateTime.UtcNow;
            discovered.UpdatedAt = DateTime.UtcNow;

            await _resourceRepository.CreateAsync(discovered, cancellationToken).ConfigureAwait(false);
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

            await _resourceRepository.UpdateAsync(existing.Id, existing, cancellationToken).ConfigureAwait(false);
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

    private static bool DictionaryEquals(Dictionary<string, string>? current, Dictionary<string, string>? incoming)
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
}
