using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.ResourceGraph;
using Azure.ResourceManager.ResourceGraph.Models;
using Azure.Security.KeyVault.Secrets;
using Helios365.Core.Models;
using Helios365.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace Helios365.Core.Services;

public class AzureResourceGraphService : IAzureResourceGraphService
{
    private const string AppServiceQuery = """
        Resources
        | where type =~ 'microsoft.web/sites'
        | project id, name, resourceGroup, location, kind, tags
        """;

    private const string FunctionAppQuery = """
        Resources
        | where type =~ 'microsoft.web/sites'
        | where tolower(kind) contains 'functionapp'
        | project id, name, resourceGroup, location, kind, tags
        """;

    private const string VirtualMachineQuery = """
        Resources
        | where type =~ 'microsoft.compute/virtualmachines'
        | project id, name, resourceGroup, location,
            vmSize = tostring(properties.hardwareProfile.vmSize),
            osType = tostring(properties.storageProfile.osDisk.osType),
            tags
        """;

    private const string MySqlFlexibleServerQuery = """
        Resources
        | where type =~ 'microsoft.dbformysql/flexibleservers'
        | project id, name, resourceGroup, location,
            skuName = tostring(sku.name),
            engineVersion = tostring(properties.version),
            tags
        """;

    private const string ServiceBusNamespaceQuery = """
        Resources
        | where type =~ 'microsoft.servicebus/namespaces'
        | project id, name, resourceGroup, location,
            skuName = tostring(sku.name),
            capacity = tostring(properties.capacity),
            tags
        """;

    private readonly SecretClient _secretClient;
    private readonly ILogger<AzureResourceGraphService> _logger;

    public AzureResourceGraphService(SecretClient secretClient, ILogger<AzureResourceGraphService> logger)
    {
        _secretClient = secretClient;
        _logger = logger;
    }

    public Task<IReadOnlyList<Resource>> GetAppServicesAsync(
        ServicePrincipal servicePrincipal,
        IEnumerable<string> subscriptionIds,
        CancellationToken cancellationToken = default) =>
        QueryResourcesAsync(
            servicePrincipal,
            subscriptionIds,
            AppServiceQuery,
            "Microsoft.Web/sites",
            "App Services",
            null,
            cancellationToken);

    public Task<IReadOnlyList<Resource>> GetFunctionAppsAsync(
        ServicePrincipal servicePrincipal,
        IEnumerable<string> subscriptionIds,
        CancellationToken cancellationToken = default) =>
        QueryResourcesAsync(
            servicePrincipal,
            subscriptionIds,
            FunctionAppQuery,
            "Microsoft.Web/sites",
            "Azure Functions",
            null,
            cancellationToken);

    public Task<IReadOnlyList<Resource>> GetVirtualMachinesAsync(
        ServicePrincipal servicePrincipal,
        IEnumerable<string> subscriptionIds,
        CancellationToken cancellationToken = default) =>
        QueryResourcesAsync(
            servicePrincipal,
            subscriptionIds,
            VirtualMachineQuery,
            "Microsoft.Compute/virtualMachines",
            "Virtual Machines",
            AddVirtualMachineMetadata,
            cancellationToken);

    public Task<IReadOnlyList<Resource>> GetMySqlFlexibleServersAsync(
        ServicePrincipal servicePrincipal,
        IEnumerable<string> subscriptionIds,
        CancellationToken cancellationToken = default) =>
        QueryResourcesAsync(
            servicePrincipal,
            subscriptionIds,
            MySqlFlexibleServerQuery,
            "Microsoft.DBforMySQL/flexibleServers",
            "MySQL Flexible Servers",
            AddMySqlMetadata,
            cancellationToken);

    public Task<IReadOnlyList<Resource>> GetServiceBusNamespacesAsync(
        ServicePrincipal servicePrincipal,
        IEnumerable<string> subscriptionIds,
        CancellationToken cancellationToken = default) =>
        QueryResourcesAsync(
            servicePrincipal,
            subscriptionIds,
            ServiceBusNamespaceQuery,
            "Microsoft.ServiceBus/namespaces",
            "Service Bus namespaces",
            AddServiceBusMetadata,
            cancellationToken);

    private async Task<IReadOnlyList<Resource>> QueryResourcesAsync(
        ServicePrincipal servicePrincipal,
        IEnumerable<string> subscriptionIds,
        string query,
        string resourceType,
        string resourceDescription,
        Action<JsonElement, Dictionary<string, string>>? metadataEnricher,
        CancellationToken cancellationToken)
    {
        if (servicePrincipal is null)
        {
            throw new ArgumentNullException(nameof(servicePrincipal));
        }

        if (subscriptionIds is null)
        {
            throw new ArgumentNullException(nameof(subscriptionIds));
        }

        var subscriptionList = subscriptionIds
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToArray();

        if (subscriptionList.Length == 0)
        {
            _logger.LogWarning("No subscriptionIds provided for ServicePrincipal {ServicePrincipalId}", servicePrincipal.Id);
            return Array.Empty<Resource>();
        }

        try
        {
            var clientSecret = await GetClientSecretAsync(servicePrincipal, cancellationToken).ConfigureAwait(false);

            var credential = new ClientSecretCredential(
                servicePrincipal.TenantId,
                servicePrincipal.ClientId,
                clientSecret);

            var armClient = CreateArmClient(credential, servicePrincipal.CloudEnvironment);
            var tenant = await ResolveTenantAsync(armClient, cancellationToken).ConfigureAwait(false);

            var request = new ResourceQueryContent(query)
            {
                Options = new ResourceQueryRequestOptions
                {
                    ResultFormat = ResultFormat.ObjectArray
                }
            };

            foreach (var subscriptionId in subscriptionList)
            {
                request.Subscriptions.Add(subscriptionId);
            }

            _logger.LogInformation(
                "Querying Azure Resource Graph for {ResourceDescription} using ServicePrincipal {ServicePrincipalId} across {SubscriptionCount} subscriptions",
                resourceDescription,
                servicePrincipal.Id,
                subscriptionList.Length);

            var response = await tenant.GetResourcesAsync(request, cancellationToken).ConfigureAwait(false);

            return MapToResources(response, servicePrincipal, resourceType, metadataEnricher);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(
                ex,
                "Azure Resource Graph request failed for ServicePrincipal {ServicePrincipalId} when querying {ResourceDescription}",
                servicePrincipal.Id,
                resourceDescription);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error while querying Azure Resource Graph for ServicePrincipal {ServicePrincipalId} when querying {ResourceDescription}",
                servicePrincipal.Id,
                resourceDescription);
            throw;
        }
    }

    private async Task<string> GetClientSecretAsync(ServicePrincipal servicePrincipal, CancellationToken cancellationToken)
    {
        // Preferred path: use the Key Vault reference stored on the ServicePrincipal
        if (!string.IsNullOrWhiteSpace(servicePrincipal.ClientSecretKeyVaultReference))
        {
            try
            {
                var referenceUri = new Uri(servicePrincipal.ClientSecretKeyVaultReference);
                var segments = referenceUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

                if (segments.Length >= 2 && string.Equals(segments[0], "secrets", StringComparison.OrdinalIgnoreCase))
                {
                    var secretName = segments[1];
                    var secret = await _secretClient.GetSecretAsync(secretName, cancellationToken: cancellationToken).ConfigureAwait(false);
                    return secret.Value.Value;
                }

                _logger.LogWarning(
                    "ClientSecretKeyVaultReference on ServicePrincipal {ServicePrincipalId} did not match expected format: {Reference}",
                    servicePrincipal.Id,
                    servicePrincipal.ClientSecretKeyVaultReference);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to resolve ServicePrincipal secret from ClientSecretKeyVaultReference for {ServicePrincipalId}",
                    servicePrincipal.Id);
            }
        }

        throw new InvalidOperationException(
            $"ServicePrincipal {servicePrincipal.Id} is missing ClientSecretKeyVaultReference; store a Key Vault reference before querying Azure Resource Graph.");
    }

    private async Task<TenantResource> ResolveTenantAsync(ArmClient armClient, CancellationToken cancellationToken)
    {
        await foreach (var tenant in armClient.GetTenants().GetAllAsync(cancellationToken).ConfigureAwait(false))
        {
            return tenant;
        }

        throw new InvalidOperationException("Unable to resolve TenantResource for the current credentials.");
    }

    private ArmClient CreateArmClient(
        TokenCredential credential,
        AzureCloudEnvironment cloudEnvironment)
    {
        var options = new ArmClientOptions
        {
            Environment = cloudEnvironment switch
            {
                AzureCloudEnvironment.AzurePublicCloud => ArmEnvironment.AzurePublicCloud,
                AzureCloudEnvironment.AzureChinaCloud => ArmEnvironment.AzureChina,
                AzureCloudEnvironment.AzureUSGovernment => ArmEnvironment.AzureGovernment,
                AzureCloudEnvironment.AzureGermanyCloud => ArmEnvironment.AzureGermany,
                _ => ArmEnvironment.AzurePublicCloud
            }
        };

        return new ArmClient(credential, defaultSubscriptionId: null, options);
    }

    private IReadOnlyList<Resource> MapToResources(
        ResourceQueryResult response,
        ServicePrincipal servicePrincipal,
        string resourceType,
        Action<JsonElement, Dictionary<string, string>>? metadataEnricher)
    {
        if (response.Data is null)
        {
            return Array.Empty<Resource>();
        }

        using var document = JsonDocument.Parse(response.Data.ToString());

        if (document.RootElement.ValueKind == JsonValueKind.Object &&
            document.RootElement.TryGetProperty("data", out var dataElement) &&
            dataElement.ValueKind == JsonValueKind.Array)
        {
            return MapResourceArray(dataElement, servicePrincipal, resourceType, metadataEnricher);
        }

        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            return MapResourceArray(document.RootElement, servicePrincipal, resourceType, metadataEnricher);
        }

        _logger.LogWarning("Unexpected Azure Resource Graph response format - expected JSON array");
        return Array.Empty<Resource>();
    }

    private IReadOnlyList<Resource> MapResourceArray(
        JsonElement arrayElement,
        ServicePrincipal servicePrincipal,
        string resourceType,
        Action<JsonElement, Dictionary<string, string>>? metadataEnricher)
    {
        var results = new List<Resource>();

        foreach (var element in arrayElement.EnumerateArray())
        {
            try
            {
                var id = element.GetProperty("id").GetString() ?? string.Empty;
                var normalizedId = ResourceIdNormalizer.Normalize(id);
                var name = element.GetProperty("name").GetString() ?? string.Empty;
                var resourceGroup = element.TryGetProperty("resourceGroup", out var rgEl) ? rgEl.GetString() ?? string.Empty : string.Empty;
                var location = element.TryGetProperty("location", out var locEl) ? locEl.GetString() ?? string.Empty : string.Empty;
                var kind = element.TryGetProperty("kind", out var kindEl) ? kindEl.GetString() ?? string.Empty : string.Empty;

                var metadata = new Dictionary<string, string>
                {
                    ["resourceGroup"] = resourceGroup,
                    ["location"] = location,
                    ["kind"] = kind
                };

                if (element.TryGetProperty("tags", out var tagsElement) && tagsElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var tagProperty in tagsElement.EnumerateObject())
                    {
                        metadata[$"tag:{tagProperty.Name}"] = tagProperty.Value.GetString() ?? string.Empty;
                    }
                }

                metadataEnricher?.Invoke(element, metadata);

                var resource = new Resource
                {
                    CustomerId = servicePrincipal.CustomerId,
                    ServicePrincipalId = servicePrincipal.Id,
                    Name = name,
                    ResourceId = normalizedId,
                    ResourceType = resourceType,
                    Metadata = metadata
                };

                results.Add(resource);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to map Azure Resource Graph result item to Resource model");
            }
        }

        return results;
    }

    private static void AddVirtualMachineMetadata(JsonElement element, Dictionary<string, string> metadata)
    {
        AddMetadataIfPresent(element, "vmSize", metadata, "vmSize");
        AddMetadataIfPresent(element, "osType", metadata, "osType");
    }

    private static void AddMySqlMetadata(JsonElement element, Dictionary<string, string> metadata)
    {
        AddMetadataIfPresent(element, "skuName", metadata, "skuName");
        AddMetadataIfPresent(element, "engineVersion", metadata, "engineVersion");
    }

    private static void AddServiceBusMetadata(JsonElement element, Dictionary<string, string> metadata)
    {
        AddMetadataIfPresent(element, "skuName", metadata, "skuName");
        AddMetadataIfPresent(element, "capacity", metadata, "capacity");
    }

    private static void AddMetadataIfPresent(
        JsonElement element,
        string propertyName,
        Dictionary<string, string> metadata,
        string metadataKey)
    {
        if (element.TryGetProperty(propertyName, out var value) &&
            value.ValueKind != JsonValueKind.Null &&
            value.ValueKind != JsonValueKind.Undefined)
        {
            metadata[metadataKey] = value.GetString() ?? string.Empty;
        }
    }
}
