using System;
using System.Collections.Generic;
using System.Linq;
using Helios365.Core.Models;
using Helios365.Core.Repositories;

namespace Helios365.Core.Services;

/// <summary>
/// Generates on-call schedule slices from reusable plans + team bindings.
/// A concrete implementation should handle time zones/DST and rotation math.
/// </summary>
public interface IOnCallScheduleGenerator
{
    Task<IReadOnlyList<ScheduleSlice>> GenerateAsync(
        OnCallPlan plan,
        CustomerPlanBinding binding,
        OnCallTeam onHoursTeam,
        OnCallTeam offHoursTeam,
        OnCallTeam backupTeam,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Orchestrates schedule generation, persistence, and lookup of current coverage.
/// </summary>
public interface IOnCallScheduleService
{
    Task RegenerateAsync(string customerId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);
    Task<ScheduleSlice?> GetCurrentAsync(string customerId, DateTime utcNow, CancellationToken cancellationToken = default);
}

/// <summary>
/// Base implementation outline; concrete class should be registered with DI.
/// </summary>
public class OnCallScheduleService : IOnCallScheduleService
{
    private readonly IOnCallPlanRepository planRepository;
    private readonly IOnCallTeamRepository teamRepository;
    private readonly ICustomerPlanBindingRepository bindingRepository;
    private readonly IScheduleSliceRepository sliceRepository;
    private readonly IOnCallScheduleGenerator generator;

    public OnCallScheduleService(
        IOnCallPlanRepository planRepository,
        IOnCallTeamRepository teamRepository,
        ICustomerPlanBindingRepository bindingRepository,
        IScheduleSliceRepository sliceRepository,
        IOnCallScheduleGenerator generator)
    {
        this.planRepository = planRepository;
        this.teamRepository = teamRepository;
        this.bindingRepository = bindingRepository;
        this.sliceRepository = sliceRepository;
        this.generator = generator;
    }

    public async Task RegenerateAsync(string customerId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        var binding = await bindingRepository.GetAsync(customerId, cancellationToken) ?? throw new InvalidOperationException($"No binding for customer {customerId}");
        var plan = await planRepository.GetAsync(binding.PlanDefinitionId, cancellationToken) ?? throw new InvalidOperationException($"Plan {binding.PlanDefinitionId} not found");

        var onTeam = await GetTeamAsync(binding.OnHoursTeamId, cancellationToken);
        var offTeam = await GetTeamAsync(binding.OffHoursTeamId, cancellationToken);
        var backupTeam = await GetTeamAsync(binding.BackupTeamId, cancellationToken);

        // Drop future slices and regenerate forward
        await sliceRepository.DeleteFutureAsync(customerId, fromUtc, cancellationToken);

        var slices = await generator.GenerateAsync(plan, binding, onTeam, offTeam, backupTeam, fromUtc, toUtc, cancellationToken);
        await sliceRepository.UpsertManyAsync(slices, cancellationToken);
    }

    public async Task<ScheduleSlice?> GetCurrentAsync(string customerId, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        // Use slices if present; otherwise lazily regenerate a small horizon
        var slices = await sliceRepository.ListAsync(customerId, utcNow.AddHours(-1), utcNow.AddHours(1), limit: 10, cancellationToken);
        var current = slices.FirstOrDefault(s => s.StartUtc <= utcNow && s.EndUtc > utcNow);
        return current;
    }

    private async Task<OnCallTeam> GetTeamAsync(string id, CancellationToken cancellationToken)
    {
        var team = await teamRepository.GetAsync(id, cancellationToken);
        return team ?? throw new InvalidOperationException($"Team {id} not found");
    }
}

/// <summary>
/// Default generator implementation; handles per-day windows, rotation, and local-time conversion.
/// Assumes OnHours windows do not cross midnight; adjust if you need overnight spans.
/// </summary>
public class OnCallScheduleGenerator : IOnCallScheduleGenerator
{
    public Task<IReadOnlyList<ScheduleSlice>> GenerateAsync(
        OnCallPlan plan,
        CustomerPlanBinding binding,
        OnCallTeam onHoursTeam,
        OnCallTeam offHoursTeam,
        OnCallTeam backupTeam,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        if (fromUtc >= toUtc) throw new ArgumentException("fromUtc must be before toUtc", nameof(fromUtc));

        var tz = ResolveTimeZone(plan.TimeZone);
        var slices = new List<ScheduleSlice>();

        var startLocal = TimeZoneInfo.ConvertTimeFromUtc(fromUtc, tz);
        var endLocal = TimeZoneInfo.ConvertTimeFromUtc(toUtc, tz);

        var teamMap = new Dictionary<string, OnCallTeam>(StringComparer.OrdinalIgnoreCase)
        {
            [onHoursTeam.Id] = onHoursTeam,
            [offHoursTeam.Id] = offHoursTeam,
            [backupTeam.Id] = backupTeam
        };

        for (var date = startLocal.Date; date <= endLocal.Date; date = date.AddDays(1))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var overrideForDay = ResolveOverride(plan, binding, date);
            if (IsHoliday(plan, date))
            {
                continue;
            }
            if (overrideForDay?.Skip == true)
            {
                continue;
            }

            var onTeam = ResolveTeam(teamMap, overrideForDay?.OnHoursTeamId ?? binding.OnHoursTeamId, onHoursTeam);
            var offTeam = ResolveTeam(teamMap, overrideForDay?.OffHoursTeamId ?? binding.OffHoursTeamId, offHoursTeam);
            var backup = ResolveTeam(teamMap, overrideForDay?.BackupTeamId ?? binding.BackupTeamId, backupTeam);

            var onWindows = plan.OnHours.Where(w => w.Day == date.DayOfWeek).OrderBy(w => w.Start).ToList();
            var onIntervals = BuildIntervals(date, onWindows);
            var offIntervals = BuildOffHours(date, onIntervals);

            foreach (var interval in onIntervals)
            {
                var slice = CreateSlice(plan, binding.CustomerId, "OnHours", onTeam, backup, date, interval, tz, fromUtc, toUtc);
                if (slice != null) slices.Add(slice);
            }

            foreach (var interval in offIntervals)
            {
                var offSlice = CreateSlice(plan, binding.CustomerId, "OffHours", offTeam, backup, date, interval, tz, fromUtc, toUtc);
                if (offSlice != null) slices.Add(offSlice);
            }

            // Backup always on, 24/7 per day.
            var fullDay = (startLocal: date, endLocal: date.AddDays(1));
            var dailyBackup = CreateSlice(plan, binding.CustomerId, "Backup", backup, null, date, fullDay, tz, fromUtc, toUtc);
            if (dailyBackup != null) slices.Add(dailyBackup);
        }

        return Task.FromResult<IReadOnlyList<ScheduleSlice>>(slices
            .OrderBy(s => s.StartUtc)
            .ThenBy(s => s.Role)
            .ToList());
    }

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static PlanOverride? ResolveOverride(OnCallPlan plan, CustomerPlanBinding binding, DateTime date)
    {
        var planOverride = plan.Overrides.LastOrDefault(o => o.Date == DateOnly.FromDateTime(date));
        var customerOverride = binding.CustomerOverrides.LastOrDefault(o => o.Date == DateOnly.FromDateTime(date));
        return customerOverride ?? planOverride;
    }

    private static bool IsHoliday(OnCallPlan plan, DateTime date)
    {
        if (plan.Holidays.Any(h => h == DateOnly.FromDateTime(date)))
        {
            return true;
        }

        return false;
    }

    private static OnCallTeam ResolveTeam(Dictionary<string, OnCallTeam> map, string requestedId, OnCallTeam fallback)
    {
        if (map.TryGetValue(requestedId, out var team))
        {
            return team;
        }

        return fallback;
    }

    private static List<(DateTime startLocal, DateTime endLocal)> BuildIntervals(DateTime date, IEnumerable<DailyWindow> windows)
    {
        var intervals = new List<(DateTime, DateTime)>();
        foreach (var window in windows)
        {
            var start = date.Add(window.Start);
            var end = date.Add(window.End);

            if (end <= start)
            {
                // Treat as overnight into next day
                end = end.AddDays(1);
            }

            intervals.Add((start, end));
        }
        return intervals;
    }

    private static List<(DateTime startLocal, DateTime endLocal)> BuildOffHours(DateTime date, List<(DateTime startLocal, DateTime endLocal)> onIntervals)
    {
        var offIntervals = new List<(DateTime, DateTime)>();
        var dayStart = date;
        var dayEnd = date.AddDays(1);

        if (!onIntervals.Any())
        {
            offIntervals.Add((dayStart, dayEnd));
            return offIntervals;
        }

        var sorted = onIntervals
            .Select(i => (startLocal: i.startLocal < dayStart ? dayStart : i.startLocal,
                          endLocal: i.endLocal > dayEnd ? dayEnd : i.endLocal))
            .Where(i => i.endLocal > i.startLocal)
            .OrderBy(i => i.startLocal)
            .ToList();

        if (sorted.First().startLocal > dayStart)
        {
            offIntervals.Add((dayStart, sorted.First().startLocal));
        }

        for (var i = 0; i < sorted.Count - 1; i++)
        {
            var currentEnd = sorted[i].endLocal;
            var nextStart = sorted[i + 1].startLocal;
            if (nextStart > currentEnd)
            {
                offIntervals.Add((currentEnd, nextStart));
            }
        }

        if (sorted.Last().endLocal < dayEnd)
        {
            offIntervals.Add((sorted.Last().endLocal, dayEnd));
        }

        return offIntervals;
    }

    private ScheduleSlice? CreateSlice(
        OnCallPlan plan,
        string customerId,
        string role,
        OnCallTeam team,
        OnCallTeam? fallbackTeam,
        DateTime localDate,
        (DateTime startLocal, DateTime endLocal) interval,
        TimeZoneInfo tz,
        DateTime windowStartUtc,
        DateTime windowEndUtc)
    {
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(interval.startLocal, tz);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(interval.endLocal, tz);

        if (endUtc <= startUtc)
        {
            return null;
        }

        // Trim to requested window
        if (endUtc <= windowStartUtc || startUtc >= windowEndUtc)
        {
            return null;
        }

        startUtc = startUtc < windowStartUtc ? windowStartUtc : startUtc;
        endUtc = endUtc > windowEndUtc ? windowEndUtc : endUtc;

        var memberIds = ResolveMembers(team, plan.Rotation, localDate);
        if (!memberIds.Any() && fallbackTeam is not null)
        {
            var fallbackMembers = ResolveMembers(fallbackTeam, plan.Rotation, localDate);
            if (fallbackMembers.Any())
            {
                team = fallbackTeam;
                memberIds = fallbackMembers;
            }
        }

        return new ScheduleSlice
        {
            CustomerId = customerId,
            PlanDefinitionId = plan.Id,
            PlanVersion = plan.Version,
            Role = role,
            TeamId = team.Id,
            MemberIds = memberIds,
            StartUtc = startUtc,
            EndUtc = endUtc,
            GeneratedAtUtc = DateTime.UtcNow
        };
    }

    private static IReadOnlyList<string> ResolveMembers(OnCallTeam team, RotationDefaults rotation, DateTime localDate)
    {
        var enabled = team.Members
            .Where(m => m.Enabled)
            .OrderBy(m => m.Order)
            .ThenBy(m => m.UserId)
            .ToList();

        if (enabled.Count == 0)
        {
            return Array.Empty<string>();
        }

        var mode = team.ModeOverride ?? rotation.Mode;
        if (mode == RotationMode.WholeTeam)
        {
            return enabled.Select(m => m.UserId).ToList();
        }

        var cadence = team.CadenceOverride ?? rotation.Cadence;
        var anchorDate = rotation.AnchorDate ?? DateOnly.FromDateTime(localDate);
        var anchorIndex = rotation.AnchorIndex < 0 ? 0 : rotation.AnchorIndex;
        var intervalDays = team.RotationIntervalDays.HasValue && team.RotationIntervalDays.Value > 0
            ? team.RotationIntervalDays.Value
            : cadence switch
            {
                RotationCadence.Weekly => 7,
                _ => 1
            };

        var deltaDays = localDate.Date - anchorDate.ToDateTime(TimeOnly.MinValue);
        var increments = (int)Math.Floor(deltaDays.TotalDays / intervalDays);

        var idx = enabled.Count == 0 ? 0 : (anchorIndex + increments) % enabled.Count;
        if (idx < 0) idx += enabled.Count;

        return enabled.Count == 0 ? Array.Empty<string>() : new[] { enabled[idx].UserId };
    }
}
