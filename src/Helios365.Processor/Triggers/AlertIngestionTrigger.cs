using Helios365.Core.Models;
using Helios365.Core.Repositories;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace Helios365.Processor.Triggers;

public class AlertIngestionTrigger
{
    private readonly ILogger<AlertIngestionTrigger> _logger;
    private readonly IAlertRepository _alertRepository;

    public AlertIngestionTrigger(
        ILogger<AlertIngestionTrigger> logger,
        IAlertRepository alertRepository)
    {
        _logger = logger;
        _alertRepository = alertRepository;
    }

    [Function(nameof(AlertIngestion))]
    public async Task<HttpResponseData> AlertIngestion(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "alerts")] HttpRequestData req,
        [DurableClient] DurableTaskClient client)
    {
        _logger.LogInformation("Received alert ingestion request");

        try
        {
            // Parse request body
            var body = await req.ReadAsStringAsync();
            if (string.IsNullOrEmpty(body))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteStringAsync("Request body is required");
                return badRequest;
            }

            var alertData = JsonSerializer.Deserialize<AlertPayload>(body);
            if (alertData == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteStringAsync("Invalid alert payload");
                return badRequest;
            }

            // Create alert
            var alert = new Alert
            {
                Id = Guid.NewGuid().ToString(),
                CustomerId = alertData.CustomerId,
                ResourceId = alertData.ResourceId,
                ResourceType = alertData.ResourceType,
                AlertType = alertData.AlertType,
                Title = alertData.Title,
                Description = alertData.Description,
                Severity = alertData.Severity,
                HealthCheckUrl = alertData.HealthCheckUrl,
                Status = AlertStatus.Received
            };

            // Save to database
            await _alertRepository.CreateAsync(alert);

            // Start durable orchestration
            string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
                "AlertOrchestrator",
                alert);

            _logger.LogInformation("Started orchestration {InstanceId} for alert {AlertId}", instanceId, alert.Id);

            // Return response
            var response = req.CreateResponse(HttpStatusCode.Accepted);
            await response.WriteAsJsonAsync(new
            {
                alertId = alert.Id,
                instanceId = instanceId,
                status = "accepted"
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing alert ingestion");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync("Internal server error");
            return errorResponse;
        }
    }

    // DTO for incoming alert payload
    public class AlertPayload
    {
        public string CustomerId { get; set; } = string.Empty;
        public string ResourceId { get; set; } = string.Empty;
        public string ResourceType { get; set; } = string.Empty;
        public string AlertType { get; set; } = string.Empty;
        public string? Title { get; set; }
        public string? Description { get; set; }
        public AlertSeverity Severity { get; set; } = AlertSeverity.Medium;
        public string? HealthCheckUrl { get; set; }
    }
}
