using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using Helios365.Core.Models;
using Helios365.Core.Services;
using Microsoft.Extensions.Logging;

namespace Helios365.Core.Services;

public interface IResourceService
{
    Task<TenantResource> GetTenantAsync(ServicePrincipal servicePrincipal, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetAccessibleSubscriptionsAsync(ServicePrincipal servicePrincipal, CancellationToken cancellationToken = default);
}

public class ResourceService : IResourceService
{
    private readonly IArmClientFactory _armClientFactory;
    private readonly ILogger<ResourceService> _logger;
    private readonly IResourceMapper<GenericResourceData> _resourceMapper;

    public ResourceService(IArmClientFactory armClientFactory, ILogger<ResourceService> logger, IResourceMapper<GenericResourceData> resourceMapper)
    {
        _armClientFactory = armClientFactory;
        _logger = logger;
        _resourceMapper = resourceMapper;
    }
    
    public async Task<Resource> GetResourceAsync(ServicePrincipal servicePrincipal, Resource resource, CancellationToken cancellationToken = default)
    {
        var armClient = await _armClientFactory.CreateAsync(servicePrincipal, cancellationToken).ConfigureAwait(false);
        var azureResource = await armClient.GetGenericResource(new ResourceIdentifier(resource.ResourceId)).GetAsync(cancellationToken).ConfigureAwait(false);

        return _resourceMapper.Map(azureResource.Value.Data, servicePrincipal.CustomerId, servicePrincipal.Id); 

    }
    public async Task<TenantResource> GetTenantAsync(ServicePrincipal servicePrincipal, CancellationToken cancellationToken = default)
    {
        var armClient = await _armClientFactory.CreateAsync(servicePrincipal, cancellationToken).ConfigureAwait(false);

        await foreach (var tenant in armClient.GetTenants().GetAllAsync(cancellationToken).ConfigureAwait(false))
        {
            return tenant;
        }

        throw new InvalidOperationException($"Unable to resolve tenant for service principal {servicePrincipal.Id}.");
    }

    public async Task<IReadOnlyList<string>> GetAccessibleSubscriptionsAsync(ServicePrincipal servicePrincipal, CancellationToken cancellationToken = default)
    {
        var armClient = await _armClientFactory.CreateAsync(servicePrincipal, cancellationToken).ConfigureAwait(false);

        var results = new List<string>();
        await foreach (var subscription in armClient.GetSubscriptions().GetAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!string.IsNullOrWhiteSpace(subscription.Data.SubscriptionId))
            {
                results.Add(subscription.Data.SubscriptionId.Trim());
            }
        }

        _logger.LogInformation("Service principal {ServicePrincipalId} has access to {SubscriptionCount} subscriptions", servicePrincipal.Id, results.Count);
        return results;
    }
}
