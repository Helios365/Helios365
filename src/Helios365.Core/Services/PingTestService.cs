using System.Diagnostics;
using System.Net.Http;
using Helios365.Core.Models;
using Helios365.Core.Repositories;
using Helios365.Core.Utilities;
using Microsoft.Extensions.Logging;
using NetHttpMethod = System.Net.Http.HttpMethod;
using ResourceActionHttpMethod = Helios365.Core.Models.HttpMethod;

namespace Helios365.Core.Services;

public interface IPingTestService
{
    Task<PingTest?> SavePingTestAsync(Resource resource, PingTest test, CancellationToken cancellationToken = default);
    Task<bool> ClearPingTestAsync(Resource resource, CancellationToken cancellationToken = default);
    Task<PingTestResult?> RunPingTestAsync(Resource resource, CancellationToken cancellationToken = default);
    Task<PingTestResult> ExecuteAsync(PingTest test, CancellationToken cancellationToken = default);
}

/// <summary>
/// Executes and persists HTTP-based ping tests.
/// </summary>
public class PingTestService : IPingTestService
{
    private readonly IPingTestRepository _pingTestRepository;
    private readonly HttpClient _httpClient;
    private readonly ILogger<PingTestService> _logger;

    public PingTestService(
        IPingTestRepository pingTestRepository,
        HttpClient httpClient,
        ILogger<PingTestService> logger)
    {
        _pingTestRepository = pingTestRepository;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<PingTest?> SavePingTestAsync(Resource resource, PingTest test, CancellationToken cancellationToken = default)
    {
        Normalize(resource, test);

        var existing = await _pingTestRepository.GetByResourceIdAsync(resource.CustomerId, test.ResourceId ?? string.Empty, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            test.Id = Guid.NewGuid().ToString("N");
            test.LastRunAt = null;
            test.LastSucceeded = null;
            test.LastStatusCode = null;
            test.LastError = null;
            return await _pingTestRepository.CreateAsync(test, cancellationToken).ConfigureAwait(false);
        }

        test.Id = existing.Id;
        return await _pingTestRepository.UpdateAsync(existing.Id, test, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ClearPingTestAsync(Resource resource, CancellationToken cancellationToken = default)
    {
        var existing = await _pingTestRepository.GetByResourceIdAsync(resource.CustomerId, resource.ResourceId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return false;
        }

        return await _pingTestRepository.DeleteAsync(existing.Id, resource.CustomerId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PingTestResult?> RunPingTestAsync(Resource resource, CancellationToken cancellationToken = default)
    {
        var test = await _pingTestRepository.GetByResourceIdAsync(resource.CustomerId, resource.ResourceId, cancellationToken).ConfigureAwait(false);
        if (test is null)
        {
            _logger.LogInformation("No ping test configured for resource {ResourceId}", resource.ResourceId);
            return null;
        }

        var result = await ExecuteAsync(test, cancellationToken).ConfigureAwait(false);

        test.LastRunAt = result.CheckedAt;
        test.LastSucceeded = result.Succeeded;
        test.LastStatusCode = result.StatusCode;
        test.LastError = result.Error;

        await _pingTestRepository.UpdateAsync(test.Id, test, cancellationToken).ConfigureAwait(false);

        return result;
    }

    public async Task<PingTestResult> ExecuteAsync(PingTest test, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(test);

        if (string.IsNullOrWhiteSpace(test.Url) || !Uri.TryCreate(test.Url, UriKind.Absolute, out var uri))
        {
            return new PingTestResult
            {
                Succeeded = false,
                Error = "Ping test URL is missing or invalid."
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
                    "Ping test returned {StatusCode} but expected {ExpectedStatusCode} for URL {Url}",
                    (int)response.StatusCode,
                    test.ExpectedStatusCode,
                    test.Url);
            }

            return new PingTestResult
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
            _logger.LogWarning(ex, "Ping test timed out after {TimeoutSeconds}s for URL {Url}", timeoutSeconds, test.Url);
            return new PingTestResult
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
            _logger.LogError(ex, "Ping test failed for URL {Url}", test.Url);
            return new PingTestResult
            {
                Succeeded = false,
                Error = ex.Message,
                DurationMs = stopwatch.ElapsedMilliseconds,
                CheckedAt = DateTime.UtcNow
            };
        }
    }

    private static void Normalize(Resource resource, PingTest test)
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
