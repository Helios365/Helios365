using Helios365.Core.Models;
using MudBlazor;

namespace Helios365.Web.Helpers;

public static class AlertDisplayHelper
{
    public static Color GetSeverityColor(AlertSeverity severity) => severity switch
    {
        AlertSeverity.Critical => Color.Error,
        AlertSeverity.High => Color.Error,
        AlertSeverity.Medium => Color.Warning,
        AlertSeverity.Low => Color.Info,
        AlertSeverity.Info => Color.Default,
        _ => Color.Default
    };

    public static string GetSeverityIcon(AlertSeverity severity) => severity switch
    {
        AlertSeverity.Critical => Icons.Material.Filled.Error,
        AlertSeverity.High => Icons.Material.Filled.Warning,
        AlertSeverity.Medium => Icons.Material.Filled.Info,
        AlertSeverity.Low => Icons.Material.Filled.Flag,
        AlertSeverity.Info => Icons.Material.Outlined.Info,
        _ => Icons.Material.Filled.Notifications
    };

    public static Color GetStatusColor(AlertStatus status) => status switch
    {
        AlertStatus.Accepted => Color.Warning,
        AlertStatus.Resolved => Color.Success,
        AlertStatus.Escalated => Color.Error,
        AlertStatus.Failed => Color.Error,
        _ => Color.Primary
    };

    public static string FormatTimelineMessage(AlertChange entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Comment))
        {
            return entry.Comment;
        }

        return entry.NewStatus switch
        {
            AlertStatus.Accepted => $"{entry.User} accepted this alert",
            AlertStatus.Resolved => $"{entry.User} resolved this alert",
            AlertStatus.Escalated => $"{entry.User} escalated this alert",
            AlertStatus.Pending => "Alert pending",
            AlertStatus.Failed => "Alert processing failed",
            _ => $"Status changed to {entry.NewStatus}"
        };
    }

    public static string GetAcceptedTime(Alert alert)
    {
        var acceptChange = alert.Changes?.FirstOrDefault(c => c.NewStatus == AlertStatus.Accepted);
        if (acceptChange != null)
        {
            return acceptChange.CreatedAt.ToLocalTime().ToString("HH:mm");
        }
        return alert.UpdatedAt.ToLocalTime().ToString("HH:mm");
    }

    public static string GetAcceptedBy(Alert alert)
    {
        var acceptChange = alert.Changes?.FirstOrDefault(c => c.NewStatus == AlertStatus.Accepted);
        return acceptChange?.User ?? "Unknown";
    }

    public static string GetEscalatedTime(Alert alert)
    {
        if (alert.EscalatedAt.HasValue)
        {
            return alert.EscalatedAt.Value.ToLocalTime().ToString("HH:mm");
        }
        var escalateChange = alert.Changes?.FirstOrDefault(c => c.NewStatus == AlertStatus.Escalated);
        if (escalateChange != null)
        {
            return escalateChange.CreatedAt.ToLocalTime().ToString("HH:mm");
        }
        return alert.UpdatedAt.ToLocalTime().ToString("HH:mm");
    }

    public static string TruncateText(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text)) return "(no title)";
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}
