using Helios365.Core.Contracts;
using Helios365.Core.Services;
using Helios365.Core.Templates.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Helios365.Functions.Activities;

public class SendNotificationActivity
{
    private readonly INotificationService _notificationService;
    private readonly IEmailTemplateService _emailTemplateService;
    private readonly CommunicationServiceOptions _options;
    private readonly ILogger<SendNotificationActivity> _logger;

    public SendNotificationActivity(
        INotificationService notificationService,
        IEmailTemplateService emailTemplateService,
        IOptions<CommunicationServiceOptions> options,
        ILogger<SendNotificationActivity> logger)
    {
        _notificationService = notificationService;
        _emailTemplateService = emailTemplateService;
        _options = options.Value;
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

        // Construct alert URL if not provided
        var alertUrl = input.AlertUrl;
        if (string.IsNullOrWhiteSpace(alertUrl) && !string.IsNullOrWhiteSpace(_options.PortalBaseUrl))
        {
            alertUrl = $"{_options.PortalBaseUrl.TrimEnd('/')}/alerts/{input.AlertId}";
        }

        // Note: URLs are excluded from SMS until toll-free verification is complete
        // Carriers silently drop SMS containing links from unverified numbers
        var smsMessage = BuildSmsMessage(input, alertUrl: null);

        // Send Email
        if (!string.IsNullOrWhiteSpace(input.UserEmail))
        {
            try
            {
                var emailModel = new AlertEmailModel
                {
                    AlertId = input.AlertId,
                    Title = input.AlertTitle,
                    Description = input.AlertDescription,
                    Severity = input.AlertSeverity,
                    AlertType = input.AlertType,
                    ResourceName = input.ResourceName,
                    CustomerName = input.CustomerName,
                    SubscriptionName = input.SubscriptionName,
                    RecipientName = input.UserDisplayName,
                    Timestamp = DateTime.UtcNow,
                    PortalUrl = alertUrl
                };

                var emailTemplate = await _emailTemplateService.RenderAlertNotificationAsync(emailModel);

                var emailResult = await _notificationService.SendHtmlEmailAsync(
                    new[] { input.UserEmail },
                    emailTemplate.Subject,
                    emailTemplate.HtmlBody,
                    emailTemplate.PlainTextBody);

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

        if (errorMessage != null)
        {
            _logger.LogError(
                "Notification FAILED for alert {AlertId}: EmailSent={EmailSent}, SmsSent={SmsSent}, Error={Error}",
                input.AlertId, emailSent, smsSent, errorMessage);
        }
        else
        {
            _logger.LogInformation(
                "Notification result for alert {AlertId}: EmailSent={EmailSent}, SmsSent={SmsSent}",
                input.AlertId, emailSent, smsSent);
        }

        return new SendNotificationResult(emailSent, smsSent, errorMessage);
    }

    private static string BuildSmsMessage(SendNotificationInput input, string? alertUrl)
    {
        // SMS messages can be longer than 160 chars (carriers will split into multiple segments)
        // but we still want to keep it reasonably concise
        const int maxTitleLength = 60;
        const int maxDescriptionLength = 100;

        var title = input.AlertTitle.Length > maxTitleLength
            ? input.AlertTitle[..(maxTitleLength - 3)] + "..."
            : input.AlertTitle;

        var description = string.IsNullOrWhiteSpace(input.AlertDescription)
            ? null
            : input.AlertDescription.Length > maxDescriptionLength
                ? input.AlertDescription[..(maxDescriptionLength - 3)] + "..."
                : input.AlertDescription;

        var message = $"[Helios365] {input.AlertSeverity}: {title}";

        if (!string.IsNullOrWhiteSpace(description))
        {
            message += $"\n{description}";
        }

        if (!string.IsNullOrWhiteSpace(alertUrl))
        {
            message += $"\n{alertUrl}";
        }

        return message;
    }
}
