using System.Text.Json.Serialization;

namespace Helios365.Core.Models;

public enum RemediationAction
{
    Restart,
    ScaleUp,
    ScaleDown,
    Failover,
    RunScript,
    Webhook
}

public class RemediationRule
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("customerId")]
    public string CustomerId { get; set; } = string.Empty;

    [JsonPropertyName("resourceType")]
    public string ResourceType { get; set; } = string.Empty;

    [JsonPropertyName("alertType")]
    public string AlertType { get; set; } = string.Empty;

    [JsonPropertyName("action")]
    public RemediationAction Action { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("maxAttempts")]
    public int MaxAttempts { get; set; } = 3;

    [JsonPropertyName("waitTimeSeconds")]
    public int WaitTimeSeconds { get; set; } = 300;

    [JsonPropertyName("scriptUrl")]
    public string? ScriptUrl { get; set; }

    [JsonPropertyName("webhookUrl")]
    public string? WebhookUrl { get; set; }

    [JsonPropertyName("parameters")]
    public Dictionary<string, string> Parameters { get; set; } = new();
}
