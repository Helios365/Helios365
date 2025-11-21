using Helios365.Core.Models;
using Microsoft.Extensions.Logging;

namespace Helios365.Core.Services;

public interface IResourceDiscoveryStrategy
{
    string ResourceType { get; }
    string DisplayName { get; }
    Task<IReadOnlyList<Resource>> DiscoverAsync(ServicePrincipal servicePrincipal, IReadOnlyList<string> subscriptionIds, CancellationToken cancellationToken = default);
}

public sealed class AppServiceDiscoveryStrategy : IResourceDiscoveryStrategy
{
    private readonly IResourceGraphService _resourceGraphService;
    private readonly ILogger<AppServiceDiscoveryStrategy> _logger;

    public AppServiceDiscoveryStrategy(IResourceGraphService resourceGraphService, ILogger<AppServiceDiscoveryStrategy> logger)
    {
        _resourceGraphService = resourceGraphService;
        _logger = logger;
    }

    public string ResourceType => "Microsoft.Web/sites";
    public string DisplayName => "App Services";

    public Task<IReadOnlyList<Resource>> DiscoverAsync(ServicePrincipal servicePrincipal, IReadOnlyList<string> subscriptionIds, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Discovering App Services for service principal {ServicePrincipalId}", servicePrincipal.Id);
        return _resourceGraphService.GetAppServicesAsync(servicePrincipal, subscriptionIds, cancellationToken);
    }
}

public sealed class VirtualMachineDiscoveryStrategy : IResourceDiscoveryStrategy
{
    private readonly IResourceGraphService _resourceGraphService;
    private readonly ILogger<VirtualMachineDiscoveryStrategy> _logger;

    public VirtualMachineDiscoveryStrategy(IResourceGraphService resourceGraphService, ILogger<VirtualMachineDiscoveryStrategy> logger)
    {
        _resourceGraphService = resourceGraphService;
        _logger = logger;
    }

    public string ResourceType => "Microsoft.Compute/virtualMachines";
    public string DisplayName => "Virtual Machines";

    public Task<IReadOnlyList<Resource>> DiscoverAsync(ServicePrincipal servicePrincipal, IReadOnlyList<string> subscriptionIds, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Discovering Virtual Machines for service principal {ServicePrincipalId}", servicePrincipal.Id);
        return _resourceGraphService.GetVirtualMachinesAsync(servicePrincipal, subscriptionIds, cancellationToken);
    }
}

public sealed class MySqlFlexibleServerDiscoveryStrategy : IResourceDiscoveryStrategy
{
    private readonly IResourceGraphService _resourceGraphService;
    private readonly ILogger<MySqlFlexibleServerDiscoveryStrategy> _logger;

    public MySqlFlexibleServerDiscoveryStrategy(IResourceGraphService resourceGraphService, ILogger<MySqlFlexibleServerDiscoveryStrategy> logger)
    {
        _resourceGraphService = resourceGraphService;
        _logger = logger;
    }

    public string ResourceType => "Microsoft.DBforMySQL/flexibleServers";
    public string DisplayName => "MySQL Flexible Servers";

    public Task<IReadOnlyList<Resource>> DiscoverAsync(ServicePrincipal servicePrincipal, IReadOnlyList<string> subscriptionIds, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Discovering MySQL Flexible Servers for service principal {ServicePrincipalId}", servicePrincipal.Id);
        return _resourceGraphService.GetMySqlFlexibleServersAsync(servicePrincipal, subscriptionIds, cancellationToken);
    }
}

public sealed class ServiceBusNamespaceDiscoveryStrategy : IResourceDiscoveryStrategy
{
    private readonly IResourceGraphService _resourceGraphService;
    private readonly ILogger<ServiceBusNamespaceDiscoveryStrategy> _logger;

    public ServiceBusNamespaceDiscoveryStrategy(IResourceGraphService resourceGraphService, ILogger<ServiceBusNamespaceDiscoveryStrategy> logger)
    {
        _resourceGraphService = resourceGraphService;
        _logger = logger;
    }

    public string ResourceType => "Microsoft.ServiceBus/namespaces";
    public string DisplayName => "Service Bus namespaces";

    public Task<IReadOnlyList<Resource>> DiscoverAsync(ServicePrincipal servicePrincipal, IReadOnlyList<string> subscriptionIds, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Discovering Service Bus namespaces for service principal {ServicePrincipalId}", servicePrincipal.Id);
        return _resourceGraphService.GetServiceBusNamespacesAsync(servicePrincipal, subscriptionIds, cancellationToken);
    }
}
