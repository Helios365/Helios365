using Helios365.Core.Contracts;
using Helios365.Core.Models;
using Helios365.Core.Repositories;
using Helios365.Functions.Activities;
using Helios365.Functions.Orchestrators;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using System.Net;

namespace Helios365.Functions.Triggers;

public class EscalateAlertTrigger
{
    private readonly IAlertRepository _alertRepository;
    private readonly ILogger<EscalateAlertTrigger> _logger;

    public EscalateAlertTrigger(
        IAlertRepository alertRepository,
        ILogger<EscalateAlertTrigger> logger)
    {
        _alertRepository = alertRepository;
        _logger = logger;
    }

    [Function(nameof(EscalateAlert))]
    public async Task<HttpResponseData> EscalateAlert(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "alerts/{alertId}/escalate")] HttpRequestData req,
        string alertId,
        [DurableClient] DurableTaskClient client)
    {
        _logger.LogInformation("Received escalate request for alert {AlertId}", alertId);

        if (string.IsNullOrWhiteSpace(alertId))
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteAsJsonAsync(new { error = "Alert ID is required" });
            return badRequest;
        }

        try
        {
            var alert = await _alertRepository.GetAsync(alertId);
            if (alert == null)
            {
                _logger.LogWarning("Alert {AlertId} not found", alertId);
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Alert not found" });
                return notFound;
            }

            if (alert.Status == AlertStatus.Resolved)
            {
                _logger.LogWarning("Alert {AlertId} is already resolved", alertId);
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { error = "Alert is already resolved" });
                return conflict;
            }

            // Mark alert as escalated
            alert.MarkStatus(AlertStatus.Escalated);
            alert.EscalationAttempts++;
            alert.Changes ??= new List<AlertChange>();
            alert.Changes.Add(new AlertChange
            {
                Id = Guid.NewGuid().ToString(),
                User = "system",
                Comment = "Manual escalation to backup requested via portal",
                NewStatus = AlertStatus.Escalated,
                CreatedAt = DateTime.UtcNow
            });

            await _alertRepository.UpdateAsync(alert.Id, alert);

            // Start the alert orchestration which will handle notifications
            var instanceId = await client.ScheduleNewOrchestrationInstanceAsync("AlertOrchestrator", alert);
            _logger.LogInformation("Started escalation orchestration {InstanceId} for alert {AlertId}", instanceId, alertId);

            var response = req.CreateResponse(HttpStatusCode.Accepted);
            await response.WriteAsJsonAsync(new
            {
                alertId = alert.Id,
                instanceId,
                status = "escalating",
                message = "Alert escalation started"
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error escalating alert {AlertId}", alertId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to escalate alert" });
            return errorResponse;
        }
    }
}
