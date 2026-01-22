using System.Text.Json;
using Helios365.Core.Contracts;
using Helios365.Core.Models;
using Helios365.Core.Repositories;
using Helios365.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace Helios365.Core.Services;

public interface IAlertService
{
    Task<AlertProcessingResult> IngestAzureMonitorAlertAsync(Customer customer, string payload, CancellationToken cancellationToken = default);
    Task<Alert?> GetAlertAsync(string alertId, CancellationToken cancellationToken = default);
    Task<Alert> AddTimelineEntryAsync(string alertId, string message, AlertStatus? newStatus = null, CancellationToken cancellationToken = default);
    Task<Alert> UpdateEscalationStateAsync(string alertId, int attempt, string targetUserId, CancellationToken cancellationToken = default);
    Task<Alert> RecordNotificationResultAsync(string alertId, string userName, bool emailSent, bool smsSent, string? errorMessage = null, bool isBackup = false, CancellationToken cancellationToken = default);
    Task<Alert> MarkEscalatedAsync(string alertId, string reason, CancellationToken cancellationToken = default);
    Task<Alert> MarkFailedAsync(string alertId, string reason, CancellationToken cancellationToken = default);
}

public class AlertService : IAlertService
{
    private readonly IResourceRepository _resourceRepository;
    private readonly IAlertRepository _alertRepository;
    private readonly ILogger<AlertService> _logger;

    public AlertService(
        IResourceRepository resourceRepository,
        IAlertRepository alertRepository,
        ILogger<AlertService> logger)
    {
        _resourceRepository = resourceRepository;
        _alertRepository = alertRepository;
        _logger = logger;
    }

    public async Task<AlertProcessingResult> IngestAzureMonitorAlertAsync(Customer customer, string payload, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return AlertProcessingResult.ValidationFailed("Request body is required");
        }

        AzureCommonAlertData alertData;
        AzureAlertEssentials essentials;
        try
        {
            var alertEnvelope = JsonSerializer.Deserialize<AzureCommonAlert>(payload, SerializerOptions);
            if (alertEnvelope?.Data is not AzureCommonAlertData data || data.Essentials is not AzureAlertEssentials e)
            {
                return AlertProcessingResult.ValidationFailed("Azure alert essentials are required");
            }

            alertData = data;
            essentials = e;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid Azure common alert payload");
            return AlertProcessingResult.ValidationFailed("Invalid Azure common alert payload");
        }

        var resourceId = essentials.AlertTargetIds?.FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return AlertProcessingResult.ValidationFailed("Azure alert missing alertTargetIDs");
        }

        var normalizedResourceId = Normalizers.NormalizeResourceId(resourceId);
        var resource = await _resourceRepository.GetByResourceIdAsync(customer.Id, normalizedResourceId, cancellationToken);
        var monitorCondition = essentials.MonitorCondition ?? "Fired";

        if (IsResolved(monitorCondition))
        {
            var sourceAlertId = essentials.AlertId ?? essentials.OriginAlertId ?? string.Empty;
            Alert? existingAlert = null;

            if (!string.IsNullOrWhiteSpace(sourceAlertId))
            {
                existingAlert = await _alertRepository.GetBySourceAlertIdAsync(customer.Id, sourceAlertId, cancellationToken);
            }

            if (existingAlert != null)
            {
                MergeMetadata(existingAlert.Metadata, BuildMetadata(essentials, alertData.AlertContext));
                existingAlert.Title = essentials.AlertRule ?? existingAlert.Title;
                existingAlert.Description = essentials.Description ?? existingAlert.Description;
                existingAlert.MarkStatus(AlertStatus.Resolved);
                await _alertRepository.UpdateAsync(existingAlert.Id, existingAlert, cancellationToken);

                _logger.LogInformation("Resolved alert {AlertId} for source alert {SourceAlertId}", existingAlert.Id, sourceAlertId);

                return AlertProcessingResult.Resolved(existingAlert, "Alert marked as resolved");
            }

            _logger.LogInformation("Received resolve notification for source alert {SourceAlertId} but no matching alert was found", sourceAlertId);
            return AlertProcessingResult.Resolved(null, "No matching alert found for resolution");
        }

        var alert = new Alert
        {
            Id = Guid.NewGuid().ToString(),
            CustomerId = customer.Id,
            ResourceId = normalizedResourceId,
            ResourceType = ResolveResourceType(normalizedResourceId, resource),
            AlertType = essentials.SignalType ?? "AzureMonitor",
            Title = essentials.AlertRule ?? essentials.Description ?? "Azure monitor alert",
            Description = essentials.Description,
            Severity = MapSeverity(essentials.Severity),
            Status = AlertStatus.Pending,
            CreatedAt = essentials.FiredDateTime?.ToUniversalTime() ?? DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Metadata = BuildMetadata(essentials, alertData.AlertContext),
            Changes = new List<AlertChange>
            {
                new AlertChange
                {
                    Id = Guid.NewGuid().ToString(),
                    User = "system",
                    Comment = "Alert received from Azure Monitor",
                    NewStatus = AlertStatus.Pending,
                    CreatedAt = DateTime.UtcNow
                }
            }
        };

        await _alertRepository.CreateAsync(alert, cancellationToken);
        _logger.LogInformation("Created alert {AlertId} for customer {CustomerId}", alert.Id, customer.Id);

        if (resource == null)
        {
            _logger.LogWarning("Resource {ResourceId} not found for customer {CustomerId}. Escalating immediately.", normalizedResourceId, customer.Id);

            alert.MarkStatus(AlertStatus.Escalated);
            await _alertRepository.UpdateAsync(alert.Id, alert, cancellationToken);

            return AlertProcessingResult.Escalated(alert, "Resource not found in Helios365. Alert escalated to on-call.");
        }

        return AlertProcessingResult.Created(alert, "Azure alert received and processing started");
    }

    public async Task<Alert?> GetAlertAsync(string alertId, CancellationToken cancellationToken = default)
    {
        return await _alertRepository.GetAsync(alertId, cancellationToken);
    }

    public async Task<Alert> AddTimelineEntryAsync(string alertId, string message, AlertStatus? newStatus = null, CancellationToken cancellationToken = default)
    {
        var alert = await _alertRepository.GetAsync(alertId, cancellationToken)
            ?? throw new InvalidOperationException($"Alert {alertId} not found");

        var previousStatus = alert.Status;
        if (newStatus.HasValue)
        {
            alert.MarkStatus(newStatus.Value);
        }

        alert.Changes ??= new List<AlertChange>();
        alert.Changes.Add(new AlertChange
        {
            Id = Guid.NewGuid().ToString(),
            User = "system",
            Comment = message,
            PreviousStatus = newStatus.HasValue ? previousStatus : null,
            NewStatus = newStatus,
            CreatedAt = DateTime.UtcNow
        });

        await _alertRepository.UpdateAsync(alertId, alert, cancellationToken);
        return alert;
    }

    public async Task<Alert> UpdateEscalationStateAsync(string alertId, int attempt, string targetUserId, CancellationToken cancellationToken = default)
    {
        var alert = await _alertRepository.GetAsync(alertId, cancellationToken)
            ?? throw new InvalidOperationException($"Alert {alertId} not found");

        alert.EscalationAttempts = attempt;
        alert.CurrentEscalationTarget = targetUserId;

        await _alertRepository.UpdateAsync(alertId, alert, cancellationToken);
        return alert;
    }

    public async Task<Alert> RecordNotificationResultAsync(string alertId, string userName, bool emailSent, bool smsSent, string? errorMessage = null, bool isBackup = false, CancellationToken cancellationToken = default)
    {
        var alert = await _alertRepository.GetAsync(alertId, cancellationToken)
            ?? throw new InvalidOperationException($"Alert {alertId} not found");

        var channels = new List<string>();
        if (emailSent) channels.Add("email");
        if (smsSent) channels.Add("SMS");

        var backupLabel = isBackup ? " (backup)" : "";
        var message = channels.Count > 0
            ? $"Notification sent to {userName}{backupLabel} via {string.Join(" and ", channels)}"
            : $"Failed to notify {userName}{backupLabel}: {errorMessage ?? "unknown error"}";

        alert.Changes ??= new List<AlertChange>();
        alert.Changes.Add(new AlertChange
        {
            Id = Guid.NewGuid().ToString(),
            User = "system",
            Comment = message,
            CreatedAt = DateTime.UtcNow
        });

        await _alertRepository.UpdateAsync(alertId, alert, cancellationToken);
        return alert;
    }

    public async Task<Alert> MarkEscalatedAsync(string alertId, string reason, CancellationToken cancellationToken = default)
    {
        var alert = await _alertRepository.GetAsync(alertId, cancellationToken)
            ?? throw new InvalidOperationException($"Alert {alertId} not found");

        var previousStatus = alert.Status;
        alert.MarkStatus(AlertStatus.Escalated);

        alert.Changes ??= new List<AlertChange>();
        alert.Changes.Add(new AlertChange
        {
            Id = Guid.NewGuid().ToString(),
            User = "system",
            Comment = reason,
            PreviousStatus = previousStatus,
            NewStatus = AlertStatus.Escalated,
            CreatedAt = DateTime.UtcNow
        });

        await _alertRepository.UpdateAsync(alertId, alert, cancellationToken);
        _logger.LogInformation("Alert {AlertId} marked as escalated: {Reason}", alertId, reason);
        return alert;
    }

    public async Task<Alert> MarkFailedAsync(string alertId, string reason, CancellationToken cancellationToken = default)
    {
        var alert = await _alertRepository.GetAsync(alertId, cancellationToken)
            ?? throw new InvalidOperationException($"Alert {alertId} not found");

        var previousStatus = alert.Status;
        alert.MarkStatus(AlertStatus.Failed);

        alert.Changes ??= new List<AlertChange>();
        alert.Changes.Add(new AlertChange
        {
            Id = Guid.NewGuid().ToString(),
            User = "system",
            Comment = reason,
            PreviousStatus = previousStatus,
            NewStatus = AlertStatus.Failed,
            CreatedAt = DateTime.UtcNow
        });

        await _alertRepository.UpdateAsync(alertId, alert, cancellationToken);
        _logger.LogError("Alert {AlertId} marked as failed: {Reason}", alertId, reason);
        return alert;
    }

    private static bool IsResolved(string? monitorCondition) =>
        "resolved".Equals(monitorCondition, StringComparison.OrdinalIgnoreCase);

    private static AlertSeverity MapSeverity(string? severity) =>
        severity?.ToLowerInvariant() switch
        {
            "sev0" => AlertSeverity.Critical,
            "sev1" => AlertSeverity.High,
            "sev2" => AlertSeverity.Medium,
            "sev3" => AlertSeverity.Low,
            "sev4" => AlertSeverity.Info,
            "critical" => AlertSeverity.Critical,
            "warning" => AlertSeverity.Low,
            "informational" => AlertSeverity.Info,
            _ => AlertSeverity.Medium
        };

    private static string ResolveResourceType(string resourceId, Resource? resource)
    {
        if (!string.IsNullOrWhiteSpace(resource?.ResourceType))
        {
            return resource.ResourceType;
        }

        try
        {
            var identifier = Azure.Core.ResourceIdentifier.Parse(resourceId);
            var resourceType = identifier.ResourceType.ToString();
            if (!string.IsNullOrWhiteSpace(resourceType))
            {
                return resourceType.ToLowerInvariant();
            }
        }
        catch (Exception)
        {
            // Ignore parse issues and fall back to unknown
        }

        return "unknown";
    }

    private static Dictionary<string, string> BuildMetadata(AzureAlertEssentials essentials, JsonElement? alertContext)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        AddMetadata(metadata, "source", "AzureMonitor");
        AddMetadata(metadata, "sourceAlertId", essentials.AlertId);
        AddMetadata(metadata, "originAlertId", essentials.OriginAlertId);
        AddMetadata(metadata, "monitorCondition", essentials.MonitorCondition);
        AddMetadata(metadata, "monitoringService", essentials.MonitoringService);
        AddMetadata(metadata, "signalType", essentials.SignalType);
        AddMetadata(metadata, "alertRule", essentials.AlertRule);
        AddMetadata(metadata, "alertContextVersion", essentials.AlertContextVersion);
        AddMetadata(metadata, "essentialsVersion", essentials.EssentialsVersion);
        AddMetadata(metadata, "smartGroupId", essentials.SmartGroupId);
        AddMetadata(metadata, "firedDateTime", essentials.FiredDateTime?.ToUniversalTime().ToString("O"));
        AddMetadata(metadata, "resolvedDateTime", essentials.ResolvedDateTime?.ToUniversalTime().ToString("O"));
        AddMetadata(metadata, "alertContext", alertContext?.GetRawText(), maxLength: 2048);

        return metadata;
    }

    private static void MergeMetadata(IDictionary<string, string> target, IDictionary<string, string> source)
    {
        foreach (var kvp in source)
        {
            target[kvp.Key] = kvp.Value;
        }
    }

    private static void AddMetadata(IDictionary<string, string> metadata, string key, string? value, int? maxLength = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (maxLength.HasValue && value.Length > maxLength.Value)
        {
            value = value[..maxLength.Value];
        }

        metadata[key] = value;
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true
    };
}

public record AlertProcessingResult(AlertProcessingStatus Status, Alert? Alert, string Message)
{
    public static AlertProcessingResult ValidationFailed(string message) => new(AlertProcessingStatus.ValidationFailed, null, message);
    public static AlertProcessingResult Resolved(Alert? alert, string message) => new(AlertProcessingStatus.Resolved, alert, message);
    public static AlertProcessingResult Created(Alert alert, string message) => new(AlertProcessingStatus.Created, alert, message);
    public static AlertProcessingResult Escalated(Alert alert, string message) => new(AlertProcessingStatus.EscalatedUnknownResource, alert, message);
}

public enum AlertProcessingStatus
{
    ValidationFailed,
    Resolved,
    Created,
    EscalatedUnknownResource
}
