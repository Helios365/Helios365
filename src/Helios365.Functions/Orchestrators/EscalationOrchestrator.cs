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
        var allUsers = BuildEscalationOrder(input.PrimaryUsers, input.BackupUsers);

        if (allUsers.Count == 0)
        {
            logger.LogWarning(
                "No on-call users available for escalation of alert {AlertId}",
                alert.Id);
            return;
        }

        logger.LogInformation(
            "Starting escalation for alert {AlertId} with {UserCount} users, MaxRetries={MaxRetries}, AckTimeout={AckTimeout}",
            alert.Id, allUsers.Count, policy.MaxRetries, policy.AckTimeout);

        int attempt = 0;
        int userIndex = 0;

        while (attempt < policy.MaxRetries)
        {
            attempt++;

            // Check if alert is still active before notifying
            var currentAlert = await context.CallActivityAsync<Alert?>(
                nameof(GetAlertActivity), alert.Id);

            if (currentAlert == null)
            {
                logger.LogWarning(
                    "Alert {AlertId} no longer exists. Stopping escalation.",
                    alert.Id);
                return;
            }

            if (currentAlert.Status == AlertStatus.Resolved ||
                currentAlert.Status == AlertStatus.Acknowledged)
            {
                logger.LogInformation(
                    "Alert {AlertId} is no longer active (status: {Status}). Stopping escalation.",
                    alert.Id, currentAlert.Status);
                return;
            }

            // Update local alert reference with latest state
            alert = currentAlert;

            // Get current user to notify (cycle through users)
            var currentUser = allUsers[userIndex % allUsers.Count];
            userIndex++;

            logger.LogInformation(
                "Escalation attempt {Attempt}/{MaxRetries} for alert {AlertId}, notifying {UserName} ({UserId})",
                attempt, policy.MaxRetries, alert.Id, currentUser.DisplayName, currentUser.UserId);

            // Update alert with current escalation state
            alert.EscalationAttempts = attempt;
            alert.CurrentEscalationTarget = currentUser.UserId;
            alert = await context.CallActivityAsync<Alert>(
                nameof(UpdateAlertActivity), alert);

            // Send notification
            var notifyInput = new SendNotificationInput(
                alert.Id,
                alert.CustomerId,
                currentUser.UserId,
                currentUser.DisplayName,
                currentUser.Email,
                currentUser.Phone,
                alert.Title ?? "Alert",
                alert.Description,
                alert.Severity.ToString(),
                alert.ResourceId);

            var notifyResult = await context.CallActivityAsync<SendNotificationResult>(
                nameof(SendNotificationActivity), notifyInput);

            if (!notifyResult.EmailSent && !notifyResult.SmsSent)
            {
                logger.LogWarning(
                    "Failed to send any notification for alert {AlertId} to user {UserName}: {Error}",
                    alert.Id, currentUser.DisplayName, notifyResult.ErrorMessage);
                // Continue to next attempt immediately if notification failed completely
                continue;
            }

            // Wait for the acknowledgment timeout before escalating to next person
            if (attempt < policy.MaxRetries)
            {
                logger.LogInformation(
                    "Waiting {Timeout} before next escalation attempt for alert {AlertId}",
                    policy.AckTimeout, alert.Id);

                var deadline = context.CurrentUtcDateTime.Add(policy.AckTimeout);
                await context.CreateTimer(deadline, CancellationToken.None);
            }
        }

        // All retries exhausted
        logger.LogWarning(
            "All {MaxRetries} escalation attempts exhausted for alert {AlertId}",
            policy.MaxRetries, alert.Id);

        alert.Changes.Add(new AlertChange
        {
            User = "system",
            Comment = $"Escalation completed after {policy.MaxRetries} notification attempts",
            PreviousStatus = alert.Status,
            NewStatus = alert.Status
        });

        await context.CallActivityAsync<Alert>(nameof(UpdateAlertActivity), alert);
    }

    private static List<OnCallUserInfo> BuildEscalationOrder(
        List<OnCallUserInfo> primary,
        List<OnCallUserInfo> backup)
    {
        var result = new List<OnCallUserInfo>();

        // Add primary users first
        result.AddRange(primary);

        // Add backup users who aren't already in primary
        var primaryIds = primary.Select(u => u.UserId).ToHashSet();
        result.AddRange(backup.Where(u => !primaryIds.Contains(u.UserId)));

        return result;
    }
}
