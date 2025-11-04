using System.Text.Json.Serialization;

namespace Helios365.Core.Models;

public enum ActionType
{
    HealthCheck,
    Restart,
    Scale
}

public enum ActionMode
{
    Manual,
    Automatic
}

public enum HttpMethod
{
    GET,
    POST
}

public enum ScaleDirection
{
    Up,
    Down
}

public abstract class ActionBase
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("resourceId")]
    public string? ResourceId { get; set; }

    [JsonPropertyName("customerId")]
    public string CustomerId { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public ActionType Type { get; set; }

    [JsonPropertyName("mode")]
    public ActionMode Mode { get; set; } = ActionMode.Manual;

    [JsonPropertyName("order")]
    public int Order { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool IsDefaultAction() => string.IsNullOrEmpty(ResourceId);
}

public class HealthCheckAction : ActionBase
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("method")]
    public HttpMethod Method { get; set; } = HttpMethod.GET;

    [JsonPropertyName("expectedStatusCode")]
    public int ExpectedStatusCode { get; set; } = 200;

    [JsonPropertyName("timeoutSeconds")]
    public int TimeoutSeconds { get; set; } = 30;

    [JsonPropertyName("headers")]
    public Dictionary<string, string> Headers { get; set; } = new();

    public HealthCheckAction()
    {
        Type = ActionType.HealthCheck;
    }
}

public class RestartAction : ActionBase
{
    [JsonPropertyName("waitBeforeSeconds")]
    public int WaitBeforeSeconds { get; set; } = 0;

    [JsonPropertyName("waitAfterSeconds")]
    public int WaitAfterSeconds { get; set; } = 300;

    public RestartAction()
    {
        Type = ActionType.Restart;
    }
}

public class ScaleAction : ActionBase
{
    [JsonPropertyName("direction")]
    public ScaleDirection Direction { get; set; } = ScaleDirection.Up;

    [JsonPropertyName("targetInstanceCount")]
    public int? TargetInstanceCount { get; set; }

    [JsonPropertyName("targetSku")]
    public string? TargetSku { get; set; }

    public ScaleAction()
    {
        Type = ActionType.Scale;
    }
}
