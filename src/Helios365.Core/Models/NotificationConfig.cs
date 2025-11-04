using System.Text.Json.Serialization;

namespace Helios365.Core.Models;

public enum NotificationChannel
{
    Email,
    Sms,
    Slack,
    Teams,
    PagerDuty,
    Webhook
}

public class NotificationConfig
{
    [JsonPropertyName("channel")]
    public NotificationChannel Channel { get; set; }

    [JsonPropertyName("recipients")]
    public List<string> Recipients { get; set; } = new();

    [JsonPropertyName("webhookUrl")]
    public string? WebhookUrl { get; set; }

    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; set; }

    [JsonPropertyName("template")]
    public string? Template { get; set; }

    [JsonPropertyName("priority")]
    public string Priority { get; set; } = "high";
}
