using Helios365.Core.Models;

namespace Helios365.Core.Services;

public interface IAzureResourceGraphService
{
    /// <summary>
    /// Queries Azure Resource Graph (using the given Service Principal)
    /// for App Service resources and returns them as domain <see cref="Resource"/> models.
    /// This method does not persist the resources - callers are responsible for storing them.
    /// </summary>
    Task<IReadOnlyList<Resource>> GetAppServicesAsync(
        ServicePrincipal servicePrincipal,
        IEnumerable<string> subscriptionIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries Azure Resource Graph for Virtual Machine resources.
    /// </summary>
    Task<IReadOnlyList<Resource>> GetVirtualMachinesAsync(
        ServicePrincipal servicePrincipal,
        IEnumerable<string> subscriptionIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries Azure Resource Graph for Azure Database for MySQL - Flexible Server instances.
    /// </summary>
    Task<IReadOnlyList<Resource>> GetMySqlFlexibleServersAsync(
        ServicePrincipal servicePrincipal,
        IEnumerable<string> subscriptionIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries Azure Resource Graph for Azure Service Bus namespaces.
    /// </summary>
    Task<IReadOnlyList<Resource>> GetServiceBusNamespacesAsync(
        ServicePrincipal servicePrincipal,
        IEnumerable<string> subscriptionIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries Azure Resource Graph for Function Apps (Azure Functions hosted on App Service).
    /// </summary>
    Task<IReadOnlyList<Resource>> GetFunctionAppsAsync(
        ServicePrincipal servicePrincipal,
        IEnumerable<string> subscriptionIds,
        CancellationToken cancellationToken = default);
}
