using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using Helios365.Core.Models;
using Helios365.Core.Repositories;
using Helios365.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace Helios365.Core.Services;

public class ResourceService : IResourceService
{
    private readonly ISecretRepository _secretRepository;
    private readonly ILogger<ResourceService> _logger;

    public ResourceService(ISecretRepository secretRepository, ILogger<ResourceService> logger)
    {
        _secretRepository = secretRepository;
        _logger = logger;
    }

    public async Task<TenantResource> GetTenantAsync(ServicePrincipal servicePrincipal, CancellationToken cancellationToken = default)
    {
        var credential = await CreateCredentialAsync(servicePrincipal, cancellationToken).ConfigureAwait(false);
        var armClient = CreateArmClient(credential, servicePrincipal.CloudEnvironment);

        await foreach (var tenant in armClient.GetTenants().GetAllAsync(cancellationToken).ConfigureAwait(false))
        {
            return tenant;
        }

        throw new InvalidOperationException($"Unable to resolve tenant for service principal {servicePrincipal.Id}.");
    }

    public async Task<IReadOnlyList<string>> GetAccessibleSubscriptionsAsync(ServicePrincipal servicePrincipal, CancellationToken cancellationToken = default)
    {
        var credential = await CreateCredentialAsync(servicePrincipal, cancellationToken).ConfigureAwait(false);
        var armClient = CreateArmClient(credential, servicePrincipal.CloudEnvironment);

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

    private async Task<TokenCredential> CreateCredentialAsync(ServicePrincipal servicePrincipal, CancellationToken cancellationToken)
    {
        var secret = await _secretRepository.GetServicePrincipalSecretAsync(servicePrincipal, cancellationToken).ConfigureAwait(false);
        return new ClientSecretCredential(servicePrincipal.TenantId, servicePrincipal.ClientId, secret);
    }

    private ArmClient CreateArmClient(TokenCredential credential, AzureCloudEnvironment cloudEnvironment)
    {
        var options = new ArmClientOptions
        {
            Environment = cloudEnvironment switch
            {
                AzureCloudEnvironment.AzurePublicCloud => ArmEnvironment.AzurePublicCloud,
                AzureCloudEnvironment.AzureChinaCloud => ArmEnvironment.AzureChina,
                AzureCloudEnvironment.AzureUSGovernment => ArmEnvironment.AzureGovernment,
                AzureCloudEnvironment.AzureGermanyCloud => ArmEnvironment.AzureGermany,
                _ => ArmEnvironment.AzurePublicCloud
            }
        };

        return new ArmClient(credential, defaultSubscriptionId: null, options);
    }
}
