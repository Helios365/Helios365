using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Helios365.Core.Models;
using Helios365.Core.Repositories;
using Microsoft.Extensions.Logging;

namespace Helios365.Core.Services.Clients;

public interface ICredentialProvider
{
    Task<TokenCredential> CreateAsync(ServicePrincipal servicePrincipal, CancellationToken cancellationToken = default);
}

public class CredentialProvider : ICredentialProvider
{
    private readonly ISecretRepository _secretRepository;
    private readonly ILogger<CredentialProvider> _logger;

    public CredentialProvider(ISecretRepository secretRepository, ILogger<CredentialProvider> logger)
    {
        _secretRepository = secretRepository;
        _logger = logger;
    }

    public async Task<TokenCredential> CreateAsync(ServicePrincipal servicePrincipal, CancellationToken cancellationToken = default)
    {
        if (servicePrincipal is null)
        {
            throw new ArgumentNullException(nameof(servicePrincipal));
        }

        var secret = await _secretRepository.GetServicePrincipalSecretAsync(servicePrincipal, cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Created credential for service principal {ServicePrincipalId}", servicePrincipal.Id);
        return new ClientSecretCredential(servicePrincipal.TenantId, servicePrincipal.ClientId, secret);
    }
}

public interface IArmClientFactory
{
    Task<ArmClient> CreateAsync(ServicePrincipal servicePrincipal, CancellationToken cancellationToken = default);
}

public class ArmClientFactory : IArmClientFactory
{
    private readonly ICredentialProvider _credentialProvider;
    private readonly ILogger<ArmClientFactory> _logger;

    public ArmClientFactory(ICredentialProvider credentialProvider, ILogger<ArmClientFactory> logger)
    {
        _credentialProvider = credentialProvider;
        _logger = logger;
    }

    public async Task<ArmClient> CreateAsync(ServicePrincipal servicePrincipal, CancellationToken cancellationToken = default)
    {
        if (servicePrincipal is null)
        {
            throw new ArgumentNullException(nameof(servicePrincipal));
        }

        var credential = await _credentialProvider.CreateAsync(servicePrincipal, cancellationToken).ConfigureAwait(false);
        var options = new ArmClientOptions
        {
            Environment = ToArmEnvironment(servicePrincipal.CloudEnvironment)
        };

        _logger.LogDebug("Created ArmClient for service principal {ServicePrincipalId} targeting {CloudEnvironment}", servicePrincipal.Id, servicePrincipal.CloudEnvironment);
        return new ArmClient(credential, defaultSubscriptionId: null, options);
    }

    private static ArmEnvironment ToArmEnvironment(AzureCloudEnvironment cloudEnvironment) =>
        cloudEnvironment switch
        {
            AzureCloudEnvironment.AzureChinaCloud => ArmEnvironment.AzureChina,
            AzureCloudEnvironment.AzureGermanyCloud => ArmEnvironment.AzureGermany,
            AzureCloudEnvironment.AzureUSGovernment => ArmEnvironment.AzureGovernment,
            _ => ArmEnvironment.AzurePublicCloud
        };
}
