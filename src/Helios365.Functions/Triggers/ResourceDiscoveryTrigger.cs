using System.Net;
using Helios365.Core.Models;
using Helios365.Core.Repositories;
using Helios365.Core.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Helios365.Functions.Triggers;

public class ResourceDiscoveryTrigger
{
    private readonly IResourceDiscoveryService _resourceDiscoveryService;
    private readonly ILogger<ResourceDiscoveryTrigger> _logger;

    public ResourceDiscoveryTrigger(
        IResourceDiscoveryService resourceDiscoveryService,
        ILogger<ResourceDiscoveryTrigger> logger)
    {
        _resourceDiscoveryService = resourceDiscoveryService;
        _logger = logger;
    }

    [Function(nameof(SyncAppServices))]
    public async Task<HttpResponseData> SyncAppServices(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "resources/sync/app-services")] HttpRequestData req)
    {
        var cancellationToken = req.FunctionContext.CancellationToken;
        var summary = await _resourceDiscoveryService.SyncAppServicesAsync(cancellationToken);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(summary, cancellationToken);
        return response;
    }
}
