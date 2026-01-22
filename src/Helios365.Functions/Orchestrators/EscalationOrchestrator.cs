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

        var alert = input.Alert;
        var policy = input.Policy;
        var primaryUsers = input.PrimaryUsers;
        var backupUsers = input.BackupUsers;

        if (primaryUsers.Count == 0 && backupUsers.Count == 0)
        {
            logger.LogWarning(
                "No on-call users available for alert {AlertId}",
                alert.Id);
            return;
        }

        logger.LogInformation(
            "Starting notifications for alert {AlertId} - Primary: {PrimaryCount}, Backup: {BackupCount}, AckTimeout={AckTimeout}",
            alert.Id, primaryUsers.Count, backupUsers.Count, policy.AckTimeout);

        int attempt = 0;

        // First, notify primary users
        foreach (var currentUser in primaryUsers)
        {
            attempt++;

            // Check if alert is still active before notifying
            var currentAlert = await context.CallActivityAsync<Alert?>(
                nameof(GetAlertActivity), alert.Id);

            if (currentAlert == null)
            {
                logger.LogWarning("Alert {AlertId} no longer exists. Stopping.", alert.Id);
                return;
            }

            if (currentAlert.Status == AlertStatus.Resolved ||
                currentAlert.Status == AlertStatus.Accepted)
            {
                logger.LogInformation(
                    "Alert {AlertId} is no longer active (status: {Status}). Stopping.",
                    alert.Id, currentAlert.Status);
                return;
            }

            alert = currentAlert;

            logger.LogInformation(
                "Notifying primary user {UserName} ({UserId}) for alert {AlertId}",
                currentUser.DisplayName, currentUser.UserId, alert.Id);

            alert.EscalationAttempts = attempt;
            alert.CurrentEscalationTarget = currentUser.UserId;
            alert = await context.CallActivityAsync<Alert>(nameof(UpdateAlertActivity), alert);

            var notifyResult = await NotifyUserAsync(context, alert, currentUser);

            // Add timeline entry for notification attempt
            var channels = new List<string>();
            if (notifyResult.EmailSent) channels.Add("email");
            if (notifyResult.SmsSent) channels.Add("SMS");

            if (channels.Count > 0)
            {
                alert.Changes.Add(new AlertChange
                {
                    Id = context.NewGuid().ToString(),
                    User = "system",
                    Comment = $"Notification sent to {currentUser.DisplayName} via {string.Join(" and ", channels)}",
                    CreatedAt = context.CurrentUtcDateTime
                });
            }
            else
            {
                alert.Changes.Add(new AlertChange
                {
                    Id = context.NewGuid().ToString(),
                    User = "system",
                    Comment = $"Failed to notify {currentUser.DisplayName}: {notifyResult.ErrorMessage}",
                    CreatedAt = context.CurrentUtcDateTime
                });
            }

            alert = await context.CallActivityAsync<Alert>(nameof(UpdateAlertActivity), alert);

            if (!notifyResult.EmailSent && !notifyResult.SmsSent)
            {
                logger.LogWarning(
                    "Failed to send notification for alert {AlertId} to {UserName}: {Error}",
                    alert.Id, currentUser.DisplayName, notifyResult.ErrorMessage);
                continue;
            }

            // Wait for acknowledgment
            logger.LogInformation(
                "Waiting {Timeout} for response from {UserName} for alert {AlertId}",
                policy.AckTimeout, currentUser.DisplayName, alert.Id);

            var deadline = context.CurrentUtcDateTime.Add(policy.AckTimeout);
            await context.CreateTimer(deadline, CancellationToken.None);
        }

        // Check if alert was accepted during primary notifications
        var alertAfterPrimary = await context.CallActivityAsync<Alert?>(nameof(GetAlertActivity), alert.Id);
        if (alertAfterPrimary == null ||
            alertAfterPrimary.Status == AlertStatus.Resolved ||
            alertAfterPrimary.Status == AlertStatus.Accepted)
        {
            logger.LogInformation("Alert {AlertId} handled by primary. No escalation needed.", alert.Id);
            return;
        }

        alert = alertAfterPrimary;

        // Escalate to backup users
        if (backupUsers.Count > 0)
        {
            logger.LogInformation(
                "Primary users did not respond. Escalating alert {AlertId} to backup users.",
                alert.Id);

            // Mark as Escalated when moving to backup
            alert.MarkStatus(AlertStatus.Escalated);
            alert.Changes.Add(new AlertChange
            {
                Id = context.NewGuid().ToString(),
                User = "system",
                Comment = "Primary on-call did not respond. Escalating to backup.",
                PreviousStatus = AlertStatus.Pending,
                NewStatus = AlertStatus.Escalated,
                CreatedAt = context.CurrentUtcDateTime
            });
            alert = await context.CallActivityAsync<Alert>(nameof(UpdateAlertActivity), alert);

            foreach (var currentUser in backupUsers)
            {
                attempt++;

                var currentAlert = await context.CallActivityAsync<Alert?>(
                    nameof(GetAlertActivity), alert.Id);

                if (currentAlert == null)
                {
                    logger.LogWarning("Alert {AlertId} no longer exists. Stopping.", alert.Id);
                    return;
                }

                if (currentAlert.Status == AlertStatus.Resolved ||
                    currentAlert.Status == AlertStatus.Accepted)
                {
                    logger.LogInformation(
                        "Alert {AlertId} is no longer active (status: {Status}). Stopping.",
                        alert.Id, currentAlert.Status);
                    return;
                }

                alert = currentAlert;

                logger.LogInformation(
                    "Notifying backup user {UserName} ({UserId}) for alert {AlertId}",
                    currentUser.DisplayName, currentUser.UserId, alert.Id);

                alert.EscalationAttempts = attempt;
                alert.CurrentEscalationTarget = currentUser.UserId;
                alert = await context.CallActivityAsync<Alert>(nameof(UpdateAlertActivity), alert);

                var notifyResult = await NotifyUserAsync(context, alert, currentUser);

                // Add timeline entry for notification attempt
                var channels = new List<string>();
                if (notifyResult.EmailSent) channels.Add("email");
                if (notifyResult.SmsSent) channels.Add("SMS");

                if (channels.Count > 0)
                {
                    alert.Changes.Add(new AlertChange
                    {
                        Id = context.NewGuid().ToString(),
                        User = "system",
                        Comment = $"Notification sent to {currentUser.DisplayName} (backup) via {string.Join(" and ", channels)}",
                        CreatedAt = context.CurrentUtcDateTime
                    });
                }
                else
                {
                    alert.Changes.Add(new AlertChange
                    {
                        Id = context.NewGuid().ToString(),
                        User = "system",
                        Comment = $"Failed to notify {currentUser.DisplayName} (backup): {notifyResult.ErrorMessage}",
                        CreatedAt = context.CurrentUtcDateTime
                    });
                }

                alert = await context.CallActivityAsync<Alert>(nameof(UpdateAlertActivity), alert);

                if (!notifyResult.EmailSent && !notifyResult.SmsSent)
                {
                    logger.LogWarning(
                        "Failed to send notification for alert {AlertId} to {UserName}: {Error}",
                        alert.Id, currentUser.DisplayName, notifyResult.ErrorMessage);
                    continue;
                }

                // Wait for acknowledgment
                var deadline = context.CurrentUtcDateTime.Add(policy.AckTimeout);
                await context.CreateTimer(deadline, CancellationToken.None);
            }
        }

        // All notifications exhausted
        logger.LogWarning("All notification attempts exhausted for alert {AlertId}", alert.Id);

        var finalAlert = await context.CallActivityAsync<Alert?>(nameof(GetAlertActivity), alert.Id);
        if (finalAlert != null &&
            finalAlert.Status != AlertStatus.Resolved &&
            finalAlert.Status != AlertStatus.Accepted)
        {
            finalAlert.Changes.Add(new AlertChange
            {
                Id = context.NewGuid().ToString(),
                User = "system",
                Comment = $"All {attempt} notification attempts completed without response",
                PreviousStatus = finalAlert.Status,
                NewStatus = finalAlert.Status,
                CreatedAt = context.CurrentUtcDateTime
            });
            await context.CallActivityAsync<Alert>(nameof(UpdateAlertActivity), finalAlert);
        }
    }

    private static async Task<SendNotificationResult> NotifyUserAsync(
        TaskOrchestrationContext context,
        Alert alert,
        OnCallUserInfo user)
    {
        var notifyInput = new SendNotificationInput(
            alert.Id,
            alert.CustomerId,
            user.UserId,
            user.DisplayName,
            user.Email,
            user.Phone,
            alert.Title ?? "Alert",
            alert.Description,
            alert.Severity.ToString(),
            alert.ResourceId);

        return await context.CallActivityAsync<SendNotificationResult>(
            nameof(SendNotificationActivity), notifyInput);
    }
}
