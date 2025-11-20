using Azure.ResourceManager.Resources;
using Helios365.Core.Models;

namespace Helios365.Core.Services;

public interface IResourceService
{
    Task<TenantResource> GetTenantAsync(ServicePrincipal servicePrincipal, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetAccessibleSubscriptionsAsync(ServicePrincipal servicePrincipal, CancellationToken cancellationToken = default);
}
