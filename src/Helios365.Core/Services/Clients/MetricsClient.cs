using Azure;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;
using Azure.ResourceManager;
using Helios365.Core.Models;
using Helios365.Core.Contracts.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Helios365.Core.Services.Clients;

public interface IMetricsClient
{
    Task<MetricsResult> QueryAsync(
        ServicePrincipal servicePrincipal,
        string resourceId,
        string resourceType,
        IEnumerable<string> metricNames,
        string? metricNamespace = null,
        TimeSpan? duration = null,
        CancellationToken cancellationToken = default);
}

public class MetricsClient : IMetricsClient
{
    private readonly ICredentialProvider _credentialProvider;
    private readonly ILogger<MetricsClient> _logger;

    public MetricsClient(ICredentialProvider credentialProvider, ILogger<MetricsClient> logger)
    {
        _credentialProvider = credentialProvider;
        _logger = logger;
    }

    public async Task<MetricsResult> QueryAsync(
        ServicePrincipal servicePrincipal,
        string resourceId,
        string resourceType,
        IEnumerable<string> metricNames,
        string? metricNamespace = null,
        TimeSpan? duration = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(servicePrincipal);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);

        var metricList = metricNames?.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToArray() ?? Array.Empty<string>();
        if (metricList.Length == 0)
        {
            return new MetricsResult
            {
                ResourceId = resourceId,
                ResourceType = resourceType,
                Series = Array.Empty<MetricSeries>()
            };
        }

        try
        {
            var credential = await _credentialProvider.CreateAsync(servicePrincipal, cancellationToken).ConfigureAwait(false);
            var endpoint = GetMetricsEndpoint(servicePrincipal.CloudEnvironment);
            var clientOptions = new MetricsQueryClientOptions
            {
                Audience = GetMetricsAudience(servicePrincipal.CloudEnvironment)
            };
            var client = new MetricsQueryClient(endpoint, credential, clientOptions);

            var options = new MetricsQueryOptions();
            options.TimeRange = duration ?? TimeSpan.FromHours(3);
            if (!string.IsNullOrWhiteSpace(metricNamespace))
            {
                options.MetricNamespace = metricNamespace;
            }

            var response = await client.QueryResourceAsync(
                resourceId,
                metricList,
                options,
                cancellationToken).ConfigureAwait(false);

            var series = MapSeries(response);

            return new MetricsResult
            {
                ResourceId = resourceId,
                ResourceType = resourceType,
                Series = series
            };
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Metrics query failed for resource {ResourceId}", resourceId);
            return new MetricsResult
            {
                ResourceId = resourceId,
                ResourceType = resourceType,
                Series = Array.Empty<MetricSeries>()
            };
        }
    }

    private static Uri GetMetricsEndpoint(AzureCloudEnvironment cloudEnvironment) =>
        cloudEnvironment switch
        {
            AzureCloudEnvironment.AzureChinaCloud => new Uri("https://management.chinacloudapi.cn"),
            AzureCloudEnvironment.AzureUSGovernment => new Uri("https://management.usgovcloudapi.net"),
            _ => new Uri("https://management.azure.com")
        };

    private static MetricsQueryAudience GetMetricsAudience(AzureCloudEnvironment cloudEnvironment) =>
        cloudEnvironment switch
        {
            AzureCloudEnvironment.AzureChinaCloud => MetricsQueryAudience.AzureChina,
            AzureCloudEnvironment.AzureUSGovernment => MetricsQueryAudience.AzureGovernment,
            _ => MetricsQueryAudience.AzurePublicCloud
        };

    private static IReadOnlyList<MetricSeries> MapSeries(MetricsQueryResult response)
    {
        if (response.Metrics is null || response.Metrics.Count == 0)
        {
            return Array.Empty<MetricSeries>();
        }

        var result = new List<MetricSeries>();

        foreach (var metric in response.Metrics)
        {
            var points = new List<MetricPoint>();

            foreach (var timeSeries in metric.TimeSeries)
            {
                foreach (var data in timeSeries.Values)
                {
                    points.Add(new MetricPoint
                    {
                        Timestamp = data.TimeStamp.UtcDateTime,
                        Value = data.Average ?? data.Total ?? data.Maximum ?? data.Minimum ?? data.Count
                    });
                }
            }

            result.Add(new MetricSeries
            {
                Name = metric.Name,
                Unit = metric.Unit.ToString(),
                Points = points
            });
        }

        return result;
    }
}
