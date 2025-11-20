using Helios365.Core.Models;
using Helios365.Core.Repositories;
using Helios365.Core.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using System.Web;

namespace Helios365.Functions.Triggers;

public class AlertIngestionTrigger
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IResourceRepository _resourceRepository;
    private readonly IAlertRepository _alertRepository;
    private readonly ILogger<AlertIngestionTrigger> _logger;

    public AlertIngestionTrigger(
        ICustomerRepository customerRepository,
        IResourceRepository resourceRepository,
        IAlertRepository alertRepository,
        ILogger<AlertIngestionTrigger> logger)
    {
        _customerRepository = customerRepository;
        _resourceRepository = resourceRepository;
        _alertRepository = alertRepository;
        _logger = logger;
    }

    [Function(nameof(AlertIngestion))]
    public async Task<HttpResponseData> AlertIngestion(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "alerts")] HttpRequestData req,
        [DurableClient] DurableTaskClient client)
    {
        _logger.LogInformation("Received alert ingestion request");

        try
        {
            // 1. Extract and validate API key
            var queryParams = HttpUtility.ParseQueryString(req.Url.Query);
            var apiKey = queryParams["apiKey"];

            _logger.LogInformation("Received alert ingestion request with API key: {ApiKey}", apiKey);

            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogWarning("Alert ingestion request missing API key");
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "API key is required" });
                return badRequest;
            }

            // 2. Validate customer
            var customer = await _customerRepository.GetByApiKeyAsync(apiKey);
            if (customer == null || !customer.Active)
            {
                _logger.LogWarning("Invalid or inactive API key: {ApiKey}", apiKey);
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Invalid API key" });
                return unauthorized;
            }

            _logger.LogInformation("Alert from customer {CustomerId} ({CustomerName})", customer.Id, customer.Name);

            // 3. Parse alert payload
            var body = await req.ReadAsStringAsync();
            if (string.IsNullOrEmpty(body))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Request body is required" });
                return badRequest;
            }

            var alertPayload = JsonSerializer.Deserialize<AlertPayload>(body);
            if (alertPayload == null || string.IsNullOrEmpty(alertPayload.ResourceId))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid alert payload. ResourceId is required." });
                return badRequest;
            }

            // 4. Look up resource
            var resource = await _resourceRepository.GetByResourceIdAsync(customer.Id, alertPayload.ResourceId);
            
            // 5. Create alert
            var alert = new Alert
            {
                Id = Guid.NewGuid().ToString(),
                CustomerId = customer.Id,
                ResourceId = alertPayload.ResourceId,
                ResourceType = alertPayload.ResourceType ?? "Unknown",
                AlertType = alertPayload.AlertType ?? "Unknown",
                Title = alertPayload.Title,
                Description = alertPayload.Description,
                Severity = alertPayload.Severity,
                Status = AlertStatus.Received,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _alertRepository.CreateAsync(alert);
            _logger.LogInformation("Created alert {AlertId}", alert.Id);

            // 6. If resource not found → Escalate immediately
            if (resource == null)
            {
                _logger.LogWarning("Resource {ResourceId} not found for customer {CustomerId}. Escalating immediately.", 
                    alertPayload.ResourceId, customer.Id);

                alert.MarkStatus(AlertStatus.Escalated);
                await _alertRepository.UpdateAsync(alert.Id, alert);

                // Send escalation email
                //await _emailService.SendEscalationEmailAsync(alert, null, customer, new List<ActionBase>());

                var response = req.CreateResponse(HttpStatusCode.Accepted);
                await response.WriteAsJsonAsync(new
                {
                    alertId = alert.Id,
                    status = "escalated",
                    message = "Resource not found in Helios365. Alert escalated to on-call."
                });

                return response;
            }

            // 7. Start durable orchestration
            var instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
                "AlertOrchestrator",
                alert);

            _logger.LogInformation("Started orchestration {InstanceId} for alert {AlertId}", instanceId, alert.Id);

            // 8. Return response
            var acceptedResponse = req.CreateResponse(HttpStatusCode.Accepted);
            await acceptedResponse.WriteAsJsonAsync(new
            {
                alertId = alert.Id,
                instanceId = instanceId,
                status = "processing",
                message = "Alert received and processing started"
            });

            return acceptedResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing alert ingestion");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Internal server error" });
            return errorResponse;
        }
    }

    // DTO for incoming alert payload
    public class AlertPayload
    {
        public string ResourceId { get; set; } = string.Empty;
        public string? ResourceType { get; set; }
        public string? AlertType { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public AlertSeverity Severity { get; set; } = AlertSeverity.Medium;
    }
}
