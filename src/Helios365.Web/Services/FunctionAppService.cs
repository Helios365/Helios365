using Microsoft.Extensions.Options;

namespace Helios365.Web.Services;

public interface IFunctionAppService
{
    Task<bool> EscalateAlertAsync(string alertId, CancellationToken cancellationToken = default);
}

public class FunctionAppService : IFunctionAppService
{
    private readonly HttpClient _httpClient;
    private readonly FunctionAppOptions _options;
    private readonly ILogger<FunctionAppService> _logger;

    public FunctionAppService(
        HttpClient httpClient,
        IOptions<FunctionAppOptions> options,
        ILogger<FunctionAppService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> EscalateAlertAsync(string alertId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            _logger.LogWarning("FunctionApp BaseUrl is not configured");
            return false;
        }

        try
        {
            var url = $"{_options.BaseUrl.TrimEnd('/')}/api/alerts/{alertId}/escalate";

            using var request = new HttpRequestMessage(HttpMethod.Post, url);

            if (!string.IsNullOrWhiteSpace(_options.HostKey))
            {
                request.Headers.Add("x-functions-key", _options.HostKey);
            }

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully escalated alert {AlertId} via Function App", alertId);
                return true;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Failed to escalate alert {AlertId}. Status: {StatusCode}, Response: {Response}",
                alertId, response.StatusCode, content);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while escalating alert {AlertId}", alertId);
            return false;
        }
    }
}
