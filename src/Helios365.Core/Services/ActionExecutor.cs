using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.Compute;
using Azure.Security.KeyVault.Secrets;
using Helios365.Core.Exceptions;
using Helios365.Core.Models;
using Microsoft.Extensions.Logging;

namespace Helios365.Core.Services;

public class ActionExecutor : IActionExecutor
{
    private readonly SecretClient _secretClient;
    private readonly HttpClient _httpClient;
    private readonly ILogger<ActionExecutor> _logger;

    public ActionExecutor(SecretClient secretClient, HttpClient httpClient, ILogger<ActionExecutor> logger)
    {
        _secretClient = secretClient;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<bool> ExecuteAsync(ActionBase action, Resource resource, ServicePrincipal servicePrincipal, CancellationToken cancellationToken = default)
    {
        try
        {
            return action switch
            {
                HealthCheckAction hca => await ExecuteHealthCheckAsync(hca, cancellationToken),
                RestartAction ra => await ExecuteRestartAsync(ra, resource, servicePrincipal, cancellationToken),
                ScaleAction sa => await ExecuteScaleAsync(sa, resource, servicePrincipal, cancellationToken),
                _ => throw new ActionExecutionException($"Unknown action type: {action.Type}")
            };
        }
        catch (ActionExecutionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing action {ActionId}", action.Id);
            throw new ActionExecutionException($"Failed to execute action: {ex.Message}", ex);
        }
    }

    private async Task<bool> ExecuteHealthCheckAsync(HealthCheckAction action, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Executing health check on {Url}", action.Url);

            var request = new HttpRequestMessage(
                action.Method == Models.HttpMethod.GET ? System.Net.Http.HttpMethod.Get : System.Net.Http.HttpMethod.Post,
                action.Url
            );

            foreach (var header in action.Headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(action.TimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);

            var response = await _httpClient.SendAsync(request, linkedCts.Token);
            var isHealthy = (int)response.StatusCode == action.ExpectedStatusCode;

            if (isHealthy)
            {
                _logger.LogInformation("Health check passed for {Url}", action.Url);
            }
            else
            {
                _logger.LogWarning("Health check failed for {Url}: expected {Expected}, got {Actual}",
                    action.Url, action.ExpectedStatusCode, (int)response.StatusCode);
            }

            return isHealthy;
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Health check timed out for {Url}", action.Url);
            return false;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Health check request failed for {Url}", action.Url);
            return false;
        }
    }

    private async Task<bool> ExecuteRestartAsync(RestartAction action, Resource resource, ServicePrincipal servicePrincipal, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Executing restart for resource {ResourceId}", resource.ResourceId);

            // Wait before restarting if configured
            if (action.WaitBeforeSeconds > 0)
            {
                _logger.LogInformation("Waiting {Seconds} seconds before restart", action.WaitBeforeSeconds);
                await Task.Delay(TimeSpan.FromSeconds(action.WaitBeforeSeconds), cancellationToken);
            }

            // Get service principal secret from Key Vault
            // TODO: migrate to using ServicePrincipal.ClientSecretKeyVaultReference (see AzureResourceGraphService)
            var secretName = $"sp-{servicePrincipal.Id}";
            var secret = await _secretClient.GetSecretAsync(secretName, cancellationToken: cancellationToken);

            // Create credential
            var credential = new ClientSecretCredential(
                servicePrincipal.TenantId,
                servicePrincipal.ClientId,
                secret.Value.Value
            );

            // Create ARM client with appropriate cloud environment
            var armClient = CreateArmClient(credential, servicePrincipal.CloudEnvironment);

            // Get resource identifier
            var resourceIdentifier = new ResourceIdentifier(resource.ResourceId);

            // Restart based on resource type
            bool success;
            if (resource.ResourceType.Contains("Microsoft.Web/sites", StringComparison.OrdinalIgnoreCase))
            {
                success = await RestartAppServiceAsync(armClient, resourceIdentifier, cancellationToken);
            }
            else if (resource.ResourceType.Contains("Microsoft.Compute/virtualMachines", StringComparison.OrdinalIgnoreCase))
            {
                success = await RestartVirtualMachineAsync(armClient, resourceIdentifier, cancellationToken);
            }
            else
            {
                _logger.LogWarning("Unsupported resource type for restart: {ResourceType}", resource.ResourceType);
                return false;
            }

            // Wait after restarting if configured
            if (success && action.WaitAfterSeconds > 0)
            {
                _logger.LogInformation("Waiting {Seconds} seconds after restart", action.WaitAfterSeconds);
                await Task.Delay(TimeSpan.FromSeconds(action.WaitAfterSeconds), cancellationToken);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restarting resource {ResourceId}", resource.ResourceId);
            return false;
        }
    }

    private async Task<bool> ExecuteScaleAsync(ScaleAction action, Resource resource, ServicePrincipal servicePrincipal, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Executing scale {Direction} for resource {ResourceId}", action.Direction, resource.ResourceId);

            // Get service principal secret from Key Vault
            var secretName = $"sp-{servicePrincipal.Id}";
            var secret = await _secretClient.GetSecretAsync(secretName, cancellationToken: cancellationToken);

            // Create credential
            var credential = new ClientSecretCredential(
                servicePrincipal.TenantId,
                servicePrincipal.ClientId,
                secret.Value.Value
            );

            // Create ARM client
            var armClient = CreateArmClient(credential, servicePrincipal.CloudEnvironment);

            // Get resource identifier
            var resourceIdentifier = new ResourceIdentifier(resource.ResourceId);

            // Scale based on resource type
            if (resource.ResourceType.Contains("Microsoft.Web/sites", StringComparison.OrdinalIgnoreCase))
            {
                return await ScaleAppServiceAsync(armClient, resourceIdentifier, action, cancellationToken);
            }
            else
            {
                _logger.LogWarning("Unsupported resource type for scaling: {ResourceType}", resource.ResourceType);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scaling resource {ResourceId}", resource.ResourceId);
            return false;
        }
    }

    private async Task<bool> RestartAppServiceAsync(ArmClient armClient, ResourceIdentifier resourceId, CancellationToken cancellationToken)
    {
        try
        {
            var site = armClient.GetWebSiteResource(resourceId);
            await site.RestartAsync(cancellationToken: cancellationToken);
            _logger.LogInformation("Successfully restarted App Service {ResourceId}", resourceId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restart App Service {ResourceId}", resourceId);
            return false;
        }
    }

    private async Task<bool> RestartVirtualMachineAsync(ArmClient armClient, ResourceIdentifier resourceId, CancellationToken cancellationToken)
    {
        try
        {
            var vm = armClient.GetVirtualMachineResource(resourceId);
            await vm.RestartAsync(Azure.WaitUntil.Started, cancellationToken);
            _logger.LogInformation("Successfully restarted VM {ResourceId}", resourceId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restart VM {ResourceId}", resourceId);
            return false;
        }
    }

    private async Task<bool> ScaleAppServiceAsync(ArmClient armClient, ResourceIdentifier resourceId, ScaleAction action, CancellationToken cancellationToken)
    {
        try
        {
            // This is a simplified implementation
            // In production, you'd need to get the App Service Plan and update its SKU/capacity
            var site = armClient.GetWebSiteResource(resourceId);
            
            // Get the server farm (App Service Plan)
            var serverFarmId = (await site.GetAsync(cancellationToken)).Value.Data.AppServicePlanId;
            
            if (serverFarmId is null)
            {
                _logger.LogWarning("Could not find App Service Plan for {ResourceId}", resourceId);
                return false;
            }

            var plan = armClient.GetAppServicePlanResource(serverFarmId);
            var planData = (await plan.GetAsync(cancellationToken)).Value.Data;

            // TODO: Implement actual scaling logic based on action.TargetInstanceCount or action.TargetSku
            _logger.LogInformation("Scaling App Service Plan {PlanId} - implementation needed", serverFarmId);
            
            // For now, just log success
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to scale App Service {ResourceId}", resourceId);
            return false;
        }
    }

    private ArmClient CreateArmClient(ClientSecretCredential credential, AzureCloudEnvironment cloudEnvironment)
    {
        // For now, only support public cloud
        // TODO: Add support for other cloud environments
        if (cloudEnvironment != AzureCloudEnvironment.AzurePublicCloud)
        {
            _logger.LogWarning("Non-public cloud environments not yet supported: {Environment}", cloudEnvironment);
        }

        return new ArmClient(credential);
    }
}
