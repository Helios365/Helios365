using System.Text.Json.Serialization;

namespace Helios365.Core.Models;

public enum AlertStatus
{
    Received,
    Routing,
    Checking,
    Healthy,
    Remediating,
    Rechecking,
    Escalated,
    Resolved,
    Failed
}

public enum AlertSeverity
{
    Critical,
    High,
    Medium,
    Low,
    Info
}

public class Alert
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("customerId")]
    public string CustomerId { get; set; } = string.Empty;

    [JsonPropertyName("resourceId")]
    public string ResourceId { get; set; } = string.Empty;

    [JsonPropertyName("resourceType")]
    public string ResourceType { get; set; } = string.Empty;

    [JsonPropertyName("alertType")]
    public string AlertType { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public AlertStatus Status { get; set; } = AlertStatus.Received;

    [JsonPropertyName("severity")]
    public AlertSeverity Severity { get; set; } = AlertSeverity.Medium;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("healthCheckUrl")]
    public string? HealthCheckUrl { get; set; }

    [JsonPropertyName("remediationAction")]
    public string? RemediationAction { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("resolvedAt")]
    public DateTime? ResolvedAt { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();

    public void MarkStatus(AlertStatus status)
    {
        Status = status;
        UpdatedAt = DateTime.UtcNow;

        if (status is AlertStatus.Resolved or AlertStatus.Healthy)
        {
            ResolvedAt = DateTime.UtcNow;
        }
    }

    public bool IsActive()
    {
        return Status is not (AlertStatus.Resolved or AlertStatus.Healthy or AlertStatus.Failed);
    }
}
