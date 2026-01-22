using Helios365.Core.Contracts;
using Helios365.Core.Models;
using Helios365.Functions.Activities;
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
            // Step 1: Get current on-call coverage
            var onCallResult = await context.CallActivityAsync<GetOnCallUsersResult>(
                nameof(GetOnCallUsersActivity),
                new GetOnCallUsersInput(alert.CustomerId, context.CurrentUtcDateTime));

            if (onCallResult.PrimaryUsers.Count == 0 && onCallResult.BackupUsers.Count == 0)
            {
                logger.LogWarning(
                    "No on-call users found for customer {CustomerId}. Alert {AlertId} marked as escalated but no notifications sent.",
                    alert.CustomerId, alert.Id);

                alert.MarkStatus(AlertStatus.Escalated);
                alert.Changes.Add(new AlertChange
                {
                    Id = context.NewGuid().ToString(),
                    User = "system",
                    Comment = "No on-call users configured - unable to send notifications",
                    PreviousStatus = AlertStatus.Pending,
                    NewStatus = AlertStatus.Escalated,
                    CreatedAt = context.CurrentUtcDateTime
                });
                await context.CallActivityAsync<Alert>(nameof(UpdateAlertActivity), alert);
                return;
            }

            // Step 2: Get escalation policy (use defaults if not found)
            var policy = onCallResult.EscalationPolicy ?? new EscalationPolicyInfo(
                TimeSpan.FromMinutes(5),
                3,
                TimeSpan.FromMinutes(5));

            logger.LogInformation(
                "Starting notification for alert {AlertId} - Primary users: {PrimaryCount}, Backup users: {BackupCount}",
                alert.Id, onCallResult.PrimaryUsers.Count, onCallResult.BackupUsers.Count);

            // Step 3: Start escalation sub-orchestration (alert stays Pending until escalated to backup)
            await context.CallSubOrchestratorAsync(
                nameof(EscalationOrchestrator),
                new EscalationOrchestratorInput(
                    alert,
                    onCallResult.PrimaryUsers.ToList(),
                    onCallResult.BackupUsers.ToList(),
                    policy));

            logger.LogInformation(
                "Escalation orchestration completed for alert {AlertId}",
                alert.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in orchestration for alert {AlertId}", alert.Id);
            alert.MarkStatus(AlertStatus.Failed);
            alert.Changes.Add(new AlertChange
            {
                Id = context.NewGuid().ToString(),
                User = "system",
                Comment = $"Orchestration failed: {ex.Message}",
                PreviousStatus = alert.Status,
                NewStatus = AlertStatus.Failed,
                CreatedAt = context.CurrentUtcDateTime
            });
            await context.CallActivityAsync<Alert>(nameof(UpdateAlertActivity), alert);
        }
    }
}
