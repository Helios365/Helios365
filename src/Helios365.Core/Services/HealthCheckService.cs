using Helios365.Core.Exceptions;
using Helios365.Core.Models;
using Microsoft.Extensions.Logging;
using System.Net.Sockets;

namespace Helios365.Core.Services;

public class HealthCheckService : IHealthCheckService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HealthCheckService> _logger;

    public HealthCheckService(HttpClient httpClient, ILogger<HealthCheckService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<bool> CheckAsync(HealthCheckConfig config, CancellationToken cancellationToken = default)
    {
        try
        {
            return config.Type switch
            {
                HealthCheckType.HttpGet => await CheckHttpGetAsync(config, cancellationToken),
                HealthCheckType.HttpPost => await CheckHttpPostAsync(config, cancellationToken),
                HealthCheckType.TcpPort => await CheckTcpPortAsync(config, cancellationToken),
                HealthCheckType.AzureResourceStatus => await CheckAzureResourceAsync(config, cancellationToken),
                _ => throw new HealthCheckException($"Unsupported health check type: {config.Type}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            throw new HealthCheckException($"Health check failed: {ex.Message}", ex);
        }
    }

    private async Task<bool> CheckHttpGetAsync(HealthCheckConfig config, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(config.Endpoint))
        {
            throw new HealthCheckException("Endpoint is required for HTTP GET check");
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(config.TimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);

            var request = new HttpRequestMessage(HttpMethod.Get, config.Endpoint);
            foreach (var header in config.Headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            var response = await _httpClient.SendAsync(request, linkedCts.Token);
            var isHealthy = (int)response.StatusCode == config.ExpectedStatusCode;

            if (isHealthy)
            {
                _logger.LogInformation("Health check passed for {Endpoint}", config.Endpoint);
            }
            else
            {
                _logger.LogWarning("Health check failed for {Endpoint}: expected {Expected}, got {Actual}",
                    config.Endpoint, config.ExpectedStatusCode, (int)response.StatusCode);
            }

            return isHealthy;
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Health check timed out for {Endpoint}", config.Endpoint);
            return false;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Health check request failed for {Endpoint}", config.Endpoint);
            return false;
        }
    }

    private async Task<bool> CheckHttpPostAsync(HealthCheckConfig config, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(config.Endpoint))
        {
            throw new HealthCheckException("Endpoint is required for HTTP POST check");
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(config.TimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);

            var request = new HttpRequestMessage(HttpMethod.Post, config.Endpoint);
            foreach (var header in config.Headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (!string.IsNullOrEmpty(config.Body))
            {
                request.Content = new StringContent(config.Body);
            }

            var response = await _httpClient.SendAsync(request, linkedCts.Token);
            var isHealthy = (int)response.StatusCode == config.ExpectedStatusCode;

            if (isHealthy)
            {
                _logger.LogInformation("Health check passed for {Endpoint}", config.Endpoint);
            }
            else
            {
                _logger.LogWarning("Health check failed for {Endpoint}: expected {Expected}, got {Actual}",
                    config.Endpoint, config.ExpectedStatusCode, (int)response.StatusCode);
            }

            return isHealthy;
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Health check timed out for {Endpoint}", config.Endpoint);
            return false;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Health check request failed for {Endpoint}", config.Endpoint);
            return false;
        }
    }

    private async Task<bool> CheckTcpPortAsync(HealthCheckConfig config, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(config.Endpoint) || !config.TcpPort.HasValue)
        {
            throw new HealthCheckException("Endpoint and TCP port are required for TCP check");
        }

        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(config.TimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);

            await client.ConnectAsync(config.Endpoint, config.TcpPort.Value, linkedCts.Token);
            _logger.LogInformation("TCP port check passed for {Endpoint}:{Port}", config.Endpoint, config.TcpPort);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TCP port check failed for {Endpoint}:{Port}", config.Endpoint, config.TcpPort);
            return false;
        }
    }

    private Task<bool> CheckAzureResourceAsync(HealthCheckConfig config, CancellationToken cancellationToken)
    {
        // This would require Azure SDK and credentials
        // Implementation should be in Processor where Azure context is available
        throw new NotImplementedException("Azure resource status check should be implemented in Processor project");
    }
}
