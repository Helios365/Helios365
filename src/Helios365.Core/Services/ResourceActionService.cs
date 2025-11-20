using System.Net.Http;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.Compute;
using Helios365.Core.Exceptions;
using Helios365.Core.Models;
using Helios365.Core.Repositories;
using Microsoft.Extensions.Logging;
using Azure;
using NetHttpMethod = System.Net.Http.HttpMethod;
using ResourceActionHttpMethod = Helios365.Core.Models.HttpMethod;

namespace Helios365.Core.Services;

public class ResourceActionService : IResourceActionService
{
    private const string AppServiceResourceType = "microsoft.web/sites";
    private const string VirtualMachineResourceType = "microsoft.compute/virtualmachines";

    private readonly IServicePrincipalRepository _servicePrincipalRepository;
    private readonly ISecretRepository _secretRepository;
    private readonly HttpClient _httpClient;
    private readonly ILogger<ResourceActionService> _logger;

    public ResourceActionService(
        IServicePrincipalRepository servicePrincipalRepository,
        ISecretRepository secretRepository,
        HttpClient httpClient,
        ILogger<ResourceActionService> logger)
    {
        _servicePrincipalRepository = servicePrincipalRepository ?? throw new ArgumentNullException(nameof(servicePrincipalRepository));
        _secretRepository = secretRepository ?? throw new ArgumentNullException(nameof(secretRepository));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> Execute(Resource resource, ActionBase action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(action);

        if (!action.Enabled)
        {
            _logger.LogWarning("Action {ActionId} is disabled. Skipping execution for resource {ResourceId}", action.Id, resource.ResourceId);
            return false;
        }

        return action switch
        {
            HealthCheckAction healthCheck => await ExecuteHealthCheckAsync(resource, healthCheck, cancellationToken).ConfigureAwait(false),
            RestartAction restart => await ExecuteRestartAsync(resource, restart, cancellationToken).ConfigureAwait(false),
            ScaleAction scale => await ExecuteScaleAsync(resource, scale, cancellationToken).ConfigureAwait(false),
            _ => throw new NotSupportedException($"Action type {action.GetType().Name} is not supported.")
        };
    }

    public IReadOnlyCollection<ActionType> GetSupportedActions(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        
        if (IsResourceType(resource, AppServiceResourceType))
        {
            return new[] { ActionType.HealthCheck, ActionType.Restart, ActionType.Scale };
        }

        if (IsResourceType(resource, VirtualMachineResourceType))
        {
            return new[] { ActionType.Restart };
        }

        return Array.Empty<ActionType>();
    }

    private async Task<bool> ExecuteHealthCheckAsync(Resource resource, HealthCheckAction action, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(action.Url))
        {
            _logger.LogWarning("Health check action {ActionId} does not define a URL.", action.Id);
            return false;
        }

        if (!Uri.TryCreate(action.Url, UriKind.Absolute, out var uri))
        {
            _logger.LogWarning("Health check action {ActionId} has an invalid URL: {Url}", action.Id, action.Url);
            return false;
        }

        using var request = new HttpRequestMessage(ConvertHttpMethod(action.Method), uri);

        if (action.Method == ResourceActionHttpMethod.POST && request.Content is null)
        {
            request.Content = new StringContent(string.Empty);
        }

        if (action.Headers is { Count: > 0 })
        {
            foreach (var header in action.Headers)
            {
                if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
                {
                    request.Content ??= new StringContent(string.Empty);
                    request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }

        var timeoutSeconds = action.TimeoutSeconds > 0 ? action.TimeoutSeconds : 30;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            var response = await _httpClient.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
            var succeeded = (int)response.StatusCode == action.ExpectedStatusCode;

            if (!succeeded)
            {
                _logger.LogWarning(
                    "Health check action {ActionId} for resource {ResourceId} returned status {StatusCode} but expected {ExpectedStatusCode}",
                    action.Id,
                    resource.ResourceId,
                    (int)response.StatusCode,
                    action.ExpectedStatusCode);
            }

            return succeeded;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Health check action {ActionId} timed out after {Timeout}s", action.Id, timeoutSeconds);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check action {ActionId} failed for resource {ResourceId}", action.Id, resource.ResourceId);
            return false;
        }
    }

    private async Task<bool> ExecuteRestartAsync(Resource resource, RestartAction action, CancellationToken cancellationToken)
    {
        var identifier = CreateResourceIdentifier(resource);

        try
        {
            var (armClient, servicePrincipal) = await CreateArmClientAsync(resource, cancellationToken).ConfigureAwait(false);

            if (action.WaitBeforeSeconds > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(action.WaitBeforeSeconds), cancellationToken).ConfigureAwait(false);
            }

            if (IsResourceType(resource, AppServiceResourceType))
            {
                var site = armClient.GetWebSiteResource(identifier);
                await site.RestartAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                await DelayAfterActionAsync(action.WaitAfterSeconds, cancellationToken).ConfigureAwait(false);

                _logger.LogInformation("Restarted App Service {ResourceId} using service principal {ServicePrincipalId}", resource.ResourceId, servicePrincipal.Id);
                return true;
            }

            if (IsResourceType(resource, VirtualMachineResourceType))
            {
                var virtualMachine = armClient.GetVirtualMachineResource(identifier);
                await virtualMachine.RestartAsync(WaitUntil.Completed, cancellationToken).ConfigureAwait(false);
                await DelayAfterActionAsync(action.WaitAfterSeconds, cancellationToken).ConfigureAwait(false);

                _logger.LogInformation("Restarted virtual machine {ResourceId} using service principal {ServicePrincipalId}", resource.ResourceId, servicePrincipal.Id);
                return true;
            }

            _logger.LogWarning("Restart action {ActionId} is not supported for resource type {ResourceType}", action.Id, resource.ResourceType);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restart resource {ResourceId} for action {ActionId}", resource.ResourceId, action.Id);
            return false;
        }
    }

    private async Task<bool> ExecuteScaleAsync(Resource resource, ScaleAction action, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Scale action {ActionId} is not implemented for resource type {ResourceType}.", action.Id, resource.ResourceType);
        return false;
    }

    private async Task<(ArmClient Client, ServicePrincipal Principal)> CreateArmClientAsync(Resource resource, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(resource.ServicePrincipalId))
        {
            throw new InvalidOperationException($"Resource {resource.Id} is not associated with a service principal.");
        }

        var servicePrincipal = await _servicePrincipalRepository
            .GetAsync(resource.ServicePrincipalId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Service principal {resource.ServicePrincipalId} was not found.");

        var secret = await _secretRepository
            .GetServicePrincipalSecretAsync(servicePrincipal, cancellationToken)
            .ConfigureAwait(false);

        var credential = new ClientSecretCredential(servicePrincipal.TenantId, servicePrincipal.ClientId, secret);
        var clientOptions = new ArmClientOptions
        {
            Environment = servicePrincipal.CloudEnvironment switch
            {
                AzureCloudEnvironment.AzureChinaCloud => ArmEnvironment.AzureChina,
                AzureCloudEnvironment.AzureGermanyCloud => ArmEnvironment.AzureGermany,
                AzureCloudEnvironment.AzureUSGovernment => ArmEnvironment.AzureGovernment,
                _ => ArmEnvironment.AzurePublicCloud
            }
        };

        return (new ArmClient(credential, defaultSubscriptionId: null, clientOptions), servicePrincipal);
    }

    private static ResourceIdentifier CreateResourceIdentifier(Resource resource)
    {
        if (string.IsNullOrWhiteSpace(resource.ResourceId))
        {
            throw new InvalidOperationException("ResourceId cannot be empty when executing an action.");
        }

        return new ResourceIdentifier(resource.ResourceId);
    }

    private static bool IsResourceType(Resource resource, string resourceType) =>
        string.Equals(resource.ResourceType, resourceType, StringComparison.OrdinalIgnoreCase);

    private static NetHttpMethod ConvertHttpMethod(ResourceActionHttpMethod method) =>
        method switch
        {
            ResourceActionHttpMethod.POST => NetHttpMethod.Post,
            _ => NetHttpMethod.Get
        };

    private static async Task DelayAfterActionAsync(int waitAfterSeconds, CancellationToken cancellationToken)
    {
        if (waitAfterSeconds > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(waitAfterSeconds), cancellationToken).ConfigureAwait(false);
        }
    }
}
