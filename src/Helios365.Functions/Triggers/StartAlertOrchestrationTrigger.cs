using Helios365.Core.Models;
using Helios365.Core.Repositories;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using System.Net;

namespace Helios365.Functions.Triggers;

public class StartAlertOrchestrationTrigger
{
    private readonly IAlertRepository _alertRepository;
    private readonly ILogger<StartAlertOrchestrationTrigger> _logger;

    public StartAlertOrchestrationTrigger(
        IAlertRepository alertRepository,
        ILogger<StartAlertOrchestrationTrigger> logger)
    {
        _alertRepository = alertRepository;
        _logger = logger;
    }

    [Function(nameof(StartAlertOrchestration))]
    public async Task<HttpResponseData> StartAlertOrchestration(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "alerts/{alertId}/orchestrate")] HttpRequestData req,
        string alertId,
        [DurableClient] DurableTaskClient client)
    {
        _logger.LogInformation("Received orchestration start request for alert {AlertId}", alertId);

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

            var instanceId = await client.ScheduleNewOrchestrationInstanceAsync("AlertOrchestrator", alert);
            _logger.LogInformation("Started alert orchestration {InstanceId} for alert {AlertId}", instanceId, alertId);

            var response = req.CreateResponse(HttpStatusCode.Accepted);
            await response.WriteAsJsonAsync(new
            {
                alertId = alert.Id,
                instanceId,
                status = "orchestrating",
                message = "Alert orchestration started"
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting orchestration for alert {AlertId}", alertId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to start alert orchestration" });
            return errorResponse;
        }
    }
}
