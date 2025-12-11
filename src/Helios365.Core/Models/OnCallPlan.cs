using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Helios365.Core.Models;

/// <summary>
/// Reusable on-call plan definition (time windows, rotation defaults, overrides).
/// Customers bind to a plan and choose which teams fill each role.
/// </summary>
public record OnCallPlan
{
    [JsonProperty("id")]
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    [JsonProperty("name")]
    public string Name { get; init; } = string.Empty;
    [JsonProperty("timeZone")]
    public string TimeZone { get; init; } = "UTC";
    [JsonProperty("onHours")]
    public IReadOnlyList<DailyWindow> OnHours { get; init; } = Array.Empty<DailyWindow>();
    [JsonProperty("includeWeekends")]
    public bool IncludeWeekends { get; init; }
    [JsonProperty("holidays")]
    public IReadOnlyList<DateOnly> Holidays { get; init; } = Array.Empty<DateOnly>();
    [JsonProperty("escalation")]
    public EscalationPolicy Escalation { get; init; } = new();
    [JsonProperty("rotation")]
    public RotationDefaults Rotation { get; init; } = new();
    [JsonProperty("overrides")]
    public IReadOnlyList<PlanOverride> Overrides { get; init; } = Array.Empty<PlanOverride>();
    [JsonProperty("version")]
    public string Version { get; init; } = "v1";
}

/// <summary>Local-time window for a given weekday.</summary>
public record DailyWindow(
    [property: JsonProperty("day")] DayOfWeek Day,
    [property: JsonProperty("start")] TimeSpan Start,
    [property: JsonProperty("end")] TimeSpan End);

public enum RotationMode
{
    RollingIndividual,
    WholeTeam
}

public enum RotationCadence
{
    Daily,
    Weekly
}

public record RotationDefaults(
    [property: JsonProperty("mode")] RotationMode Mode = RotationMode.RollingIndividual,
    [property: JsonProperty("cadence")] RotationCadence Cadence = RotationCadence.Daily,
    [property: JsonProperty("anchorDate")] DateOnly? AnchorDate = null,
    [property: JsonProperty("anchorIndex")] int AnchorIndex = 0);

public record EscalationPolicy(
    [property: JsonProperty("ackTimeout")] TimeSpan AckTimeout,
    [property: JsonProperty("maxRetries")] int MaxRetries,
    [property: JsonProperty("retryDelay")] TimeSpan RetryDelay)
{
    public EscalationPolicy() : this(TimeSpan.FromMinutes(5), 3, TimeSpan.FromMinutes(5))
    {
    }
}

/// <summary>Date-specific overrides for teams or skips.</summary>
public record PlanOverride(
    [property: JsonProperty("date")] DateOnly Date,
    [property: JsonProperty("onHoursTeamId")] string? OnHoursTeamId = null,
    [property: JsonProperty("offHoursTeamId")] string? OffHoursTeamId = null,
    [property: JsonProperty("backupTeamId")] string? BackupTeamId = null,
    [property: JsonProperty("skip")] bool? Skip = null);

/// <summary>Teams are reusable across customers.</summary>
public record OnCallTeam
{
    [JsonProperty("id")]
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    [JsonProperty("name")]
    public string Name { get; init; } = string.Empty;
    [JsonProperty("enabled")]
    public bool Enabled { get; init; } = true;
    [JsonProperty("modeOverride")]
    public RotationMode? ModeOverride { get; init; }
    [JsonProperty("cadenceOverride")]
    public RotationCadence? CadenceOverride { get; init; }
    [JsonProperty("members")]
    public IReadOnlyList<TeamMember> Members { get; init; } = Array.Empty<TeamMember>();
}

public record TeamMember(
    [property: JsonProperty("userId")] string UserId,
    [property: JsonProperty("enabled")] bool Enabled = true,
    [property: JsonProperty("order")] int Order = 0);

/// <summary>Binding of a customer to a reusable plan and specific teams for each role.</summary>
public record CustomerPlanBinding
{
    [JsonProperty("id")]
    public string Id { get; init; } = string.Empty;
    [JsonProperty("customerId")]
    public string CustomerId { get; init; } = string.Empty;
    [JsonProperty("planDefinitionId")]
    public string PlanDefinitionId { get; init; } = string.Empty;
    [JsonProperty("onHoursTeamId")]
    public string OnHoursTeamId { get; init; } = string.Empty;
    [JsonProperty("offHoursTeamId")]
    public string OffHoursTeamId { get; init; } = string.Empty;
    [JsonProperty("backupTeamId")]
    public string BackupTeamId { get; init; } = string.Empty;
    [JsonProperty("effectiveFrom")]
    public DateOnly EffectiveFrom { get; init; } = DateOnly.FromDateTime(DateTime.UtcNow.Date);
    [JsonProperty("effectiveThrough")]
    public DateOnly? EffectiveThrough { get; init; }
    [JsonProperty("customerOverrides")]
    public IReadOnlyList<PlanOverride> CustomerOverrides { get; init; } = Array.Empty<PlanOverride>();
}

/// <summary>
/// Materialized on-call slice used for reporting, SLA, and fast lookups.
/// Past slices are immutable; future slices are regenerated on plan/team changes.
/// </summary>
public record ScheduleSlice
{
    [JsonProperty("id")]
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    [JsonProperty("customerId")]
    public string CustomerId { get; init; } = string.Empty;
    [JsonProperty("planDefinitionId")]
    public string PlanDefinitionId { get; init; } = string.Empty;
    [JsonProperty("planVersion")]
    public string PlanVersion { get; init; } = string.Empty;
    [JsonProperty("role")]
    public string Role { get; init; } = string.Empty; // "OnHours" | "OffHours" | "Backup"
    [JsonProperty("teamId")]
    public string TeamId { get; init; } = string.Empty;
    [JsonProperty("memberIds")]
    public IReadOnlyList<string> MemberIds { get; init; } = Array.Empty<string>();
    [JsonProperty("startUtc")]
    public DateTime StartUtc { get; init; }
    [JsonProperty("endUtc")]
    public DateTime EndUtc { get; init; }
    [JsonProperty("generatedAtUtc")]
    public DateTime GeneratedAtUtc { get; init; } = DateTime.UtcNow;
}
