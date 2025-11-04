using System.Text.Json.Serialization;

namespace Helios365.Core.Models;

public enum HealthCheckType
{
    HttpGet,
    HttpPost,
    TcpPort,
    AzureResourceStatus
}

public class HealthCheckConfig
{
    [JsonPropertyName("type")]
    public HealthCheckType Type { get; set; }

    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; set; }

    [JsonPropertyName("expectedStatusCode")]
    public int ExpectedStatusCode { get; set; } = 200;

    [JsonPropertyName("timeoutSeconds")]
    public int TimeoutSeconds { get; set; } = 30;

    [JsonPropertyName("headers")]
    public Dictionary<string, string> Headers { get; set; } = new();

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("tcpPort")]
    public int? TcpPort { get; set; }
}
