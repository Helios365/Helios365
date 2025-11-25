using Newtonsoft.Json;

namespace Helios365.Core.Models;

public sealed class MetricPoint
{
    [JsonProperty("timestamp")]
    public DateTime Timestamp { get; init; }

    [JsonProperty("value")]
    public double? Value { get; init; }
}

public sealed class MetricSeries
{
    [JsonProperty("name")]
    public string Name { get; init; } = string.Empty;

    [JsonProperty("unit")]
    public string Unit { get; init; } = string.Empty;

    [JsonProperty("points")]
    public IReadOnlyList<MetricPoint> Points { get; init; } = Array.Empty<MetricPoint>();
}

public sealed class MetricsResult
{
    [JsonProperty("resourceId")]
    public string ResourceId { get; init; } = string.Empty;

    [JsonProperty("resourceType")]
    public string ResourceType { get; init; } = string.Empty;

    [JsonProperty("series")]
    public IReadOnlyList<MetricSeries> Series { get; init; } = Array.Empty<MetricSeries>();
}
