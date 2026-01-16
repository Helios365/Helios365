namespace Helios365.Core.Contracts;

/// <summary>
/// Input for the SendNotification activity.
/// </summary>
public record SendNotificationInput(
    string AlertId,
    string CustomerId,
    string UserId,
    string UserDisplayName,
    string? UserEmail,
    string? UserPhone,
    string AlertTitle,
    string? AlertDescription,
    string AlertSeverity,
    string ResourceId);

/// <summary>
/// Result from SendNotification activity.
/// </summary>
public record SendNotificationResult(
    bool EmailSent,
    bool SmsSent,
    string? ErrorMessage);

/// <summary>
/// Input for GetOnCallUsers activity.
/// </summary>
public record GetOnCallUsersInput(
    string CustomerId,
    DateTime UtcNow);

/// <summary>
/// On-call user info for notification.
/// </summary>
public record OnCallUserInfo(
    string UserId,
    string DisplayName,
    string? Email,
    string? Phone);

/// <summary>
/// Result from GetOnCallUsers activity.
/// </summary>
public record GetOnCallUsersResult(
    IReadOnlyList<OnCallUserInfo> PrimaryUsers,
    IReadOnlyList<OnCallUserInfo> BackupUsers,
    string? PlanId,
    EscalationPolicyInfo? EscalationPolicy);

/// <summary>
/// Escalation policy info extracted from plan.
/// </summary>
public record EscalationPolicyInfo(
    TimeSpan AckTimeout,
    int MaxRetries,
    TimeSpan RetryDelay);
