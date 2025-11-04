using System.Text.Json.Serialization;

namespace Helios365.Core.Models;

public enum AzureCloudEnvironment
{
    AzurePublicCloud,
    AzureChinaCloud,
    AzureUSGovernment,
    AzureGermanyCloud
}

public class ServicePrincipal
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("customerId")]
    public string CustomerId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("tenantId")]
    public string TenantId { get; set; } = string.Empty;

    [JsonPropertyName("clientId")]
    public string ClientId { get; set; } = string.Empty;

    [JsonPropertyName("clientSecretKeyVaultReference")]
    public string ClientSecretKeyVaultReference { get; set; } = string.Empty;

    [JsonPropertyName("cloudEnvironment")]
    public AzureCloudEnvironment CloudEnvironment { get; set; } = AzureCloudEnvironment.AzurePublicCloud;

    [JsonPropertyName("active")]
    public bool Active { get; set; } = true;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();
}
