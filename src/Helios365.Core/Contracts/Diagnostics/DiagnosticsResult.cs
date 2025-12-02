using Newtonsoft.Json;

namespace Helios365.Core.Contracts.Diagnostics;

public sealed class DiagnosticsResult
{
    [JsonProperty("resourceId")]
    public string ResourceId { get; set; } = string.Empty;

    [JsonProperty("resourceType")]
    public string ResourceType { get; set; } = string.Empty;

    [JsonProperty("data")]
    public Dictionary<string, string> Data { get; set; } = new();
}
