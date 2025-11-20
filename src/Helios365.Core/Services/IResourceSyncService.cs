using Helios365.Core.Models;

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
