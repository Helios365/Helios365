using Helios365.Core.Contracts;
using Helios365.Core.Repositories;
using Helios365.Core.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Helios365.Functions.Activities;

public class GetOnCallUsersActivity
{
    private readonly IOnCallScheduleService _scheduleService;
    private readonly IOnCallPlanRepository _planRepository;
    private readonly IUserRepository _userRepository;
    private readonly ILogger<GetOnCallUsersActivity> _logger;

    public GetOnCallUsersActivity(
        IOnCallScheduleService scheduleService,
        IOnCallPlanRepository planRepository,
        IUserRepository userRepository,
        ILogger<GetOnCallUsersActivity> logger)
    {
        _scheduleService = scheduleService;
        _planRepository = planRepository;
        _userRepository = userRepository;
        _logger = logger;
    }

    [Function(nameof(GetOnCallUsersActivity))]
    public async Task<GetOnCallUsersResult> Run(
        [ActivityTrigger] GetOnCallUsersInput input)
    {
        _logger.LogInformation(
            "Getting on-call users for customer {CustomerId} at {UtcNow}",
            input.CustomerId, input.UtcNow);

        var coverage = await _scheduleService.GetCurrentCoverageAsync(
            input.CustomerId, input.UtcNow);

        var primaryUsers = new List<OnCallUserInfo>();
        var backupUsers = new List<OnCallUserInfo>();
        string? planId = null;
        EscalationPolicyInfo? escalationPolicy = null;

        // Get primary on-call users
        if (coverage.PrimarySlice != null)
        {
            planId = coverage.PrimarySlice.PlanDefinitionId;

            foreach (var userId in coverage.PrimarySlice.MemberIds)
            {
                var user = await _userRepository.GetAsync(userId);
                if (user != null)
                {
                    primaryUsers.Add(new OnCallUserInfo(
                        user.Id,
                        user.DisplayName,
                        user.Mail,
                        user.MobilePhone));
                }
            }
        }

        // Get backup users
        if (coverage.BackupSlice != null)
        {
            planId ??= coverage.BackupSlice.PlanDefinitionId;

            foreach (var userId in coverage.BackupSlice.MemberIds)
            {
                var user = await _userRepository.GetAsync(userId);
                if (user != null)
                {
                    backupUsers.Add(new OnCallUserInfo(
                        user.Id,
                        user.DisplayName,
                        user.Mail,
                        user.MobilePhone));
                }
            }
        }

        // Get escalation policy from plan
        if (!string.IsNullOrEmpty(planId))
        {
            var plan = await _planRepository.GetAsync(planId);
            if (plan?.Escalation != null)
            {
                escalationPolicy = new EscalationPolicyInfo(
                    plan.Escalation.AckTimeout,
                    plan.Escalation.MaxRetries,
                    plan.Escalation.RetryDelay);
            }
        }

        _logger.LogInformation(
            "Found {PrimaryCount} primary and {BackupCount} backup on-call users for customer {CustomerId}",
            primaryUsers.Count, backupUsers.Count, input.CustomerId);

        return new GetOnCallUsersResult(
            primaryUsers,
            backupUsers,
            planId,
            escalationPolicy);
    }
}
