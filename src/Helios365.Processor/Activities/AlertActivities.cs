using Helios365.Core.Models;
using Helios365.Core.Repositories;
using Helios365.Core.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Helios365.Processor.Activities;

public class AlertActivities
{
    private readonly ILogger<AlertActivities> _logger;
    private readonly IAlertRepository _alertRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IHealthCheckService _healthCheckService;

    public AlertActivities(
        ILogger<AlertActivities> logger,
        IAlertRepository alertRepository,
        ICustomerRepository customerRepository,
        IHealthCheckService healthCheckService)
    {
        _logger = logger;
        _alertRepository = alertRepository;
        _customerRepository = customerRepository;
        _healthCheckService = healthCheckService;
    }

    [Function(nameof(UpdateAlertActivity))]
    public async Task UpdateAlertActivity([ActivityTrigger] Alert alert)
    {
        _logger.LogInformation("Updating alert {AlertId} to status {Status}", alert.Id, alert.Status);
        await _alertRepository.UpdateAsync(alert.Id, alert);
    }

    [Function(nameof(LoadCustomerActivity))]
    public async Task<Customer?> LoadCustomerActivity([ActivityTrigger] string customerId)
    {
        _logger.LogInformation("Loading customer {CustomerId}", customerId);
        return await _customerRepository.GetAsync(customerId);
    }

    [Function(nameof(HealthCheckActivity))]
    public async Task<bool> HealthCheckActivity([ActivityTrigger] Alert alert)
    {
        _logger.LogInformation("Performing health check for alert {AlertId}", alert.Id);

        if (string.IsNullOrEmpty(alert.HealthCheckUrl))
        {
            _logger.LogWarning("No health check URL configured for alert {AlertId}", alert.Id);
            return false;
        }

        try
        {
            var config = new HealthCheckConfig
            {
                Type = HealthCheckType.HttpGet,
                Endpoint = alert.HealthCheckUrl,
                ExpectedStatusCode = 200,
                TimeoutSeconds = 30
            };

            return await _healthCheckService.CheckAsync(config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed for alert {AlertId}", alert.Id);
            return false;
        }
    }

    [Function(nameof(RemediationActivity))]
    public async Task<bool> RemediationActivity(
        [ActivityTrigger] (Alert alert, Customer customer) input)
    {
        var (alert, customer) = input;
        _logger.LogInformation("Triggering remediation for alert {AlertId}", alert.Id);

        // TODO: Implement remediation logic
        // This would call the customer's Agent function to restart/scale the resource
        // For now, just log and return success
        
        if (string.IsNullOrEmpty(customer.Config.Helios365Endpoint))
        {
            _logger.LogWarning("No Helios365 endpoint configured for customer {CustomerId}", customer.Id);
            return false;
        }

        try
        {
            // Example: POST to customer's Agent function
            // var httpClient = new HttpClient();
            // var response = await httpClient.PostAsJsonAsync(
            //     $"{customer.Config.Helios365Endpoint}/api/remediate",
            //     new { ResourceId = alert.ResourceId, Action = "restart" });
            // return response.IsSuccessStatusCode;

            _logger.LogInformation("Remediation triggered successfully for alert {AlertId}", alert.Id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Remediation failed for alert {AlertId}", alert.Id);
            return false;
        }
    }

    [Function(nameof(SendNotificationActivity))]
    public async Task SendNotificationActivity(
        [ActivityTrigger] (Alert alert, Customer customer) input)
    {
        var (alert, customer) = input;
        _logger.LogInformation("Sending notification for alert {AlertId}", alert.Id);

        // TODO: Implement notification logic
        // This would send email/Slack/Teams notifications
        // For now, just log
        
        foreach (var email in customer.Config.NotificationEmails)
        {
            _logger.LogInformation("Would send notification to {Email} for alert {AlertId}", email, alert.Id);
        }

        await Task.CompletedTask;
    }
}
