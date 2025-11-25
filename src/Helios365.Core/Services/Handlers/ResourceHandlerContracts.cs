using Helios365.Core.Models;

namespace Helios365.Core.Services.Handlers;

public interface IResourceHandler
{
    string ResourceType { get; }
    string DisplayName { get; }
}

public interface IResourceDiscovery : IResourceHandler
{
    Task<IReadOnlyList<Resource>> DiscoverAsync(ServicePrincipal servicePrincipal, IReadOnlyList<string> subscriptionIds, CancellationToken cancellationToken = default);
}

public interface IResourceLifecycle : IResourceHandler
{
    Task<bool> RestartAsync(ServicePrincipal servicePrincipal, Resource resource, RestartAction action, CancellationToken cancellationToken = default);
}

public interface IResourceDiagnostics : IResourceHandler
{
    Task<DiagnosticsResult> GetDiagnosticsAsync(ServicePrincipal servicePrincipal, Resource resource, CancellationToken cancellationToken = default);

    Task<MetricsResult> GetMetricsAsync(ServicePrincipal servicePrincipal, Resource resource, CancellationToken cancellationToken = default);
}
