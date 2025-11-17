using Newtonsoft.Json;

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
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("customerId")]
    public string CustomerId { get; set; } = string.Empty;

    [JsonProperty("resourceId")]
    public string ResourceId { get; set; } = string.Empty;

    [JsonProperty("resourceType")]
    public string ResourceType { get; set; } = string.Empty;

    [JsonProperty("alertType")]
    public string AlertType { get; set; } = string.Empty;

    [JsonProperty("status")]
    public AlertStatus Status { get; set; } = AlertStatus.Received;

    [JsonProperty("severity")]
    public AlertSeverity Severity { get; set; } = AlertSeverity.Medium;

    [JsonProperty("title")]
    public string? Title { get; set; }

    [JsonProperty("description")]
    public string? Description { get; set; }

    [JsonProperty("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonProperty("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonProperty("resolvedAt")]
    public DateTime? ResolvedAt { get; set; }

    [JsonProperty("escalatedAt")]
    public DateTime? EscalatedAt { get; set; }

    [JsonProperty("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();

    public void MarkStatus(AlertStatus status)
    {
        Status = status;
        UpdatedAt = DateTime.UtcNow;

        if (status is AlertStatus.Resolved or AlertStatus.Healthy)
        {
            ResolvedAt = DateTime.UtcNow;
        }

        if (status is AlertStatus.Escalated)
        {
            EscalatedAt = DateTime.UtcNow;
        }
    }

    public bool IsActive()
    {
        return Status is not (AlertStatus.Resolved or AlertStatus.Healthy or AlertStatus.Failed);
    }
}
