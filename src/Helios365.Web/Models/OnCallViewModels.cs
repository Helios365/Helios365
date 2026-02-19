using Helios365.Core.Models;

namespace Helios365.Web.Models;

public class OnCallSlotInput
{
    public string Label { get; set; } = string.Empty;
    public string Start { get; set; } = "08:00";
    public string End { get; set; } = "17:00";
}

public class OnCallPlanInput
{
    public string Name { get; set; } = string.Empty;
    public string TimeZone { get; set; } = "UTC";
    public List<OnCallSlotInput> Slots { get; set; } = new() { new OnCallSlotInput { Label = "On-hours" } };
    public bool IncludeWeekends { get; set; }
    public string Holidays { get; set; } = string.Empty;
}

public class OnCallTeamInput
{
    public string Name { get; set; } = string.Empty;
    public HashSet<string> SelectedUserIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string ManualMembers { get; set; } = string.Empty;
    public bool WholeTeam { get; set; }
    public int? RotationIntervalDays { get; set; }
}

public class OnCallBindingSlotInput
{
    public int SlotIndex { get; set; }
    public string Label { get; set; } = string.Empty;
    public string TimeRange { get; set; } = string.Empty;
    public string TeamId { get; set; } = string.Empty;
}

public class OnCallBindingInput
{
    public string CustomerId { get; set; } = string.Empty;
    public string PlanId { get; set; } = string.Empty;
    public string FallbackOnTeamId { get; set; } = string.Empty;
    public List<OnCallBindingSlotInput> SlotTeams { get; set; } = new();
    public string OffTeamId { get; set; } = string.Empty;
    public string BackupTeamId { get; set; } = string.Empty;
    public DateOnly EffectiveFrom { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow.Date);
}

public class ScheduleDayView
{
    public DateOnly Date { get; set; }
    public List<ScheduleSliceView> Slices { get; set; } = new();
}

public record ScheduleSliceView(ScheduleSlice Slice, DateTime StartLocal, DateTime EndLocal)
{
    public string Role => Slice.Role;
    public string TeamId => Slice.TeamId;
    public IReadOnlyList<string> MemberIds => Slice.MemberIds;
}
