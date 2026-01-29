using Newtonsoft.Json;

namespace Helios365.Core.Models;

public enum AlertStatus
{
    Pending,
    Escalated,
    Accepted,
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

    [JsonProperty("resourceName")]
    public string ResourceName { get; set; } = string.Empty;

    [JsonProperty("resourceType")]
    public string ResourceType { get; set; } = string.Empty;

    [JsonProperty("subscriptionName")]
    public string SubscriptionName { get; set; } = string.Empty;

    [JsonProperty("customerName")]
    public string CustomerName { get; set; } = string.Empty;

    [JsonProperty("alertType")]
    public string AlertType { get; set; } = string.Empty;

    [JsonProperty("status")]
    public AlertStatus Status { get; set; } = AlertStatus.Pending;

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

    [JsonProperty("escalationAttempts")]
    public int EscalationAttempts { get; set; } = 0;

    [JsonProperty("currentEscalationTarget")]
    public string? CurrentEscalationTarget { get; set; }

    [JsonProperty("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();

    [JsonProperty("changes")]
    public List<AlertChange> Changes { get; set; } = new();

    public void MarkStatus(AlertStatus status)
    {
        Status = status;
        UpdatedAt = DateTime.UtcNow;

        if (status is AlertStatus.Resolved)
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
        return Status is not (AlertStatus.Resolved or AlertStatus.Failed);
    }
}

public class AlertChange
{
    [JsonProperty("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonProperty("user")]
    public string User { get; set; } = "unknown";

    [JsonProperty("comment")]
    public string Comment { get; set; } = string.Empty;

    [JsonProperty("newStatus")]
    public AlertStatus? NewStatus { get; set; }

    [JsonProperty("newSeverity")]
    public AlertSeverity? NewSeverity { get; set; }

    [JsonProperty("previousStatus")]
    public AlertStatus? PreviousStatus { get; set; }

    [JsonProperty("previousSeverity")]
    public AlertSeverity? PreviousSeverity { get; set; }

    [JsonProperty("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
