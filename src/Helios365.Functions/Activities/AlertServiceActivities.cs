using Helios365.Core.Models;
using Helios365.Core.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Helios365.Functions.Activities;

public record UpdateEscalationStateInput(string AlertId, int Attempt, string TargetUserId);
public record RecordNotificationResultInput(string AlertId, string UserName, bool EmailSent, bool SmsSent, string? ErrorMessage, bool IsBackup);
public record MarkEscalatedInput(string AlertId, string Reason);
public record MarkFailedInput(string AlertId, string Reason);
public record AddTimelineEntryInput(string AlertId, string Message);

public class AlertServiceActivities
{
    private readonly IAlertService _alertService;
    private readonly ILogger<AlertServiceActivities> _logger;

    public AlertServiceActivities(
        IAlertService alertService,
        ILogger<AlertServiceActivities> logger)
    {
        _alertService = alertService;
        _logger = logger;
    }

    [Function(nameof(UpdateEscalationStateActivity))]
    public async Task<Alert> UpdateEscalationStateActivity(
        [ActivityTrigger] UpdateEscalationStateInput input)
    {
        _logger.LogInformation(
            "Updating escalation state for alert {AlertId} - Attempt: {Attempt}, Target: {Target}",
            input.AlertId, input.Attempt, input.TargetUserId);

        return await _alertService.UpdateEscalationStateAsync(input.AlertId, input.Attempt, input.TargetUserId);
    }

    [Function(nameof(RecordNotificationResultActivity))]
    public async Task<Alert> RecordNotificationResultActivity(
        [ActivityTrigger] RecordNotificationResultInput input)
    {
        _logger.LogInformation(
            "Recording notification result for alert {AlertId} - User: {User}, Email: {Email}, SMS: {SMS}",
            input.AlertId, input.UserName, input.EmailSent, input.SmsSent);

        return await _alertService.RecordNotificationResultAsync(
            input.AlertId,
            input.UserName,
            input.EmailSent,
            input.SmsSent,
            input.ErrorMessage,
            input.IsBackup);
    }

    [Function(nameof(MarkEscalatedActivity))]
    public async Task<Alert> MarkEscalatedActivity(
        [ActivityTrigger] MarkEscalatedInput input)
    {
        _logger.LogInformation(
            "Marking alert {AlertId} as escalated: {Reason}",
            input.AlertId, input.Reason);

        return await _alertService.MarkEscalatedAsync(input.AlertId, input.Reason);
    }

    [Function(nameof(MarkFailedActivity))]
    public async Task<Alert> MarkFailedActivity(
        [ActivityTrigger] MarkFailedInput input)
    {
        _logger.LogInformation(
            "Marking alert {AlertId} as failed: {Reason}",
            input.AlertId, input.Reason);

        return await _alertService.MarkFailedAsync(input.AlertId, input.Reason);
    }

    [Function(nameof(AddTimelineEntryActivity))]
    public async Task<Alert> AddTimelineEntryActivity(
        [ActivityTrigger] AddTimelineEntryInput input)
    {
        _logger.LogInformation(
            "Adding timeline entry for alert {AlertId}: {Message}",
            input.AlertId, input.Message);

        return await _alertService.AddTimelineEntryAsync(input.AlertId, input.Message);
    }
}
