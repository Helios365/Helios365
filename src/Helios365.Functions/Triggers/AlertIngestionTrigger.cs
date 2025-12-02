using Helios365.Core.Models;
using Helios365.Core.Repositories;
using Helios365.Core.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Web;

namespace Helios365.Functions.Triggers;

public class AlertIngestionTrigger
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IAlertService _alertService;
    private readonly ILogger<AlertIngestionTrigger> _logger;

    public AlertIngestionTrigger(
        ICustomerRepository customerRepository,
        IAlertService alertService,
        ILogger<AlertIngestionTrigger> logger)
    {
        _customerRepository = customerRepository;
        _alertService = alertService;
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
            var queryParams = HttpUtility.ParseQueryString(req.Url.Query);
            var apiKey = queryParams["apiKey"];

            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogWarning("Alert ingestion request missing API key");
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "API key is required" });
                return badRequest;
            }

            var customer = await _customerRepository.GetByApiKeyAsync(apiKey);
            if (customer == null || !customer.Active)
            {
                _logger.LogWarning("Invalid or inactive API key provided");
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Invalid API key" });
                return unauthorized;
            }

            _logger.LogInformation("Alert from customer {CustomerId} ({CustomerName})", customer.Id, customer.Name);

            var body = await req.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(body))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Request body is required" });
                return badRequest;
            }

            var result = await _alertService.IngestAzureMonitorAlertAsync(customer, body);

            return result.Status switch
            {
                AlertProcessingStatus.ValidationFailed => await WriteResponse(req, HttpStatusCode.BadRequest, new
                {
                    error = result.Message
                }),
                AlertProcessingStatus.Resolved => await WriteResponse(req, HttpStatusCode.Accepted, new
                {
                    alertId = result.Alert?.Id,
                    status = "resolved",
                    message = result.Message
                }),
                AlertProcessingStatus.EscalatedUnknownResource => await WriteResponse(req, HttpStatusCode.Accepted, new
                {
                    alertId = result.Alert?.Id,
                    status = "escalated",
                    message = result.Message
                }),
                AlertProcessingStatus.Created => await HandleCreatedAsync(req, client, result.Alert!, result.Message),
                _ => await WriteResponse(req, HttpStatusCode.InternalServerError, new { error = "Unhandled alert status" })
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing alert ingestion");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Internal server error" });
            return errorResponse;
        }
    }

    private async Task<HttpResponseData> HandleCreatedAsync(HttpRequestData req, DurableTaskClient client, Alert alert, string message)
    {
        var instanceId = await client.ScheduleNewOrchestrationInstanceAsync("AlertOrchestrator", alert);
        _logger.LogInformation("Started orchestration {InstanceId} for alert {AlertId}", instanceId, alert.Id);

        return await WriteResponse(req, HttpStatusCode.Accepted, new
        {
            alertId = alert.Id,
            instanceId = instanceId,
            status = "processing",
            message
        });
    }

    private static async Task<HttpResponseData> WriteResponse(HttpRequestData req, HttpStatusCode statusCode, object body)
    {
        var response = req.CreateResponse(statusCode);
        await response.WriteAsJsonAsync(body);
        return response;
    }
}
