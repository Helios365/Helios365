using Helios365.Core.Models;
using Helios365.Core.Repositories;
using Helios365.Core.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Helios365.Functions.Activities;

public class AlertActivities
{
    private readonly IAlertRepository _alertRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IResourceRepository _resourceRepository;
    private readonly IServicePrincipalRepository _servicePrincipalRepository;
    private readonly IActionRepository _actionRepository;
    private readonly IActionExecutor _actionExecutor;
    private readonly IEmailService _emailService;
    private readonly ILogger<AlertActivities> _logger;

    public AlertActivities(
        IAlertRepository alertRepository,
        ICustomerRepository customerRepository,
        IResourceRepository resourceRepository,
        IServicePrincipalRepository servicePrincipalRepository,
        IActionRepository actionRepository,
        IActionExecutor actionExecutor,
        IEmailService emailService,
        ILogger<AlertActivities> logger)
    {
        _alertRepository = alertRepository;
        _customerRepository = customerRepository;
        _resourceRepository = resourceRepository;
        _servicePrincipalRepository = servicePrincipalRepository;
        _actionRepository = actionRepository;
        _actionExecutor = actionExecutor;
        _emailService = emailService;
        _logger = logger;
    }

    [Function(nameof(UpdateAlertActivity))]
    public async Task UpdateAlertActivity([ActivityTrigger] Alert alert)
    {
        _logger.LogInformation("Updating alert {AlertId} to status {Status}", alert.Id, alert.Status);
        await _alertRepository.UpdateAsync(alert.Id, alert);
    }

    [Function(nameof(LoadResourceActivity))]
    public async Task<Resource?> LoadResourceActivity([ActivityTrigger] (string customerId, string resourceId) input)
    {
        var (customerId, resourceId) = input;
        _logger.LogInformation("Loading resource {ResourceId} for customer {CustomerId}", resourceId, customerId);
        return await _resourceRepository.GetByResourceIdAsync(customerId, resourceId);
    }

    [Function(nameof(LoadCustomerActivity))]
    public async Task<Customer?> LoadCustomerActivity([ActivityTrigger] string customerId)
    {
        _logger.LogInformation("Loading customer {CustomerId}", customerId);
        return await _customerRepository.GetAsync(customerId);
    }

    [Function(nameof(LoadServicePrincipalActivity))]
    public async Task<ServicePrincipal?> LoadServicePrincipalActivity([ActivityTrigger] string servicePrincipalId)
    {
        _logger.LogInformation("Loading service principal {ServicePrincipalId}", servicePrincipalId);
        return await _servicePrincipalRepository.GetAsync(servicePrincipalId);
    }

    [Function(nameof(LoadActionsActivity))]
    public async Task<List<ActionBase>> LoadActionsActivity([ActivityTrigger] (string customerId, string? resourceId) input)
    {
        var (customerId, resourceId) = input;
        _logger.LogInformation("Loading automatic actions for customer {CustomerId}, resource {ResourceId}", 
            customerId, resourceId ?? "default");

        var actions = await _actionRepository.ListAutomaticActionsAsync(customerId, resourceId);
        return actions.OrderBy(a => a.Order).ToList();
    }

    [Function(nameof(ExecuteActionActivity))]
    public async Task<bool> ExecuteActionActivity(
        [ActivityTrigger] (ActionBase action, Resource resource, ServicePrincipal servicePrincipal) input)
    {
        var (action, resource, servicePrincipal) = input;
        
        _logger.LogInformation("Executing action {ActionId} ({ActionType}) for resource {ResourceId}",
            action.Id, action.Type, resource.ResourceId);

        try
        {
            var result = await _actionExecutor.ExecuteAsync(action, resource, servicePrincipal);
            
            if (result)
            {
                _logger.LogInformation("Action {ActionId} executed successfully", action.Id);
            }
            else
            {
                _logger.LogWarning("Action {ActionId} executed but returned failure", action.Id);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing action {ActionId}", action.Id);
            return false;
        }
    }

    [Function(nameof(SendEscalationEmailActivity))]
    public async Task SendEscalationEmailActivity([ActivityTrigger] string alertId)
    {
        _logger.LogInformation("Sending escalation email for alert {AlertId}", alertId);

        try
        {
            // Load alert
            var alert = await _alertRepository.GetAsync(alertId);
            if (alert == null)
            {
                _logger.LogWarning("Alert {AlertId} not found for escalation email", alertId);
                return;
            }

            // Load customer
            var customer = await _customerRepository.GetAsync(alert.CustomerId);
            if (customer == null)
            {
                _logger.LogWarning("Customer {CustomerId} not found for escalation email", alert.CustomerId);
                return;
            }

            // Try to load resource (may be null)
            Resource? resource = null;
            try
            {
                resource = await _resourceRepository.GetByResourceIdAsync(alert.CustomerId, alert.ResourceId);
            }
            catch
            {
                // Resource not found is OK for escalation emails
            }

            // Load attempted actions (those with same alert context)
            // For now, just pass empty list - in production you'd track this
            var attemptedActions = new List<ActionBase>();

            await _emailService.SendEscalationEmailAsync(alert, resource, customer, attemptedActions);
            
            _logger.LogInformation("Escalation email sent successfully for alert {AlertId}", alertId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending escalation email for alert {AlertId}", alertId);
            // Don't throw - we don't want email failures to stop the orchestration
        }
    }
}
