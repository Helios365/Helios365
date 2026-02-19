using System.Globalization;
using Helios365.Core.Models;

namespace Helios365.Web.Helpers;

public static class OnCallDisplayHelper
{
    public static string FormatOnHours(OnCallPlan plan)
    {
        if (plan.TimeSlots.Any())
        {
            var slotStrings = plan.TimeSlots.OrderBy(s => s.Index)
                .Select(s => $"{s.Label} {s.Start:hh\\:mm}-{s.End:hh\\:mm}");
            var slots = string.Join(", ", slotStrings);
            return plan.IncludeWeekends ? $"{slots} (all days)" : $"{slots} (weekdays)";
        }

        var window = plan.OnHours.FirstOrDefault();
        if (window == null) return "-";
        var span = $"{window.Start:hh\\:mm}-{window.End:hh\\:mm}";
        return plan.IncludeWeekends ? $"{span} (all days)" : $"{span} (weekdays)";
    }

    public static int RoleRank(string role) => role switch
    {
        "OnHours" => 0,
        "OffHours" => 1,
        "Backup" => 2,
        _ => 3
    };

    public static class RoleColor
    {
        public const string On = "#0d6efd";
        public const string Off = "#6c757d";
        public const string Backup = "#f6c344";

        public static string For(string role) => role switch
        {
            "OnHours" => On,
            "OffHours" => Off,
            "Backup" => Backup,
            _ => "#adb5bd"
        };
    }

    public static string RoleTextColor(string role) => role switch
    {
        "Backup" => "#000",
        _ => "#fff"
    };

    public static int RoleOffset(string role) => role switch
    {
        "OnHours" => 4,
        "OffHours" => 20,
        "Backup" => 36,
        _ => 4
    };

    public static string FormatSliceRange(DateTime startLocal, DateTime endLocal) =>
        $"{startLocal:yyyy-MM-dd HH:mm}-{endLocal:HH:mm}";

    public static string GetTimelineStyle(DateTime startLocal, DateTime endLocal, string role, DateOnly date)
    {
        var dayStart = date.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);

        if (endLocal <= dayStart || startLocal >= dayEnd) return "left:0;width:0;";

        var startMinutes = Math.Max(0, (startLocal - dayStart).TotalMinutes);
        var endMinutes = Math.Min(1440, (endLocal - dayStart).TotalMinutes);
        var widthMinutes = Math.Max(2, endMinutes - startMinutes);

        var leftPct = startMinutes / 1440d * 100d;
        var widthPct = widthMinutes / 1440d * 100d;

        var top = RoleOffset(role);
        return string.Format(
            CultureInfo.InvariantCulture,
            "left:{0:F2}%;width:{1:F2}%;min-width:12px;max-width:100%;top:{2}px;",
            leftPct,
            widthPct,
            top);
    }

    public static IReadOnlyList<DailyWindow> BuildOnHoursFromSlots(
        List<(int Index, string Label, TimeSpan Start, TimeSpan End)> slots, bool includeWeekends)
    {
        var days = Enum.GetValues<DayOfWeek>().Where(d => includeWeekends || (d != DayOfWeek.Saturday && d != DayOfWeek.Sunday));
        return days
            .SelectMany(d => slots.Select(s => new DailyWindow(d, s.Start, s.End, s.Index)))
            .ToList();
    }

    public static IReadOnlyList<DateOnly> ParseHolidays(string holidays)
    {
        if (string.IsNullOrWhiteSpace(holidays)) return Array.Empty<DateOnly>();

        var result = new List<DateOnly>();
        foreach (var value in holidays.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (DateOnly.TryParse(value, out var date))
            {
                result.Add(date);
            }
        }
        return result;
    }

    public static bool TryParseTime(string value, out TimeSpan time)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            time = default;
            return false;
        }

        return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out time);
    }
}
