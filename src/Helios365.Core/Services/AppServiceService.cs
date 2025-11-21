using System.Net.Http;
using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.AppService;
using Helios365.Core.Models;
using Microsoft.Extensions.Logging;
using NetHttpMethod = System.Net.Http.HttpMethod;
using ResourceActionHttpMethod = Helios365.Core.Models.HttpMethod;

namespace Helios365.Core.Services;

public interface IAppServiceService
{
    Task<IReadOnlyList<WebSiteResource>> ListAsync(ServicePrincipal servicePrincipal, string subscriptionId, CancellationToken cancellationToken = default);

    Task<WebSiteResource?> GetAsync(ServicePrincipal servicePrincipal, string resourceId, CancellationToken cancellationToken = default);

    Task<bool> RestartAsync(ServicePrincipal servicePrincipal, string resourceId, RestartAction action, CancellationToken cancellationToken = default);

    Task<bool> HealthCheckAsync(HealthCheckAction action, CancellationToken cancellationToken = default);

    Task<bool> ScaleAsync(ServicePrincipal servicePrincipal, string resourceId, ScaleAction action, CancellationToken cancellationToken = default);
}

public class AppServiceService : IAppServiceService
{
    private readonly IArmClientFactory _armClientFactory;
    private readonly HttpClient _httpClient;
    private readonly ILogger<AppServiceService> _logger;

    public AppServiceService(IArmClientFactory armClientFactory, HttpClient httpClient, ILogger<AppServiceService> logger)
    {
        _armClientFactory = armClientFactory;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<WebSiteResource>> ListAsync(ServicePrincipal servicePrincipal, string subscriptionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(servicePrincipal);

        if (string.IsNullOrWhiteSpace(subscriptionId))
        {
            throw new ArgumentException("SubscriptionId is required.", nameof(subscriptionId));
        }

        var armClient = await _armClientFactory.CreateAsync(servicePrincipal, cancellationToken).ConfigureAwait(false);
        var subscription = armClient.GetSubscriptionResource(new ResourceIdentifier($"/subscriptions/{subscriptionId.Trim()}"));

        var appServices = new List<WebSiteResource>();
        await foreach (var site in subscription.GetWebSitesAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            appServices.Add(site);
        }

        _logger.LogInformation("Found {AppServiceCount} App Services in subscription {SubscriptionId}", appServices.Count, subscriptionId);
        return appServices;
    }

    public async Task<WebSiteResource?> GetAsync(ServicePrincipal servicePrincipal, string resourceId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(servicePrincipal);

        if (string.IsNullOrWhiteSpace(resourceId))
        {
            throw new ArgumentException("ResourceId is required.", nameof(resourceId));
        }

        try
        {
            var armClient = await _armClientFactory.CreateAsync(servicePrincipal, cancellationToken).ConfigureAwait(false);
            var site = armClient.GetWebSiteResource(new ResourceIdentifier(resourceId));
            var response = await site.GetAsync(cancellationToken).ConfigureAwait(false);
            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning(ex, "App Service {ResourceId} was not found.", resourceId);
            return null;
        }
    }

    public async Task<bool> RestartAsync(ServicePrincipal servicePrincipal, string resourceId, RestartAction action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(servicePrincipal);
        ArgumentNullException.ThrowIfNull(action);

        if (string.IsNullOrWhiteSpace(resourceId))
        {
            throw new ArgumentException("ResourceId is required.", nameof(resourceId));
        }

        try
        {
            var armClient = await _armClientFactory.CreateAsync(servicePrincipal, cancellationToken).ConfigureAwait(false);
            var site = armClient.GetWebSiteResource(new ResourceIdentifier(resourceId));

            if (action.WaitBeforeSeconds > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(action.WaitBeforeSeconds), cancellationToken).ConfigureAwait(false);
            }

            await site.RestartAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            await DelayAfterActionAsync(action.WaitAfterSeconds, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Restarted App Service {ResourceId} using service principal {ServicePrincipalId}", resourceId, servicePrincipal.Id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restart App Service {ResourceId}", resourceId);
            return false;
        }
    }

    public async Task<bool> HealthCheckAsync(HealthCheckAction action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

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
                    "Health check action {ActionId} returned status {StatusCode} but expected {ExpectedStatusCode}",
                    action.Id,
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
            _logger.LogError(ex, "Health check action {ActionId} failed for URL {Url}", action.Id, action.Url);
            return false;
        }
    }

    public Task<bool> ScaleAsync(ServicePrincipal servicePrincipal, string resourceId, ScaleAction action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(servicePrincipal);
        ArgumentNullException.ThrowIfNull(action);

        if (string.IsNullOrWhiteSpace(resourceId))
        {
            throw new ArgumentException("ResourceId is required.", nameof(resourceId));
        }

        _logger.LogWarning("Scale action {ActionId} is not yet implemented for App Services.", action.Id);
        return Task.FromResult(false);
    }

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
