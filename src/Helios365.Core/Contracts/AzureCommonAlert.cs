using System.Text.Json;
using System.Text.Json.Serialization;

namespace Helios365.Core.Contracts.Alerts;

public class AzureCommonAlert
{
    [JsonPropertyName("schemaId")]
    public string? SchemaId { get; set; }

    [JsonPropertyName("data")]
    public AzureCommonAlertData? Data { get; set; }
}

public class AzureCommonAlertData
{
    [JsonPropertyName("essentials")]
    public AzureAlertEssentials? Essentials { get; set; }

    [JsonPropertyName("alertContext")]
    public JsonElement? AlertContext { get; set; }
}

public class AzureAlertEssentials
{
    [JsonPropertyName("alertId")]
    public string? AlertId { get; set; }

    [JsonPropertyName("alertRule")]
    public string? AlertRule { get; set; }

    [JsonPropertyName("severity")]
    public string? Severity { get; set; }

    [JsonPropertyName("signalType")]
    public string? SignalType { get; set; }

    [JsonPropertyName("monitorCondition")]
    public string? MonitorCondition { get; set; }

    [JsonPropertyName("monitoringService")]
    public string? MonitoringService { get; set; }

    [JsonPropertyName("alertTargetIDs")]
    public List<string>? AlertTargetIds { get; set; }

    [JsonPropertyName("originAlertId")]
    public string? OriginAlertId { get; set; }

    [JsonPropertyName("firedDateTime")]
    public DateTime? FiredDateTime { get; set; }

    [JsonPropertyName("resolvedDateTime")]
    public DateTime? ResolvedDateTime { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("essentialsVersion")]
    public string? EssentialsVersion { get; set; }

    [JsonPropertyName("alertContextVersion")]
    public string? AlertContextVersion { get; set; }

    [JsonPropertyName("smartGroupId")]
    public string? SmartGroupId { get; set; }
}
