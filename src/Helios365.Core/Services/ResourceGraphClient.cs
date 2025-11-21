using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.ResourceGraph;
using Azure.ResourceManager.ResourceGraph.Models;
using Helios365.Core.Models;
using Microsoft.Extensions.Logging;

namespace Helios365.Core.Services;

public interface IResourceGraphClient
{
    Task<ResourceQueryResult> QueryAsync(
        ServicePrincipal servicePrincipal,
        IEnumerable<string> subscriptionIds,
        string query,
        CancellationToken cancellationToken = default);
}

public class ResourceGraphClient : IResourceGraphClient
{
    private readonly IArmClientFactory _armClientFactory;
    private readonly ILogger<ResourceGraphClient> _logger;

    public ResourceGraphClient(IArmClientFactory armClientFactory, ILogger<ResourceGraphClient> logger)
    {
        _armClientFactory = armClientFactory;
        _logger = logger;
    }

    public async Task<ResourceQueryResult> QueryAsync(
        ServicePrincipal servicePrincipal,
        IEnumerable<string> subscriptionIds,
        string query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(servicePrincipal);

        ArgumentNullException.ThrowIfNull(subscriptionIds);

        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Query is required.", nameof(query));
        }

        var armClient = await _armClientFactory.CreateAsync(servicePrincipal, cancellationToken).ConfigureAwait(false);
        var tenant = await ResolveTenantAsync(armClient, cancellationToken).ConfigureAwait(false);

        var request = new ResourceQueryContent(query)
        {
            Options = new ResourceQueryRequestOptions
            {
                ResultFormat = ResultFormat.ObjectArray
            }
        };

        foreach (var subscriptionId in subscriptionIds)
        {
            request.Subscriptions.Add(subscriptionId);
        }

        _logger.LogInformation(
            "Executing Azure Resource Graph query for ServicePrincipal {ServicePrincipalId} across {SubscriptionCount} subscriptions",
            servicePrincipal.Id,
            request.Subscriptions.Count);

        return await tenant.GetResourcesAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<TenantResource> ResolveTenantAsync(ArmClient armClient, CancellationToken cancellationToken)
    {
        await foreach (var tenant in armClient.GetTenants().GetAllAsync(cancellationToken).ConfigureAwait(false))
        {
            return tenant;
        }

        throw new InvalidOperationException("Unable to resolve TenantResource for the current credentials.");
    }
}
