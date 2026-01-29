using Helios365.Core.Models;
using Helios365.Core.Repositories;

namespace Helios365.Core.Services;

/// <summary>
/// Generates on-call schedule slices from reusable plans + team bindings.
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
/// Data transfer object for all on-call administration data.
/// </summary>
public record OnCallData(
    IReadOnlyList<OnCallPlan> Plans,
    IReadOnlyList<OnCallTeam> Teams,
    IReadOnlyList<CustomerPlanBinding> Bindings,
    IReadOnlyList<Customer> Customers,
    IReadOnlyList<User> Users);

/// <summary>
/// Current on-call coverage including primary (on/off hours) and backup.
/// </summary>
public record CurrentCoverage(
    ScheduleSlice? PrimarySlice,
    ScheduleSlice? BackupSlice)
{
    public bool HasCoverage => PrimarySlice != null || BackupSlice != null;
}

/// <summary>
/// Result of a schedule query including slices and display context.
/// </summary>
public record ScheduleQueryResult(
    IReadOnlyList<ScheduleSlice> Slices,
    CurrentCoverage CurrentCoverage,
    TimeZoneInfo DisplayTimeZone,
    string DisplayTimeZoneId);

/// <summary>
/// Orchestrates on-call administration: CRUD for plans/teams/bindings,
/// schedule generation, and lookup of current coverage.
/// </summary>
public interface IOnCallScheduleService
{
    // Data loading
    Task<OnCallData> GetOnCallDataAsync(CancellationToken cancellationToken = default);

    // Plan operations
    Task<OnCallPlan> SavePlanAsync(OnCallPlan plan, CancellationToken cancellationToken = default);
    Task<OnCallPlan?> GetPlanAsync(string planId, CancellationToken cancellationToken = default);

    // Team operations
    Task<OnCallTeam> SaveTeamAsync(OnCallTeam team, CancellationToken cancellationToken = default);
    Task<OnCallTeam?> GetTeamAsync(string teamId, CancellationToken cancellationToken = default);

    // Binding operations
    Task<CustomerPlanBinding> SaveBindingAsync(CustomerPlanBinding binding, CancellationToken cancellationToken = default);
    Task<CustomerPlanBinding?> GetBindingAsync(string customerId, CancellationToken cancellationToken = default);

    // Schedule operations
    Task RegenerateAsync(string customerId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);
    Task ExtendAsync(string customerId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);
    Task<CurrentCoverage> GetCurrentCoverageAsync(string customerId, DateTime utcNow, CancellationToken cancellationToken = default);
    Task<ScheduleQueryResult> GetScheduleAsync(string customerId, DateTime fromUtc, DateTime toUtc, int limit = 500, CancellationToken cancellationToken = default);

    // Helpers
    TimeZoneInfo ResolveDisplayTimeZone(string customerId, IReadOnlyList<CustomerPlanBinding> bindings, IReadOnlyList<OnCallPlan> plans);
}

public class OnCallScheduleService : IOnCallScheduleService
{
    private readonly IOnCallPlanRepository _planRepository;
    private readonly IOnCallTeamRepository _teamRepository;
    private readonly ICustomerPlanBindingRepository _bindingRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IUserRepository _userRepository;
    private readonly IScheduleSliceRepository _sliceRepository;
    private readonly IOnCallScheduleGenerator _generator;

    public OnCallScheduleService(
        IOnCallPlanRepository planRepository,
        IOnCallTeamRepository teamRepository,
        ICustomerPlanBindingRepository bindingRepository,
        ICustomerRepository customerRepository,
        IUserRepository userRepository,
        IScheduleSliceRepository sliceRepository,
        IOnCallScheduleGenerator generator)
    {
        _planRepository = planRepository;
        _teamRepository = teamRepository;
        _bindingRepository = bindingRepository;
        _customerRepository = customerRepository;
        _userRepository = userRepository;
        _sliceRepository = sliceRepository;
        _generator = generator;
    }

    public async Task<OnCallData> GetOnCallDataAsync(CancellationToken cancellationToken = default)
    {
        var plansTask = _planRepository.ListAsync(200, 0, cancellationToken);
        var teamsTask = _teamRepository.ListAsync(200, 0, cancellationToken);
        var bindingsTask = _bindingRepository.ListAsync(200, 0, cancellationToken);
        var customersTask = _customerRepository.ListAsync(200, 0, cancellationToken);
        var usersTask = _userRepository.ListAsync(500, 0, cancellationToken);

        await Task.WhenAll(plansTask, teamsTask, bindingsTask, customersTask, usersTask);

        return new OnCallData(
            plansTask.Result.OrderBy(p => p.Name).ToList(),
            teamsTask.Result.OrderBy(t => t.Name).ToList(),
            bindingsTask.Result.OrderBy(b => b.CustomerId).ToList(),
            customersTask.Result.OrderBy(c => c.Name).ToList(),
            usersTask.Result.OrderBy(u => u.DisplayName).ToList());
    }

    public async Task<OnCallPlan> SavePlanAsync(OnCallPlan plan, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plan.Name))
        {
            throw new ArgumentException("Plan name is required", nameof(plan));
        }

        if (string.IsNullOrWhiteSpace(plan.Id))
        {
            plan = plan with { Id = Guid.NewGuid().ToString("N") };
        }

        if (string.IsNullOrWhiteSpace(plan.TimeZone))
        {
            plan = plan with { TimeZone = "UTC" };
        }

        return await _planRepository.UpsertAsync(plan, cancellationToken);
    }

    public async Task<OnCallPlan?> GetPlanAsync(string planId, CancellationToken cancellationToken = default)
    {
        return await _planRepository.GetAsync(planId, cancellationToken);
    }

    public async Task<OnCallTeam> SaveTeamAsync(OnCallTeam team, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(team.Name))
        {
            throw new ArgumentException("Team name is required", nameof(team));
        }

        if (!team.Members.Any())
        {
            throw new ArgumentException("Team must have at least one member", nameof(team));
        }

        if (string.IsNullOrWhiteSpace(team.Id))
        {
            team = team with { Id = Guid.NewGuid().ToString("N") };
        }

        return await _teamRepository.UpsertAsync(team, cancellationToken);
    }

    public async Task<OnCallTeam?> GetTeamAsync(string teamId, CancellationToken cancellationToken = default)
    {
        return await _teamRepository.GetAsync(teamId, cancellationToken);
    }

    public async Task<CustomerPlanBinding> SaveBindingAsync(CustomerPlanBinding binding, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(binding.CustomerId))
        {
            throw new ArgumentException("Customer is required", nameof(binding));
        }

        if (string.IsNullOrWhiteSpace(binding.PlanDefinitionId))
        {
            throw new ArgumentException("Plan is required", nameof(binding));
        }

        if (string.IsNullOrWhiteSpace(binding.OnHoursTeamId) ||
            string.IsNullOrWhiteSpace(binding.OffHoursTeamId) ||
            string.IsNullOrWhiteSpace(binding.BackupTeamId))
        {
            throw new ArgumentException("All three teams (on-hours, off-hours, backup) are required", nameof(binding));
        }

        if (string.IsNullOrWhiteSpace(binding.Id))
        {
            binding = binding with { Id = binding.CustomerId };
        }

        return await _bindingRepository.UpsertAsync(binding, cancellationToken);
    }

    public async Task<CustomerPlanBinding?> GetBindingAsync(string customerId, CancellationToken cancellationToken = default)
    {
        return await _bindingRepository.GetAsync(customerId, cancellationToken);
    }

    public async Task RegenerateAsync(string customerId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        var binding = await _bindingRepository.GetAsync(customerId, cancellationToken)
            ?? throw new InvalidOperationException($"No binding for customer {customerId}");
        var plan = await _planRepository.GetAsync(binding.PlanDefinitionId, cancellationToken)
            ?? throw new InvalidOperationException($"Plan {binding.PlanDefinitionId} not found");

        var onTeam = await GetRequiredTeamAsync(binding.OnHoursTeamId, cancellationToken);
        var offTeam = await GetRequiredTeamAsync(binding.OffHoursTeamId, cancellationToken);
        var backupTeam = await GetRequiredTeamAsync(binding.BackupTeamId, cancellationToken);

        await _sliceRepository.DeleteFutureAsync(customerId, fromUtc, cancellationToken);

        var slices = await _generator.GenerateAsync(plan, binding, onTeam, offTeam, backupTeam, fromUtc, toUtc, cancellationToken);
        await _sliceRepository.UpsertManyAsync(slices, cancellationToken);
    }

    public async Task ExtendAsync(string customerId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        var binding = await _bindingRepository.GetAsync(customerId, cancellationToken)
            ?? throw new InvalidOperationException($"No binding for customer {customerId}");
        var plan = await _planRepository.GetAsync(binding.PlanDefinitionId, cancellationToken)
            ?? throw new InvalidOperationException($"Plan {binding.PlanDefinitionId} not found");

        var onTeam = await GetRequiredTeamAsync(binding.OnHoursTeamId, cancellationToken);
        var offTeam = await GetRequiredTeamAsync(binding.OffHoursTeamId, cancellationToken);
        var backupTeam = await GetRequiredTeamAsync(binding.BackupTeamId, cancellationToken);

        // Generate new slices without deleting existing ones
        var slices = await _generator.GenerateAsync(plan, binding, onTeam, offTeam, backupTeam, fromUtc, toUtc, cancellationToken);
        await _sliceRepository.UpsertManyAsync(slices, cancellationToken);
    }

    public async Task<CurrentCoverage> GetCurrentCoverageAsync(string customerId, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var slices = await _sliceRepository.ListAsync(customerId, utcNow.AddHours(-1), utcNow.AddHours(1), 20, cancellationToken);
        var activeSlices = slices.Where(s => s.StartUtc <= utcNow && s.EndUtc > utcNow).ToList();

        var primarySlice = activeSlices.FirstOrDefault(s => s.Role == "OnHours" || s.Role == "OffHours");
        var backupSlice = activeSlices.FirstOrDefault(s => s.Role == "Backup");

        return new CurrentCoverage(primarySlice, backupSlice);
    }

    public async Task<ScheduleQueryResult> GetScheduleAsync(string customerId, DateTime fromUtc, DateTime toUtc, int limit = 500, CancellationToken cancellationToken = default)
    {
        var nowUtc = DateTime.UtcNow;
        var slices = (await _sliceRepository.ListAsync(customerId, fromUtc, toUtc, limit, cancellationToken))
            .OrderBy(s => s.StartUtc)
            .ToList();

        // If no slices found in the requested window, try an expanded window
        if (!slices.Any())
        {
            var fallbackFrom = nowUtc.AddDays(-7);
            var fallbackTo = nowUtc.AddDays(90);
            slices = (await _sliceRepository.ListAsync(customerId, fallbackFrom, fallbackTo, 1000, cancellationToken))
                .OrderBy(s => s.StartUtc)
                .ToList();
        }

        // Get current coverage (primary + backup)
        var activeSlices = slices.Where(s => s.StartUtc <= nowUtc && s.EndUtc > nowUtc).ToList();
        var primarySlice = activeSlices.FirstOrDefault(s => s.Role == "OnHours" || s.Role == "OffHours");
        var backupSlice = activeSlices.FirstOrDefault(s => s.Role == "Backup");
        var currentCoverage = new CurrentCoverage(primarySlice, backupSlice);

        // Resolve display time zone from binding/plan
        var binding = await _bindingRepository.GetAsync(customerId, cancellationToken);
        var (timeZone, timeZoneId) = await ResolveTimeZoneAsync(binding, cancellationToken);

        return new ScheduleQueryResult(slices, currentCoverage, timeZone, timeZoneId);
    }

    public TimeZoneInfo ResolveDisplayTimeZone(string customerId, IReadOnlyList<CustomerPlanBinding> bindings, IReadOnlyList<OnCallPlan> plans)
    {
        var planId = bindings.FirstOrDefault(b => b.CustomerId == customerId)?.PlanDefinitionId;
        var tzId = plans.FirstOrDefault(p => p.Id == planId)?.TimeZone;

        if (string.IsNullOrWhiteSpace(tzId))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(tzId);
        }
        catch
        {
            return TimeZoneInfo.Utc;
        }
    }

    private async Task<OnCallTeam> GetRequiredTeamAsync(string id, CancellationToken cancellationToken)
    {
        var team = await _teamRepository.GetAsync(id, cancellationToken);
        return team ?? throw new InvalidOperationException($"Team {id} not found");
    }

    private async Task<(TimeZoneInfo TimeZone, string TimeZoneId)> ResolveTimeZoneAsync(CustomerPlanBinding? binding, CancellationToken cancellationToken)
    {
        if (binding == null)
        {
            return (TimeZoneInfo.Utc, "UTC");
        }

        var plan = await _planRepository.GetAsync(binding.PlanDefinitionId, cancellationToken);
        var tzId = plan?.TimeZone;

        if (string.IsNullOrWhiteSpace(tzId))
        {
            return (TimeZoneInfo.Utc, "UTC");
        }

        try
        {
            return (TimeZoneInfo.FindSystemTimeZoneById(tzId), tzId);
        }
        catch
        {
            return (TimeZoneInfo.Utc, "UTC");
        }
    }
}

/// <summary>
/// Default generator implementation; handles per-day windows, rotation, and local-time conversion.
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
        return plan.Holidays.Any(h => h == DateOnly.FromDateTime(date));
    }

    private static OnCallTeam ResolveTeam(Dictionary<string, OnCallTeam> map, string requestedId, OnCallTeam fallback)
    {
        return map.TryGetValue(requestedId, out var team) ? team : fallback;
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
