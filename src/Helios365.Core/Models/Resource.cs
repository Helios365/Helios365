using Newtonsoft.Json;

namespace Helios365.Core.Models;

public class Resource
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("customerId")]
    public string CustomerId { get; set; } = string.Empty;

    [JsonProperty("servicePrincipalId")]
    public string ServicePrincipalId { get; set; } = string.Empty;

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("resourceId")]
    public string ResourceId { get; set; } = string.Empty;

    [JsonProperty("resourceType")]
    public string ResourceType { get; set; } = string.Empty;

    [JsonProperty("useDefaultActions")]
    public bool UseDefaultActions { get; set; } = true;

    [JsonProperty("active")]
    public bool Active { get; set; } = true;

    [JsonProperty("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonProperty("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonProperty("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();
}
