using Newtonsoft.Json;

namespace Helios365.Core.Models;

public class WebTest
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("resourceId")]
    public string? ResourceId { get; set; }

    [JsonProperty("customerId")]
    public string CustomerId { get; set; } = string.Empty;

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

    [JsonProperty("lastRunAt")]
    public DateTime? LastRunAt { get; set; }

    [JsonProperty("lastSucceeded")]
    public bool? LastSucceeded { get; set; }

    [JsonProperty("lastStatusCode")]
    public int? LastStatusCode { get; set; }

    [JsonProperty("lastError")]
    public string? LastError { get; set; }
}

public sealed class WebTestResult
{
    [JsonProperty("succeeded")]
    public bool Succeeded { get; init; }

    [JsonProperty("statusCode")]
    public int? StatusCode { get; init; }

    [JsonProperty("error")]
    public string? Error { get; init; }

    [JsonProperty("durationMs")]
    public long DurationMs { get; init; }

    [JsonProperty("checkedAt")]
    public DateTime CheckedAt { get; init; } = DateTime.UtcNow;
}
