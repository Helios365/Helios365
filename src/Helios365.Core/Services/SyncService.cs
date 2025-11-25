using Helios365.Core.Models;
using Helios365.Core.Repositories;
using Helios365.Core.Services.Handlers;
using Helios365.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace Helios365.Core.Services;

public interface ISyncService
{
    Task<SyncSummary> SyncAsync(IEnumerable<string>? resourceTypes = default, CancellationToken cancellationToken = default);
}

public sealed class SyncSummary
{
    public int ProcessedPrincipals { get; set; }
    public int SkippedPrincipals { get; set; }
    public int CreatedResources { get; set; }
    public int UpdatedResources { get; set; }
    public int UnchangedResources { get; set; }
    public List<string> Errors { get; } = new();
}

public class SyncService : ISyncService
{
    private readonly IServicePrincipalRepository _servicePrincipalRepository;
    private readonly IResourceRepository _resourceRepository;
    private readonly IResourceService _resourceService;
    private readonly IReadOnlyList<IResourceDiscovery> _handlers;
    private readonly ILogger<SyncService> _logger;

    public SyncService(
        IServicePrincipalRepository servicePrincipalRepository,
        IResourceRepository resourceRepository,
        IResourceService resourceService,
        IEnumerable<IResourceHandler> resourceHandlers,
        ILogger<SyncService> logger)
    {
        _servicePrincipalRepository = servicePrincipalRepository;
        _resourceRepository = resourceRepository;
        _resourceService = resourceService;
        _handlers = resourceHandlers.OfType<IResourceDiscovery>().ToList();
        _logger = logger;
    }

    public async Task<SyncSummary> SyncAsync(IEnumerable<string>? resourceTypes = default, CancellationToken cancellationToken = default)
    {
        var summary = new SyncSummary();
        var processedResourceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var handlerFilter = resourceTypes?
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var handlersToRun = _handlers
            .Where(h => handlerFilter is null || handlerFilter.Count == 0 || handlerFilter.Contains(h.ResourceType))
            .ToList();

        if (handlersToRun.Count == 0)
        {
            _logger.LogWarning("No discovery handlers matched the requested resource types.");
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
                subscriptionIds = await _resourceService.GetAccessibleSubscriptionsAsync(servicePrincipal, cancellationToken).ConfigureAwait(false);
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

            foreach (var handler in handlersToRun)
            {
                try
                {
                    var discovered = await handler.DiscoverAsync(servicePrincipal, subscriptionIds, cancellationToken).ConfigureAwait(false);

                    if (discovered.Count > 0)
                    {
                        principalProcessed = true;
                    }

                    foreach (var resource in discovered)
                    {
                        var normalizedId = Normalizers.NormalizeResourceId(resource.ResourceId);
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
                    _logger.LogError(ex, "Failed to sync {ResourceType} for service principal {ServicePrincipalId}", handler.DisplayName, servicePrincipal.Id);
                    summary.Errors.Add($"ServicePrincipal {servicePrincipal.Id} ({handler.DisplayName}): {ex.Message}");
                }
            }

            if (principalProcessed)
            {
                summary.ProcessedPrincipals++;
            }
        }

        return summary;
    }

    private async Task UpsertResourceAsync(Resource discovered, SyncSummary summary, CancellationToken cancellationToken)
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
