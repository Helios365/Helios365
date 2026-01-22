using Helios365.Core.Contracts;
using Helios365.Core.Models;
using Helios365.Functions.Activities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;

namespace Helios365.Functions.Orchestrators;

public record EscalationOrchestratorInput(
    Alert Alert,
    List<OnCallUserInfo> PrimaryUsers,
    List<OnCallUserInfo> BackupUsers,
    EscalationPolicyInfo Policy);

public class EscalationOrchestrator
{
    [Function(nameof(EscalationOrchestrator))]
    public async Task RunOrchestrator(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var logger = context.CreateReplaySafeLogger<EscalationOrchestrator>();
        var input = context.GetInput<EscalationOrchestratorInput>();

        if (input == null)
        {
            logger.LogError("Escalation input is null");
            return;
        }

        var alertId = input.Alert.Id;
        var policy = input.Policy;

        if (input.PrimaryUsers.Count == 0 && input.BackupUsers.Count == 0)
        {
            logger.LogWarning("No on-call users available for alert {AlertId}", alertId);
            return;
        }

        logger.LogInformation(
            "Starting notifications for alert {AlertId} - Primary: {PrimaryCount}, Backup: {BackupCount}",
            alertId, input.PrimaryUsers.Count, input.BackupUsers.Count);

        int attempt = 0;

        // Notify primary users
        foreach (var user in input.PrimaryUsers)
        {
            if (await IsAlertHandledAsync(context, alertId, logger))
                return;

            attempt++;
            await NotifyUserAsync(context, alertId, user, attempt, isBackup: false, policy.AckTimeout, logger);
        }

        // Check if alert was handled by primary
        if (await IsAlertHandledAsync(context, alertId, logger))
        {
            logger.LogInformation("Alert {AlertId} handled by primary. No escalation needed.", alertId);
            return;
        }

        // Escalate to backup
        if (input.BackupUsers.Count > 0)
        {
            logger.LogInformation("Primary users did not respond. Escalating alert {AlertId} to backup.", alertId);

            await context.CallActivityAsync<Alert>(
                nameof(AlertServiceActivities.MarkEscalatedActivity),
                new MarkEscalatedInput(alertId, "Primary on-call did not respond. Escalating to backup."));

            foreach (var user in input.BackupUsers)
            {
                if (await IsAlertHandledAsync(context, alertId, logger))
                    return;

                attempt++;
                await NotifyUserAsync(context, alertId, user, attempt, isBackup: true, policy.AckTimeout, logger);
            }
        }

        // All notifications exhausted
        if (!await IsAlertHandledAsync(context, alertId, logger))
        {
            logger.LogWarning("All {Attempts} notification attempts exhausted for alert {AlertId}", attempt, alertId);

            await context.CallActivityAsync<Alert>(
                nameof(AlertServiceActivities.AddTimelineEntryActivity),
                new AddTimelineEntryInput(alertId, $"All {attempt} notification attempts completed without response"));
        }
    }

    private static async Task<bool> IsAlertHandledAsync(
        TaskOrchestrationContext context,
        string alertId,
        ILogger logger)
    {
        var alert = await context.CallActivityAsync<Alert?>(nameof(GetAlertActivity), alertId);

        if (alert == null)
        {
            logger.LogWarning("Alert {AlertId} no longer exists. Stopping.", alertId);
            return true;
        }

        if (alert.Status == AlertStatus.Resolved || alert.Status == AlertStatus.Accepted)
        {
            logger.LogInformation("Alert {AlertId} is {Status}. Stopping.", alertId, alert.Status);
            return true;
        }

        return false;
    }

    private static async Task NotifyUserAsync(
        TaskOrchestrationContext context,
        string alertId,
        OnCallUserInfo user,
        int attempt,
        bool isBackup,
        TimeSpan ackTimeout,
        ILogger logger)
    {
        var label = isBackup ? "backup" : "primary";
        logger.LogInformation(
            "Notifying {Label} user {UserName} for alert {AlertId}",
            label, user.DisplayName, alertId);

        // Update escalation state
        await context.CallActivityAsync<Alert>(
            nameof(AlertServiceActivities.UpdateEscalationStateActivity),
            new UpdateEscalationStateInput(alertId, attempt, user.UserId));

        // Get fresh alert for notification
        var alert = await context.CallActivityAsync<Alert?>(nameof(GetAlertActivity), alertId);
        if (alert == null) return;

        // Send notification
        var notifyResult = await context.CallActivityAsync<SendNotificationResult>(
            nameof(SendNotificationActivity),
            new SendNotificationInput(
                alertId,
                alert.CustomerId,
                user.UserId,
                user.DisplayName,
                user.Email,
                user.Phone,
                alert.Title ?? "Alert",
                alert.Description,
                alert.Severity.ToString(),
                alert.ResourceId));

        // Record result in timeline
        await context.CallActivityAsync<Alert>(
            nameof(AlertServiceActivities.RecordNotificationResultActivity),
            new RecordNotificationResultInput(
                alertId,
                user.DisplayName,
                notifyResult.EmailSent,
                notifyResult.SmsSent,
                notifyResult.ErrorMessage,
                isBackup));

        // Wait for acknowledgment if notification was sent
        if (notifyResult.EmailSent || notifyResult.SmsSent)
        {
            var deadline = context.CurrentUtcDateTime.Add(ackTimeout);
            await context.CreateTimer(deadline, CancellationToken.None);
        }
    }
}
