using Helios365.Core.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;

namespace Helios365.Functions.Orchestrators;

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
            alert.MarkStatus(AlertStatus.Routing);
            // await context.CallActivityAsync("UpdateAlertActivity", alert);

            // // Step 2: Load resource
            // var resource = await context.CallActivityAsync<Resource?>("LoadResourceActivity", 
            //     (alert.CustomerId, alert.ResourceId));

            // if (resource == null)
            // {
            //     logger.LogWarning("Resource not found for alert {AlertId}", alert.Id);
            //     alert.MarkStatus(AlertStatus.Escalated);
            //     await context.CallActivityAsync("UpdateAlertActivity", alert);
            //     await context.CallActivityAsync("SendEscalationEmailActivity", alert.Id);
            //     return;
            // }

            // // Step 3: Load customer
            // var customer = await context.CallActivityAsync<Customer?>("LoadCustomerActivity", alert.CustomerId);
            // if (customer == null || !customer.Active)
            // {
            //     logger.LogWarning("Customer {CustomerId} not found or inactive", alert.CustomerId);
            //     alert.MarkStatus(AlertStatus.Failed);
            //     await context.CallActivityAsync("UpdateAlertActivity", alert);
            //     return;
            // }

            // // Step 4: Load service principal
            // var servicePrincipal = await context.CallActivityAsync<ServicePrincipal?>(
            //     "LoadServicePrincipalActivity", resource.ServicePrincipalId);

            // if (servicePrincipal == null || !servicePrincipal.Active)
            // {
            //     logger.LogWarning("Service principal {ServicePrincipalId} not found or inactive", 
            //         resource.ServicePrincipalId);
            //     alert.MarkStatus(AlertStatus.Escalated);
            //     await context.CallActivityAsync("UpdateAlertActivity", alert);
            //     await context.CallActivityAsync("SendEscalationEmailActivity", alert.Id);
            //     return;
            // }

            // // Step 5: Load actions
            // var actions = await context.CallActivityAsync<List<ActionBase>>(
            //     "LoadActionsActivity", 
            //     (alert.CustomerId, resource.Id));

            // if (!actions.Any())
            // {
            //     logger.LogInformation("No automatic actions configured for alert {AlertId}. Escalating.", alert.Id);
            //     alert.MarkStatus(AlertStatus.Escalated);
            //     await context.CallActivityAsync("UpdateAlertActivity", alert);
            //     await context.CallActivityAsync("SendEscalationEmailActivity", alert.Id);
            //     return;
            // }

            // logger.LogInformation("Found {Count} automatic actions for alert {AlertId}", actions.Count, alert.Id);

            // // Step 6: Execute actions in order
            // alert.MarkStatus(AlertStatus.Checking);
            // await context.CallActivityAsync("UpdateAlertActivity", alert);

            // var attemptedActions = new List<ActionBase>();

            // foreach (var action in actions.OrderBy(a => a.Order))
            // {
            //     logger.LogInformation("Executing action {ActionId} ({ActionType}) for alert {AlertId}", 
            //         action.Id, action.Type, alert.Id);

            //     attemptedActions.Add(action);

            //     var actionResult = await context.CallActivityAsync<bool>(
            //         "ExecuteActionActivity",
            //         (action, resource, servicePrincipal));

            //     // If it's a health check and it passes, we're done!
            //     if (action is HealthCheckAction && actionResult)
            //     {
            //         logger.LogInformation("Health check passed for alert {AlertId}. Marking as healthy.", alert.Id);
            //         alert.MarkStatus(AlertStatus.Healthy);
            //         await context.CallActivityAsync("UpdateAlertActivity", alert);
            //         return;
            //     }

            //     // If it's a restart action
            //     if (action is RestartAction restartAction)
            //     {
            //         if (actionResult)
            //         {
            //             logger.LogInformation("Restart completed for alert {AlertId}. Waiting {Seconds} seconds before recheck.", 
            //                 alert.Id, restartAction.WaitAfterSeconds);

            //             alert.MarkStatus(AlertStatus.Remediating);
            //             await context.CallActivityAsync("UpdateAlertActivity", alert);

            //             // Wait configured time (or default 5 minutes)
            //             var waitTime = restartAction.WaitAfterSeconds > 0 
            //                 ? TimeSpan.FromSeconds(restartAction.WaitAfterSeconds)
            //                 : TimeSpan.FromMinutes(customer.EscalationTimeoutMinutes);

            //             await context.CreateTimer(context.CurrentUtcDateTime.Add(waitTime), CancellationToken.None);

            //             // Update status to rechecking
            //             alert.MarkStatus(AlertStatus.Rechecking);
            //             await context.CallActivityAsync("UpdateAlertActivity", alert);
            //         }
            //         else
            //         {
            //             logger.LogWarning("Restart failed for alert {AlertId}", alert.Id);
            //         }
            //     }
            // }

            // // Step 7: After all actions, check if there's a final health check
            // var finalHealthCheck = attemptedActions.OfType<HealthCheckAction>().LastOrDefault();
            // if (finalHealthCheck != null)
            // {
            //     var finalResult = await context.CallActivityAsync<bool>(
            //         "ExecuteActionActivity",
            //         (finalHealthCheck, resource, servicePrincipal));

            //     if (finalResult)
            //     {
            //         logger.LogInformation("Final health check passed for alert {AlertId}. Marking as resolved.", alert.Id);
            //         alert.MarkStatus(AlertStatus.Resolved);
            //         await context.CallActivityAsync("UpdateAlertActivity", alert);
            //         return;
            //     }
            // }

            // Step 8: All actions exhausted, escalate
            logger.LogWarning("All actions exhausted for alert {AlertId}. Escalating to on-call.", alert.Id);
            alert.MarkStatus(AlertStatus.Escalated);
            // await context.CallActivityAsync("UpdateAlertActivity", alert);
            // await context.CallActivityAsync("SendEscalationEmailActivity", alert.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in orchestration for alert {AlertId}", alert.Id);
            alert.MarkStatus(AlertStatus.Failed);
            await context.CallActivityAsync("UpdateAlertActivity", alert);
        }
    }
}
