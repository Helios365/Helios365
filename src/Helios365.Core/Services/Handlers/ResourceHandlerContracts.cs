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
