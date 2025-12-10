using Azure.Core;
using Azure.ResourceManager.Resources;
using Helios365.Core.Contracts.Diagnostics;
using Helios365.Core.Models;
using Helios365.Core.Services.Clients;
using Helios365.Core.Services.Handlers;
using Helios365.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace Helios365.Core.Services;

public interface IResourceService
{
    Task<TenantResource> GetTenantAsync(ServicePrincipal servicePrincipal, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetAccessibleSubscriptionsAsync(ServicePrincipal servicePrincipal, CancellationToken cancellationToken = default);

    Task<Resource> GetResourceAsync(ServicePrincipal servicePrincipal, Resource resource, CancellationToken cancellationToken = default);

    Task<string?> GetStatusAsync(ServicePrincipal servicePrincipal, Resource resource, CancellationToken cancellationToken = default);

    Task<bool> StartAsync(ServicePrincipal servicePrincipal, Resource resource, CancellationToken cancellationToken = default);

    Task<bool> StopAsync(ServicePrincipal servicePrincipal, Resource resource, CancellationToken cancellationToken = default);

    Task<bool> RestartAsync(ServicePrincipal servicePrincipal, Resource resource, RestartAction action, CancellationToken cancellationToken = default);

    Task<WebTestResult?> RunHealthCheckAsync(Resource resource, CancellationToken cancellationToken = default);

    Task<WebTest?> SavePingTestAsync(Resource resource, WebTest test, CancellationToken cancellationToken = default);

    Task<bool> ClearPingTestAsync(Resource resource, CancellationToken cancellationToken = default);

    Task<DiagnosticsResult?> GetDiagnosticsAsync(ServicePrincipal servicePrincipal, Resource resource, CancellationToken cancellationToken = default);

    Task<MetricsResult?> GetMetricsAsync(ServicePrincipal servicePrincipal, Resource resource, CancellationToken cancellationToken = default);

    bool SupportsLifecycle(string resourceType);

    bool SupportsDiagnostics(string resourceType);
}

public class ResourceService : IResourceService
{
    private readonly IArmClientFactory _armClientFactory;
    private readonly ILogger<ResourceService> _logger;
    private readonly IWebTestService _pingTestService;
    private readonly IReadOnlyDictionary<string, IResourceHandler> _handlersByType;

    public ResourceService(
        IArmClientFactory armClientFactory,
        ILogger<ResourceService> logger,
        IEnumerable<IResourceHandler> handlers,
        IWebTestService pingTestService)
    {
        _armClientFactory = armClientFactory;
        _logger = logger;
        _pingTestService = pingTestService;
        _handlersByType = handlers.ToDictionary(h => h.ResourceType, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<Resource> GetResourceAsync(ServicePrincipal servicePrincipal, Resource resource, CancellationToken cancellationToken = default)
    {
        var armClient = await _armClientFactory.CreateAsync(servicePrincipal, cancellationToken).ConfigureAwait(false);
        var azureResource = await armClient.GetGenericResource(new ResourceIdentifier(resource.ResourceId)).GetAsync(cancellationToken).ConfigureAwait(false);

        return ResourceMappingHelpers.FromArm(azureResource.Value.Data, servicePrincipal.CustomerId, servicePrincipal.Id); 

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

    public async Task<bool> RestartAsync(ServicePrincipal servicePrincipal, Resource resource, RestartAction action, CancellationToken cancellationToken = default)
    {
        var handler = ResolveHandler<IResourceLifecycle>(resource.ResourceType);
        if (handler is null)
        {
            _logger.LogWarning("Restart not supported for resource type {ResourceType}", resource.ResourceType);
            return false;
        }

        return await handler.RestartAsync(servicePrincipal, resource, action, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> GetStatusAsync(ServicePrincipal servicePrincipal, Resource resource, CancellationToken cancellationToken = default)
    {
        var handler = ResolveHandler<IResourceLifecycle>(resource.ResourceType);
        if (handler is null)
        {
            _logger.LogWarning("Status not supported for resource type {ResourceType}", resource.ResourceType);
            return null;
        }

        return await handler.GetStatusAsync(servicePrincipal, resource, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> StartAsync(ServicePrincipal servicePrincipal, Resource resource, CancellationToken cancellationToken = default)
    {
        var handler = ResolveHandler<IResourceLifecycle>(resource.ResourceType);
        if (handler is null)
        {
            _logger.LogWarning("Start not supported for resource type {ResourceType}", resource.ResourceType);
            return false;
        }

        return await handler.StartAsync(servicePrincipal, resource, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> StopAsync(ServicePrincipal servicePrincipal, Resource resource, CancellationToken cancellationToken = default)
    {
        var handler = ResolveHandler<IResourceLifecycle>(resource.ResourceType);
        if (handler is null)
        {
            _logger.LogWarning("Stop not supported for resource type {ResourceType}", resource.ResourceType);
            return false;
        }

        return await handler.StopAsync(servicePrincipal, resource, cancellationToken).ConfigureAwait(false);
    }

    public Task<WebTestResult?> RunHealthCheckAsync(Resource resource, CancellationToken cancellationToken = default) =>
        _pingTestService.RunWebTestAsync(resource, cancellationToken);

    public Task<WebTest?> SavePingTestAsync(Resource resource, WebTest test, CancellationToken cancellationToken = default) =>
        _pingTestService.SaveWebTestAsync(resource, test, cancellationToken);

    public Task<bool> ClearPingTestAsync(Resource resource, CancellationToken cancellationToken = default) =>
        _pingTestService.ClearWebTestAsync(resource, cancellationToken);

    public async Task<DiagnosticsResult?> GetDiagnosticsAsync(ServicePrincipal servicePrincipal, Resource resource, CancellationToken cancellationToken = default)
    {
        var handler = ResolveHandler<IResourceDiagnostics>(resource.ResourceType);
        if (handler is null)
        {
            _logger.LogWarning("Diagnostics not supported for resource type {ResourceType}", resource.ResourceType);
            return null;
        }

        return await handler.GetDiagnosticsAsync(servicePrincipal, resource, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MetricsResult?> GetMetricsAsync(ServicePrincipal servicePrincipal, Resource resource, CancellationToken cancellationToken = default)
    {
        var handler = ResolveHandler<IResourceDiagnostics>(resource.ResourceType);
        if (handler is null)
        {
            _logger.LogWarning("Metrics not supported for resource type {ResourceType}", resource.ResourceType);
            return null;
        }

        return await handler.GetMetricsAsync(servicePrincipal, resource, cancellationToken).ConfigureAwait(false);
    }

    private TCapability? ResolveHandler<TCapability>(string resourceType)
        where TCapability : class, IResourceHandler
    {
        if (_handlersByType.TryGetValue(resourceType, out var handler) && handler is TCapability typed)
        {
            return typed;
        }

        return null;
    }

    public bool SupportsLifecycle(string resourceType) =>
        ResolveHandler<IResourceLifecycle>(resourceType) is not null;

    public bool SupportsDiagnostics(string resourceType) =>
        ResolveHandler<IResourceDiagnostics>(resourceType) is not null;
}
