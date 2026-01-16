using Helios365.Core.Models;
using Helios365.Core.Repositories;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Helios365.Functions.Activities;

public class UpdateAlertActivity
{
    private readonly IAlertRepository _alertRepository;
    private readonly ILogger<UpdateAlertActivity> _logger;

    public UpdateAlertActivity(
        IAlertRepository alertRepository,
        ILogger<UpdateAlertActivity> logger)
    {
        _alertRepository = alertRepository;
        _logger = logger;
    }

    [Function(nameof(UpdateAlertActivity))]
    public async Task<Alert> Run(
        [ActivityTrigger] Alert alert)
    {
        _logger.LogInformation(
            "Updating alert {AlertId} - Status: {Status}, EscalationAttempts: {Attempts}",
            alert.Id, alert.Status, alert.EscalationAttempts);

        return await _alertRepository.UpdateAsync(alert.Id, alert);
    }
}
