using Azure;
using Azure.Communication.Email;
using Azure.Communication.Sms;
using Azure.Core;
using Helios365.Core.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Helios365.Core.Services;

public interface INotificationService
{
    /// <summary>
    /// Sends a plain-text email to the specified recipients.
    /// </summary>
    Task<NotificationSendResult> SendEmailAsync(
        IEnumerable<string> recipients,
        string subject,
        string body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an HTML email with a plain-text fallback to the specified recipients.
    /// </summary>
    Task<NotificationSendResult> SendHtmlEmailAsync(
        IEnumerable<string> recipients,
        string subject,
        string htmlBody,
        string plainTextBody,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an SMS message to the specified recipients.
    /// </summary>
    Task<NotificationSendResult> SendSmsAsync(
        IEnumerable<string> recipients,
        string message,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Sends plain-text emails and SMS messages via Azure Communication Services.
/// </summary>
public class NotificationService : INotificationService
{
    private readonly EmailClient emailClient;
    private readonly SmsClient smsClient;
    private readonly CommunicationServiceOptions options;
    private readonly ILogger<NotificationService> logger;

    public NotificationService(
        EmailClient emailClient,
        SmsClient smsClient,
        IOptions<CommunicationServiceOptions> options,
        ILogger<NotificationService> logger)
    {
        this.emailClient = emailClient;
        this.smsClient = smsClient;
        this.logger = logger;
        this.options = options.Value;
    }

    public async Task<NotificationSendResult> SendEmailAsync(
        IEnumerable<string> recipients,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        var recipientList = NormalizeRecipients(recipients);

        if (recipientList.Count == 0)
        {
            throw new ArgumentException("At least one recipient is required.", nameof(recipients));
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new ArgumentException("Email subject is required.", nameof(subject));
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            throw new ArgumentException("Email body is required.", nameof(body));
        }

        EnsureEmailConfigured();

        try
        {
            var content = new EmailContent(subject)
            {
                PlainText = body
            };

            var emailRecipients = new EmailRecipients(
                recipientList.Select(address => new EmailAddress(address)));

            var message = new EmailMessage(options.EmailSender, emailRecipients, content);

            var operation = await emailClient.SendAsync(WaitUntil.Completed, message, cancellationToken)
                .ConfigureAwait(false);

            var status = operation.Value?.Status.ToString() ?? "Unknown";

            logger.LogInformation(
                "Sent email via Communication Services to {Recipients}. Status: {Status}",
                string.Join(", ", recipientList),
                status);

            return NotificationSendResult.Success(recipientList, Array.Empty<string>());
        }
        catch (RequestFailedException ex)
        {
            logger.LogError(ex,
                "Azure Communication Services email request failed to {Recipients}. Status: {Status}, ErrorCode: {ErrorCode}, Message: {Message}",
                string.Join(", ", recipientList), ex.Status, ex.ErrorCode, ex.Message);
            return NotificationSendResult.Failure(recipientList, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email via Communication Services to {Recipients}", string.Join(", ", recipientList));
            return NotificationSendResult.Failure(recipientList, ex.Message);
        }
    }

    public async Task<NotificationSendResult> SendHtmlEmailAsync(
        IEnumerable<string> recipients,
        string subject,
        string htmlBody,
        string plainTextBody,
        CancellationToken cancellationToken = default)
    {
        var recipientList = NormalizeRecipients(recipients);

        if (recipientList.Count == 0)
        {
            throw new ArgumentException("At least one recipient is required.", nameof(recipients));
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new ArgumentException("Email subject is required.", nameof(subject));
        }

        if (string.IsNullOrWhiteSpace(htmlBody))
        {
            throw new ArgumentException("Email HTML body is required.", nameof(htmlBody));
        }

        EnsureEmailConfigured();

        try
        {
            var content = new EmailContent(subject)
            {
                Html = htmlBody,
                PlainText = string.IsNullOrWhiteSpace(plainTextBody) ? null : plainTextBody
            };

            var emailRecipients = new EmailRecipients(
                recipientList.Select(address => new EmailAddress(address)));

            var message = new EmailMessage(options.EmailSender, emailRecipients, content);

            var operation = await emailClient.SendAsync(WaitUntil.Completed, message, cancellationToken)
                .ConfigureAwait(false);

            var status = operation.Value?.Status.ToString() ?? "Unknown";

            logger.LogInformation(
                "Sent HTML email via Communication Services to {Recipients}. Status: {Status}",
                string.Join(", ", recipientList),
                status);

            return NotificationSendResult.Success(recipientList, Array.Empty<string>());
        }
        catch (RequestFailedException ex)
        {
            logger.LogError(ex,
                "Azure Communication Services HTML email request failed to {Recipients}. Status: {Status}, ErrorCode: {ErrorCode}, Message: {Message}",
                string.Join(", ", recipientList), ex.Status, ex.ErrorCode, ex.Message);
            return NotificationSendResult.Failure(recipientList, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send HTML email via Communication Services to {Recipients}", string.Join(", ", recipientList));
            return NotificationSendResult.Failure(recipientList, ex.Message);
        }
    }

    public async Task<NotificationSendResult> SendSmsAsync(
        IEnumerable<string> recipients,
        string message,
        CancellationToken cancellationToken = default)
    {
        var recipientList = NormalizeRecipients(recipients);

        if (recipientList.Count == 0)
        {
            throw new ArgumentException("At least one recipient is required.", nameof(recipients));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("SMS message is required.", nameof(message));
        }

        EnsureSmsConfigured();

        logger.LogInformation(
            "Attempting to send SMS to {RecipientCount} recipient(s) from sender {SmsSender}",
            recipientList.Count,
            MaskPhoneNumber(options.SmsSender));

        var messageIds = new List<string>();
        var failedRecipients = new List<string>();

        foreach (var recipient in recipientList)
        {
            try
            {
                var response = await smsClient.SendAsync(
                    from: options.SmsSender,
                    to: recipient,
                    message: message,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                var sendResult = response.Value;

                if (sendResult.Successful)
                {
                    if (!string.IsNullOrWhiteSpace(sendResult.MessageId))
                    {
                        messageIds.Add(sendResult.MessageId);
                    }
                }
                else
                {
                    failedRecipients.Add(recipient);
                    logger.LogWarning(
                        "Communication Services failed to send SMS to {Recipient}. StatusCode: {StatusCode}, Error: {ErrorMessage}",
                        recipient,
                        sendResult.HttpStatusCode,
                        sendResult.ErrorMessage ?? "Unknown error");
                }
            }
            catch (RequestFailedException ex)
            {
                failedRecipients.Add(recipient);
                logger.LogError(ex,
                    "Azure Communication Services SMS request failed to {Recipient}. Status: {Status}, ErrorCode: {ErrorCode}, Message: {Message}",
                    recipient, ex.Status, ex.ErrorCode, ex.Message);
            }
            catch (Exception ex)
            {
                failedRecipients.Add(recipient);
                logger.LogError(ex, "Exception while sending SMS via Communication Services to {Recipient}", recipient);
            }
        }

        if (failedRecipients.Count == 0)
        {
            logger.LogInformation(
                "Sent SMS via Communication Services to {Recipients}. MessageIds: {MessageIds}",
                string.Join(", ", recipientList),
                string.Join(", ", messageIds));

            return NotificationSendResult.Success(recipientList, messageIds);
        }

        var errorMessage = $"Failed to send SMS to {string.Join(", ", failedRecipients)}.";

        return NotificationSendResult.Failure(recipientList, errorMessage, failedRecipients, messageIds);
    }

    private void EnsureEmailConfigured()
    {
        if (string.IsNullOrWhiteSpace(options.EmailSender))
        {
            logger.LogError("CommunicationServices:EmailSender is not configured. Email cannot be sent.");
            throw new InvalidOperationException("CommunicationServices:EmailSender is not configured.");
        }
    }

    private void EnsureSmsConfigured()
    {
        if (string.IsNullOrWhiteSpace(options.SmsSender))
        {
            logger.LogError("CommunicationServices:SmsSender is not configured. SMS cannot be sent.");
            throw new InvalidOperationException("CommunicationServices:SmsSender is not configured.");
        }
    }

    private static IReadOnlyList<string> NormalizeRecipients(IEnumerable<string> recipients)
    {
        if (recipients is null)
        {
            return new List<string>();
        }

        return recipients
            .Select(r => r?.Trim())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string MaskPhoneNumber(string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber) || phoneNumber.Length < 6)
        {
            return phoneNumber ?? "(not set)";
        }

        // Show first 3 and last 2 characters, mask the rest
        return $"{phoneNumber[..3]}***{phoneNumber[^2..]}";
    }
}
