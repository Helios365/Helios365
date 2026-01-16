using Helios365.Core.Contracts;
using Helios365.Core.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Helios365.Functions.Activities;

public class SendNotificationActivity
{
    private readonly INotificationService _notificationService;
    private readonly ILogger<SendNotificationActivity> _logger;

    public SendNotificationActivity(
        INotificationService notificationService,
        ILogger<SendNotificationActivity> logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    [Function(nameof(SendNotificationActivity))]
    public async Task<SendNotificationResult> Run(
        [ActivityTrigger] SendNotificationInput input)
    {
        _logger.LogInformation(
            "Sending notification for alert {AlertId} to user {UserId} ({UserName})",
            input.AlertId, input.UserId, input.UserDisplayName);

        bool emailSent = false;
        bool smsSent = false;
        string? errorMessage = null;

        var emailBody = BuildEmailBody(input);
        var smsMessage = BuildSmsMessage(input);

        // Send Email
        if (!string.IsNullOrWhiteSpace(input.UserEmail))
        {
            try
            {
                var emailSubject = $"[Helios365] {input.AlertSeverity}: {input.AlertTitle}";
                var emailResult = await _notificationService.SendEmailAsync(
                    new[] { input.UserEmail },
                    emailSubject,
                    emailBody);

                emailSent = emailResult.Succeeded;
                if (!emailResult.Succeeded)
                {
                    _logger.LogWarning(
                        "Failed to send email for alert {AlertId}: {Error}",
                        input.AlertId, emailResult.Error);
                    errorMessage = emailResult.Error;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception sending email for alert {AlertId}", input.AlertId);
                errorMessage = ex.Message;
            }
        }
        else
        {
            _logger.LogWarning("No email address for user {UserId}", input.UserId);
        }

        // Send SMS
        if (!string.IsNullOrWhiteSpace(input.UserPhone))
        {
            try
            {
                var smsResult = await _notificationService.SendSmsAsync(
                    new[] { input.UserPhone },
                    smsMessage);

                smsSent = smsResult.Succeeded;
                if (!smsResult.Succeeded)
                {
                    _logger.LogWarning(
                        "Failed to send SMS for alert {AlertId}: {Error}",
                        input.AlertId, smsResult.Error);
                    errorMessage ??= smsResult.Error;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception sending SMS for alert {AlertId}", input.AlertId);
                errorMessage ??= ex.Message;
            }
        }
        else
        {
            _logger.LogWarning("No phone number for user {UserId}", input.UserId);
        }

        _logger.LogInformation(
            "Notification result for alert {AlertId}: EmailSent={EmailSent}, SmsSent={SmsSent}",
            input.AlertId, emailSent, smsSent);

        return new SendNotificationResult(emailSent, smsSent, errorMessage);
    }

    private static string BuildEmailBody(SendNotificationInput input)
    {
        return $"""
            Alert Notification

            Title: {input.AlertTitle}
            Severity: {input.AlertSeverity}
            Resource: {input.ResourceId}

            Description:
            {input.AlertDescription ?? "No description provided"}

            You are receiving this notification because you are on-call.
            Please review this alert in the Helios365 portal.

            --
            Helios365 Alert System
            """;
    }

    private static string BuildSmsMessage(SendNotificationInput input)
    {
        // SMS has character limits, keep it concise
        var truncatedTitle = input.AlertTitle.Length > 50
            ? input.AlertTitle[..47] + "..."
            : input.AlertTitle;

        return $"[Helios365] {input.AlertSeverity}: {truncatedTitle}";
    }
}
