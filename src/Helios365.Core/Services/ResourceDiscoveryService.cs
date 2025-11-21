using Helios365.Core.Models;
using Helios365.Core.Repositories;
using Helios365.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace Helios365.Core.Services;

public interface IResourceDiscoveryService
{
    Task<ResourceDiscoverySummary> SyncAsync(IEnumerable<string>? resourceTypes = default, CancellationToken cancellationToken = default);
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

public class ResourceDiscoveryService : IResourceDiscoveryService
{
    private readonly IServicePrincipalRepository _servicePrincipalRepository;
    private readonly IResourceRepository _resourceRepository;
    private readonly IResourceService _azureResourceService;
    private readonly IEnumerable<IResourceDiscoveryStrategy> _discoveryStrategies;
    private readonly ILogger<ResourceDiscoveryService> _logger;

    public ResourceDiscoveryService(
        IServicePrincipalRepository servicePrincipalRepository,
        IResourceRepository resourceRepository,
        IResourceService azureResourceService,
        IEnumerable<IResourceDiscoveryStrategy> discoveryStrategies,
        ILogger<ResourceDiscoveryService> logger)
    {
        _servicePrincipalRepository = servicePrincipalRepository;
        _resourceRepository = resourceRepository;
        _azureResourceService = azureResourceService;
        _discoveryStrategies = discoveryStrategies;
        _logger = logger;
    }

    public async Task<ResourceDiscoverySummary> SyncAsync(IEnumerable<string>? resourceTypes = default, CancellationToken cancellationToken = default)
    {
        var summary = new ResourceDiscoverySummary();
        var processedResourceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var strategyFilter = resourceTypes?
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var strategiesToRun = _discoveryStrategies
            .Where(s => strategyFilter is null || strategyFilter.Count == 0 || strategyFilter.Contains(s.ResourceType))
            .ToList();

        if (strategiesToRun.Count == 0)
        {
            _logger.LogWarning("No discovery strategies matched the requested resource types.");
            return summary;
        }

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

            var principalProcessed = false;

            foreach (var strategy in strategiesToRun)
            {
                try
                {
                    var discovered = await strategy.DiscoverAsync(servicePrincipal, subscriptionIds, cancellationToken).ConfigureAwait(false);

                    if (discovered.Count > 0)
                    {
                        principalProcessed = true;
                    }

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

                        if (string.IsNullOrWhiteSpace(resource.ServicePrincipalId))
                        {
                            resource.ServicePrincipalId = servicePrincipal.Id;
                        }

                        await UpsertResourceAsync(resource, summary, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to sync {ResourceType} for service principal {ServicePrincipalId}", strategy.DisplayName, servicePrincipal.Id);
                    summary.Errors.Add($"ServicePrincipal {servicePrincipal.Id} ({strategy.DisplayName}): {ex.Message}");
                }
            }

            if (principalProcessed)
            {
                summary.ProcessedPrincipals++;
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
