using Helios365.Core.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;

namespace Helios365.Processor.Orchestrators;

public class AlertOrchestrator
{
    [Function(nameof(AlertOrchestrator))]
    public async Task RunOrchestrator(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var logger = context.CreateReplaySafeLogger<AlertOrchestrator>();
        var alert = context.GetInput<Alert>();

        if (alert == null)
        {
            logger.LogError("Alert is null");
            return;
        }

        logger.LogInformation("Starting orchestration for alert {AlertId}", alert.Id);

        try
        {
            // Step 1: Update alert status to Routing
            alert.Status = AlertStatus.Routing;
            await context.CallActivityAsync("UpdateAlertActivity", alert);

            // Step 2: Load customer configuration
            var customer = await context.CallActivityAsync<Customer?>("LoadCustomerActivity", alert.CustomerId);
            if (customer == null || !customer.Active)
            {
                logger.LogWarning("Customer {CustomerId} not found or inactive", alert.CustomerId);
                alert.Status = AlertStatus.Failed;
                await context.CallActivityAsync("UpdateAlertActivity", alert);
                return;
            }

            // Step 3: Perform health check
            alert.Status = AlertStatus.Checking;
            await context.CallActivityAsync("UpdateAlertActivity", alert);

            var isHealthy = await context.CallActivityAsync<bool>("HealthCheckActivity", alert);

            if (isHealthy)
            {
                // Alert is false positive - mark as resolved
                logger.LogInformation("Health check passed for alert {AlertId}", alert.Id);
                alert.Status = AlertStatus.Healthy;
                await context.CallActivityAsync("UpdateAlertActivity", alert);
                return;
            }

            // Step 4: Attempt remediation if enabled
            if (!customer.Config.AutoRemediationEnabled)
            {
                // Skip remediation, go straight to escalation
                logger.LogInformation("Auto-remediation disabled for customer {CustomerId}", customer.Id);
                alert.Status = AlertStatus.Escalated;
                await context.CallActivityAsync("UpdateAlertActivity", alert);
                await context.CallActivityAsync("SendNotificationActivity", (alert, customer));
                return;
            }

            // Trigger remediation
            alert.Status = AlertStatus.Remediating;
            await context.CallActivityAsync("UpdateAlertActivity", alert);

            var remediationSuccess = await context.CallActivityAsync<bool>("RemediationActivity", (alert, customer));

            if (!remediationSuccess)
            {
                logger.LogWarning("Remediation failed for alert {AlertId}", alert.Id);
                alert.Status = AlertStatus.Escalated;
                await context.CallActivityAsync("UpdateAlertActivity", alert);
                await context.CallActivityAsync("SendNotificationActivity", (alert, customer));
                return;
            }

            // Step 5: Wait before rechecking
            var waitTime = TimeSpan.FromMinutes(customer.Config.EscalationTimeoutMinutes);
            await context.CreateTimer(context.CurrentUtcDateTime.Add(waitTime), CancellationToken.None);

            // Step 6: Recheck health
            alert.Status = AlertStatus.Rechecking;
            await context.CallActivityAsync("UpdateAlertActivity", alert);

            isHealthy = await context.CallActivityAsync<bool>("HealthCheckActivity", alert);

            if (isHealthy)
            {
                // Remediation successful!
                logger.LogInformation("Remediation successful for alert {AlertId}", alert.Id);
                alert.Status = AlertStatus.Resolved;
                await context.CallActivityAsync("UpdateAlertActivity", alert);
                return;
            }

            // Step 7: Escalate to on-call
            logger.LogWarning("Remediation did not resolve issue for alert {AlertId}", alert.Id);
            alert.Status = AlertStatus.Escalated;
            await context.CallActivityAsync("UpdateAlertActivity", alert);
            await context.CallActivityAsync("SendNotificationActivity", (alert, customer));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in orchestration for alert {AlertId}", alert.Id);
            alert.Status = AlertStatus.Failed;
            await context.CallActivityAsync("UpdateAlertActivity", alert);
        }
    }
}
