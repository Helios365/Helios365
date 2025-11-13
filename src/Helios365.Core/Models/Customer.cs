using Newtonsoft.Json;

namespace Helios365.Core.Models;

public class Customer
{
    [JsonProperty(PropertyName = "id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty(PropertyName = "name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty(PropertyName = "apiKey")]
    public string ApiKey { get; set; } = string.Empty;

    [JsonProperty(PropertyName = "notificationEmails")]
    public List<string> NotificationEmails { get; set; } = new();

    [JsonProperty(PropertyName = "escalationTimeoutMinutes")]
    public int EscalationTimeoutMinutes { get; set; } = 5;

    [JsonProperty(PropertyName = "active")]
    public bool Active { get; set; } = true;

    [JsonProperty(PropertyName = "createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonProperty(PropertyName = "updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonProperty(PropertyName = "metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();
}
