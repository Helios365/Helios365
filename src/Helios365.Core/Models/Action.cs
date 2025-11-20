using Newtonsoft.Json;

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
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("resourceId")]
    public string? ResourceId { get; set; }

    [JsonProperty("customerId")]
    public string CustomerId { get; set; } = string.Empty;

    [JsonProperty("type")]
    public ActionType Type { get; set; }

    [JsonProperty("mode")]
    public ActionMode Mode { get; set; } = ActionMode.Manual;

    [JsonProperty("order")]
    public int Order { get; set; }

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonProperty("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool IsDefaultAction() => string.IsNullOrEmpty(ResourceId);
}

public class HealthCheckAction : ActionBase
{
    [JsonProperty("url")]
    public string Url { get; set; } = string.Empty;

    [JsonProperty("method")]
    public HttpMethod Method { get; set; } = HttpMethod.GET;

    [JsonProperty("expectedStatusCode")]
    public int ExpectedStatusCode { get; set; } = 200;

    [JsonProperty("timeoutSeconds")]
    public int TimeoutSeconds { get; set; } = 30;

    [JsonProperty("headers")]
    public Dictionary<string, string> Headers { get; set; } = new();

    public HealthCheckAction()
    {
        Type = ActionType.HealthCheck;
    }
}

public class RestartAction : ActionBase
{
    [JsonProperty("waitBeforeSeconds")]
    public int WaitBeforeSeconds { get; set; } = 0;

    [JsonProperty("waitAfterSeconds")]
    public int WaitAfterSeconds { get; set; } = 300;

    public RestartAction()
    {
        Type = ActionType.Restart;
    }
}

public class ScaleAction : ActionBase
{
    [JsonProperty("direction")]
    public ScaleDirection Direction { get; set; } = ScaleDirection.Up;

    [JsonProperty("targetInstanceCount")]
    public int? TargetInstanceCount { get; set; }

    [JsonProperty("targetSku")]
    public string? TargetSku { get; set; }

    public ScaleAction()
    {
        Type = ActionType.Scale;
    }
}