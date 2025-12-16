using Azure;
using Azure.Communication.Email;
using Azure.Communication.Sms;
using Azure.Core;
using Helios365.Core.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Helios365.Core.Services;

public interface ICommunicationService
{
    Task<CommunicationSendResult> SendEmailAsync(
        IEnumerable<string> recipients,
        string subject,
        string body,
        CancellationToken cancellationToken = default);

    Task<CommunicationSendResult> SendSmsAsync(
        IEnumerable<string> recipients,
        string message,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Sends plain-text emails and SMS messages via Azure Communication Services.
/// </summary>
public class CommunicationService : ICommunicationService
{
    private readonly EmailClient emailClient;
    private readonly SmsClient smsClient;
    private readonly CommunicationServiceOptions options;
    private readonly ILogger<CommunicationService> logger;

    public CommunicationService(
        EmailClient emailClient,
        SmsClient smsClient,
        IOptions<CommunicationServiceOptions> options,
        ILogger<CommunicationService> logger)
    {
        this.emailClient = emailClient;
        this.smsClient = smsClient;
        this.logger = logger;
        this.options = options.Value;
    }

    public async Task<CommunicationSendResult> SendEmailAsync(
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

            return CommunicationSendResult.Success(recipientList, Array.Empty<string>());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email via Communication Services to {Recipients}", string.Join(", ", recipientList));
            return CommunicationSendResult.Failure(recipientList, ex.Message);
        }
    }

    public async Task<CommunicationSendResult> SendSmsAsync(
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

            return CommunicationSendResult.Success(recipientList, messageIds);
        }

        var errorMessage = $"Failed to send SMS to {string.Join(", ", failedRecipients)}.";

        return CommunicationSendResult.Failure(recipientList, errorMessage, failedRecipients, messageIds);
    }

    private void EnsureEmailConfigured()
    {
        if (string.IsNullOrWhiteSpace(options.EmailSender))
        {
            throw new InvalidOperationException("CommunicationServices:EmailSender is not configured.");
        }
    }

    private void EnsureSmsConfigured()
    {
        if (string.IsNullOrWhiteSpace(options.SmsSender))
        {
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
}
