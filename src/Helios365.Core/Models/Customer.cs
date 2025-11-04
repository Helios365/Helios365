using System.Text.Json.Serialization;

namespace Helios365.Core.Models;

public class CustomerConfig
{
    [JsonPropertyName("tenantId")]
    public string TenantId { get; set; } = string.Empty;

    [JsonPropertyName("subscriptionIds")]
    public List<string> SubscriptionIds { get; set; } = new();

    [JsonPropertyName("helios365Endpoint")]
    public string? Helios365Endpoint { get; set; }

    [JsonPropertyName("autoRemediationEnabled")]
    public bool AutoRemediationEnabled { get; set; }

    [JsonPropertyName("notificationEmails")]
    public List<string> NotificationEmails { get; set; } = new();

    [JsonPropertyName("escalationTimeoutMinutes")]
    public int EscalationTimeoutMinutes { get; set; } = 5;

    [JsonPropertyName("healthCheckTimeoutSeconds")]
    public int HealthCheckTimeoutSeconds { get; set; } = 30;
}

public class Customer
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("config")]
    public CustomerConfig Config { get; set; } = new();

    [JsonPropertyName("active")]
    public bool Active { get; set; } = true;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();
}
