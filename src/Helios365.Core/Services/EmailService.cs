using Azure.Communication.Email;
using Azure.Core;
using Helios365.Core.Exceptions;
using Helios365.Core.Models;
using Microsoft.Extensions.Logging;

namespace Helios365.Core.Services;

public class EmailService : IEmailService
{
    private readonly EmailClient _emailClient;
    private readonly string _fromEmail;
    private readonly ILogger<EmailService> _logger;

    public EmailService(EmailClient emailClient, string fromEmail, ILogger<EmailService> logger)
    {
        _emailClient = emailClient;
        _fromEmail = fromEmail;
        _logger = logger;
    }

    public async Task SendEscalationEmailAsync(
        Alert alert,
        Resource? resource,
        Customer customer,
        List<ActionBase> attemptedActions,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (customer.NotificationEmails == null || !customer.NotificationEmails.Any())
            {
                _logger.LogWarning("No notification emails configured for customer {CustomerId}", customer.Id);
                return;
            }

            _logger.LogInformation("Sending escalation email for alert {AlertId} to {Count} recipients", 
                alert.Id, customer.NotificationEmails.Count);

            var subject = $"[HELIOS365] Alert Escalation - {alert.Title ?? alert.AlertType}";
            var htmlBody = BuildEmailHtml(alert, resource, customer, attemptedActions);

            var emailMessage = new EmailMessage(
                senderAddress: _fromEmail,
                content: new EmailContent(subject)
                {
                    Html = htmlBody
                },
                recipients: new EmailRecipients(
                    customer.NotificationEmails.Select(email => new EmailAddress(email)).ToList()
                )
            );

            var operation = await _emailClient.SendAsync(
                Azure.WaitUntil.Started,
                emailMessage,
                cancellationToken
            );

            _logger.LogInformation("Escalation email sent for alert {AlertId}, operation ID: {OperationId}", 
                alert.Id, operation.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send escalation email for alert {AlertId}", alert.Id);
            throw new EmailException($"Failed to send escalation email: {ex.Message}", ex);
        }
    }

    private string BuildEmailHtml(Alert alert, Resource? resource, Customer customer, List<ActionBase> attemptedActions)
    {
        var actionsHtml = string.Join("", attemptedActions.Select(a =>
        {
            var actionName = a switch
            {
                HealthCheckAction => "Health Check",
                RestartAction => "Restart Resource",
                ScaleAction => "Scale Resource",
                _ => a.Type.ToString()
            };

            return $@"
                <li style=""margin-bottom: 8px;"">
                    <strong>{actionName}</strong> 
                    <span style=""color: #d32f2f;"">❌ Failed</span>
                </li>";
        }));

        var resourceInfo = resource != null
            ? $@"
                <p style=""margin: 10px 0;""><strong>Resource:</strong> {resource.Name}</p>
                <p style=""margin: 10px 0;""><strong>Resource Type:</strong> {resource.ResourceType}</p>
                <p style=""margin: 10px 0;""><strong>Resource ID:</strong> <code style=""background: #f5f5f5; padding: 2px 6px; border-radius: 3px;"">{resource.ResourceId}</code></p>"
            : $@"
                <p style=""margin: 10px 0; color: #f57c00;""><strong>⚠️ Resource not found in Helios365</strong></p>
                <p style=""margin: 10px 0;""><strong>Resource ID:</strong> <code style=""background: #f5f5f5; padding: 2px 6px; border-radius: 3px;"">{alert.ResourceId}</code></p>";

        var severityColor = alert.Severity switch
        {
            AlertSeverity.Critical => "#d32f2f",
            AlertSeverity.High => "#f57c00",
            AlertSeverity.Medium => "#fbc02d",
            AlertSeverity.Low => "#388e3c",
            _ => "#757575"
        };

        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
</head>
<body style=""font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
    <div style=""background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 30px; text-align: center; border-radius: 8px 8px 0 0;"">
        <h1 style=""color: white; margin: 0; font-size: 28px;"">🌞 Helios365</h1>
        <p style=""color: #f0f0f0; margin: 10px 0 0 0;"">Alert Escalation Required</p>
    </div>
    
    <div style=""background: white; padding: 30px; border: 1px solid #e0e0e0; border-top: none; border-radius: 0 0 8px 8px;"">
        <div style=""background: #fff3cd; border-left: 4px solid #ffc107; padding: 15px; margin-bottom: 20px; border-radius: 4px;"">
            <p style=""margin: 0; font-weight: bold;"">⚠️ Manual Intervention Required</p>
            <p style=""margin: 5px 0 0 0;"">An alert could not be automatically resolved and requires your attention.</p>
        </div>

        <h2 style=""color: #333; border-bottom: 2px solid #667eea; padding-bottom: 10px;"">Alert Details</h2>
        
        <div style=""background: #f9f9f9; padding: 15px; border-radius: 4px; margin: 15px 0;"">
            <p style=""margin: 10px 0;""><strong>Alert ID:</strong> {alert.Id}</p>
            <p style=""margin: 10px 0;""><strong>Type:</strong> {alert.AlertType}</p>
            <p style=""margin: 10px 0;""><strong>Severity:</strong> <span style=""color: {severityColor}; font-weight: bold;"">{alert.Severity}</span></p>
            {(alert.Title != null ? $@"<p style=""margin: 10px 0;""><strong>Title:</strong> {alert.Title}</p>" : "")}
            {(alert.Description != null ? $@"<p style=""margin: 10px 0;""><strong>Description:</strong> {alert.Description}</p>" : "")}
            <p style=""margin: 10px 0;""><strong>Started:</strong> {alert.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC</p>
            <p style=""margin: 10px 0;""><strong>Duration:</strong> {(DateTime.UtcNow - alert.CreatedAt).TotalMinutes:F1} minutes</p>
        </div>

        <h2 style=""color: #333; border-bottom: 2px solid #667eea; padding-bottom: 10px;"">Resource Information</h2>
        
        <div style=""background: #f9f9f9; padding: 15px; border-radius: 4px; margin: 15px 0;"">
            {resourceInfo}
        </div>

        {(attemptedActions.Any() ? $@"
        <h2 style=""color: #333; border-bottom: 2px solid #667eea; padding-bottom: 10px;"">Actions Attempted</h2>
        
        <div style=""background: #f9f9f9; padding: 15px; border-radius: 4px; margin: 15px 0;"">
            <ul style=""margin: 0; padding-left: 20px;"">
                {actionsHtml}
            </ul>
        </div>" : "")}

        <div style=""background: #e3f2fd; border-left: 4px solid #2196f3; padding: 15px; margin: 20px 0; border-radius: 4px;"">
            <p style=""margin: 0; font-weight: bold;"">Next Steps</p>
            <p style=""margin: 10px 0 0 0;"">Please investigate this alert and take appropriate action. You can view more details and manage this alert in the Helios365 dashboard.</p>
        </div>

        <div style=""text-align: center; margin-top: 30px;"">
            <a href=""https://helios365.portal/alerts/{alert.Id}"" style=""display: inline-block; background: #667eea; color: white; padding: 12px 30px; text-decoration: none; border-radius: 4px; font-weight: bold;"">View in Dashboard</a>
        </div>

        <div style=""margin-top: 30px; padding-top: 20px; border-top: 1px solid #e0e0e0; font-size: 12px; color: #757575; text-align: center;"">
            <p style=""margin: 5px 0;"">This is an automated message from Helios365</p>
            <p style=""margin: 5px 0;"">Customer: {customer.Name}</p>
            <p style=""margin: 5px 0;"">Alert ID: {alert.Id}</p>
        </div>
    </div>
</body>
</html>";
    }
}
