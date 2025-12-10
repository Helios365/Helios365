using System.Diagnostics;
using Helios365.Core.Models;
using Helios365.Core.Repositories;
using Helios365.Core.Utilities;
using Microsoft.Extensions.Logging;
using NetHttpMethod = System.Net.Http.HttpMethod;
using ResourceActionHttpMethod = Helios365.Core.Models.HttpMethod;

namespace Helios365.Core.Services;

public interface IWebTestService
{
    Task<WebTest?> SaveWebTestAsync(Resource resource, WebTest test, CancellationToken cancellationToken = default);
    Task<bool> ClearWebTestAsync(Resource resource, CancellationToken cancellationToken = default);
    Task<WebTestResult?> RunWebTestAsync(Resource resource, CancellationToken cancellationToken = default);
    Task<WebTestResult> ExecuteAsync(WebTest test, CancellationToken cancellationToken = default);
}

/// <summary>
/// Executes and persists HTTP-based web tests.
/// </summary>
public class WebTestService : IWebTestService
{
    private readonly IWebTestRepository _webTestRepository;
    private readonly HttpClient _httpClient;
    private readonly ILogger<WebTestService> _logger;

    public WebTestService(
        IWebTestRepository webTestRepository,
        HttpClient httpClient,
        ILogger<WebTestService> logger)
    {
        _webTestRepository = webTestRepository;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<WebTest?> SaveWebTestAsync(Resource resource, WebTest test, CancellationToken cancellationToken = default)
    {
        Normalize(resource, test);

        var existing = await _webTestRepository.GetByResourceIdAsync(resource.CustomerId, test.ResourceId ?? string.Empty, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            test.Id = Guid.NewGuid().ToString("N");
            test.LastRunAt = null;
            test.LastSucceeded = null;
            test.LastStatusCode = null;
            test.LastError = null;
            return await _webTestRepository.CreateAsync(test, cancellationToken).ConfigureAwait(false);
        }

        test.Id = existing.Id;
        return await _webTestRepository.UpdateAsync(existing.Id, test, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ClearWebTestAsync(Resource resource, CancellationToken cancellationToken = default)
    {
        var existing = await _webTestRepository.GetByResourceIdAsync(resource.CustomerId, resource.ResourceId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return false;
        }

        return await _webTestRepository.DeleteAsync(existing.Id, resource.CustomerId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<WebTestResult?> RunWebTestAsync(Resource resource, CancellationToken cancellationToken = default)
    {
        var test = await _webTestRepository.GetByResourceIdAsync(resource.CustomerId, resource.ResourceId, cancellationToken).ConfigureAwait(false);
        if (test is null)
        {
            _logger.LogInformation("No web test configured for resource {ResourceId}", resource.ResourceId);
            return null;
        }

        var result = await ExecuteAsync(test, cancellationToken).ConfigureAwait(false);

        test.LastRunAt = result.CheckedAt;
        test.LastSucceeded = result.Succeeded;
        test.LastStatusCode = result.StatusCode;
        test.LastError = result.Error;

        await _webTestRepository.UpdateAsync(test.Id, test, cancellationToken).ConfigureAwait(false);

        return result;
    }

    public async Task<WebTestResult> ExecuteAsync(WebTest test, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(test);

        if (string.IsNullOrWhiteSpace(test.Url) || !Uri.TryCreate(test.Url, UriKind.Absolute, out var uri))
        {
            return new WebTestResult
            {
                Succeeded = false,
                Error = "Web test URL is missing or invalid."
            };
        }

        using var request = new HttpRequestMessage(ConvertHttpMethod(test.Method), uri);

        if (test.Method == ResourceActionHttpMethod.POST && request.Content is null)
        {
            request.Content = new StringContent(string.Empty);
        }

        if (test.Headers is { Count: > 0 })
        {
            foreach (var header in test.Headers)
            {
                if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
                {
                    request.Content ??= new StringContent(string.Empty);
                    request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }

        var timeoutSeconds = test.TimeoutSeconds > 0 ? test.TimeoutSeconds : 30;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await _httpClient.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
            stopwatch.Stop();

            var succeeded = (int)response.StatusCode == test.ExpectedStatusCode;
            if (!succeeded)
            {
                _logger.LogWarning(
                    "Web test returned {StatusCode} but expected {ExpectedStatusCode} for URL {Url}",
                    (int)response.StatusCode,
                    test.ExpectedStatusCode,
                    test.Url);
            }

            return new WebTestResult
            {
                Succeeded = succeeded,
                StatusCode = (int)response.StatusCode,
                DurationMs = stopwatch.ElapsedMilliseconds,
                CheckedAt = DateTime.UtcNow
            };
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex, "Web test timed out after {TimeoutSeconds}s for URL {Url}", timeoutSeconds, test.Url);
            return new WebTestResult
            {
                Succeeded = false,
                Error = $"Timed out after {timeoutSeconds}s",
                DurationMs = stopwatch.ElapsedMilliseconds,
                CheckedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Web test failed for URL {Url}", test.Url);
            return new WebTestResult
            {
                Succeeded = false,
                Error = ex.Message,
                DurationMs = stopwatch.ElapsedMilliseconds,
                CheckedAt = DateTime.UtcNow
            };
        }
    }

    private static void Normalize(Resource resource, WebTest test)
    {
        test.CustomerId = resource.CustomerId;
        test.ResourceId = Normalizers.NormalizeResourceId(resource.ResourceId);
    }

    private static NetHttpMethod ConvertHttpMethod(ResourceActionHttpMethod method) =>
        method switch
        {
            ResourceActionHttpMethod.POST => NetHttpMethod.Post,
            _ => NetHttpMethod.Get
        };
}
