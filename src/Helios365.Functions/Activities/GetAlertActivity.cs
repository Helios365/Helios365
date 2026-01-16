using Helios365.Core.Models;
using Helios365.Core.Repositories;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Helios365.Functions.Activities;

public class GetAlertActivity
{
    private readonly IAlertRepository _alertRepository;
    private readonly ILogger<GetAlertActivity> _logger;

    public GetAlertActivity(
        IAlertRepository alertRepository,
        ILogger<GetAlertActivity> logger)
    {
        _alertRepository = alertRepository;
        _logger = logger;
    }

    [Function(nameof(GetAlertActivity))]
    public async Task<Alert?> Run(
        [ActivityTrigger] string alertId)
    {
        _logger.LogDebug("Fetching alert {AlertId}", alertId);

        return await _alertRepository.GetAsync(alertId);
    }
}
