using Newtonsoft.Json;

namespace Helios365.Core.Contracts.Diagnostics;

public sealed class ResourceHealthResult
{
    [JsonProperty("resourceId")]
    public string ResourceId { get; init; } = string.Empty;

    [JsonProperty("resourceType")]
    public string ResourceType { get; init; } = string.Empty;

    [JsonProperty("availabilityState")]
    public string AvailabilityState { get; init; } = "Unknown";

    [JsonProperty("title")]
    public string? Title { get; init; }

    [JsonProperty("summary")]
    public string? Summary { get; init; }

    [JsonProperty("detailedStatus")]
    public string? DetailedStatus { get; init; }

    [JsonProperty("reasonType")]
    public string? ReasonType { get; init; }

    [JsonProperty("reasonChronicity")]
    public string? ReasonChronicity { get; init; }

    [JsonProperty("occuredOn")]
    public DateTimeOffset? OccuredOn { get; init; }

    [JsonProperty("reportedOn")]
    public DateTimeOffset? ReportedOn { get; init; }

    [JsonProperty("resolutionEta")]
    public DateTimeOffset? ResolutionEta { get; init; }

    [JsonProperty("recommendedActions")]
    public IReadOnlyList<HealthRecommendedAction> RecommendedActions { get; init; } = Array.Empty<HealthRecommendedAction>();
}

public sealed class HealthRecommendedAction
{
    [JsonProperty("action")]
    public string Action { get; init; } = string.Empty;

    [JsonProperty("actionUrl")]
    public string? ActionUrl { get; init; }
}
