using Newtonsoft.Json;

namespace Helios365.Core.Models;

public enum AzureCloudEnvironment
{
    AzurePublicCloud,
    AzureChinaCloud,
    AzureUSGovernment
}

public class ServicePrincipal
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("customerId")]
    public string CustomerId { get; set; } = string.Empty;

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("tenantId")]
    public string TenantId { get; set; } = string.Empty;

    [JsonProperty("clientId")]
    public string ClientId { get; set; } = string.Empty;

    [JsonProperty("clientSecretKeyVaultReference")]
    public string ClientSecretKeyVaultReference { get; set; } = string.Empty;

    [JsonProperty("cloudEnvironment")]
    public AzureCloudEnvironment CloudEnvironment { get; set; } = AzureCloudEnvironment.AzurePublicCloud;

    [JsonProperty("active")]
    public bool Active { get; set; } = true;

    [JsonProperty("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonProperty("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonProperty("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();
}
