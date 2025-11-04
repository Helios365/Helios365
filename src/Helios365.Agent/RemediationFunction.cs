using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.AppService;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace Helios365.Agent;

public class RemediationFunction
{
    private readonly ILogger<RemediationFunction> _logger;

    public RemediationFunction(ILogger<RemediationFunction> logger)
    {
        _logger = logger;
    }

    [Function("Remediate")]
    public async Task<HttpResponseData> Remediate(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "remediate")] HttpRequestData req)
    {
        _logger.LogInformation("Received remediation request");

        try
        {
            var body = await req.ReadAsStringAsync();
            if (string.IsNullOrEmpty(body))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteStringAsync("Request body is required");
                return badRequest;
            }

            var request = JsonSerializer.Deserialize<RemediationRequest>(body);
            if (request == null || string.IsNullOrEmpty(request.ResourceId))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteStringAsync("Invalid request payload");
                return badRequest;
            }

            _logger.LogInformation("Remediating resource {ResourceId} with action {Action}",
                request.ResourceId, request.Action);

            // Use Managed Identity to authenticate
            var credential = new DefaultAzureCredential();
            var armClient = new ArmClient(credential);

            bool success = request.Action.ToLower() switch
            {
                "restart" => await RestartResourceAsync(armClient, request.ResourceId),
                _ => throw new NotImplementedException($"Action {request.Action} not implemented")
            };

            var response = req.CreateResponse(success ? HttpStatusCode.OK : HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new
            {
                success = success,
                resourceId = request.ResourceId,
                action = request.Action,
                timestamp = DateTime.UtcNow
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during remediation");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync($"Remediation failed: {ex.Message}");
            return errorResponse;
        }
    }

    private async Task<bool> RestartResourceAsync(ArmClient armClient, string resourceId)
    {
        try
        {
            // Example: Restart an App Service
            // Note: This is simplified - you'd need to parse the resource ID to determine the type
            if (resourceId.Contains("Microsoft.Web/sites"))
            {
                var siteId = new ResourceIdentifier(resourceId);
                var site = armClient.GetWebSiteResource(siteId);
                await site.RestartAsync();
                _logger.LogInformation("Successfully restarted App Service {ResourceId}", resourceId);
                return true;
            }

            // Add more resource types as needed (VMs, etc.)
            _logger.LogWarning("Resource type not supported for restart: {ResourceId}", resourceId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restart resource {ResourceId}", resourceId);
            return false;
        }
    }

    public class RemediationRequest
    {
        public string ResourceId { get; set; } = string.Empty;
        public string Action { get; set; } = "restart";
    }
}
