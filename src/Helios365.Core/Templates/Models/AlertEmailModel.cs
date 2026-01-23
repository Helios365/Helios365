namespace Helios365.Core.Templates.Models;

/// <summary>
/// Model for alert notification email templates.
/// </summary>
public class AlertEmailModel
{
    public required string AlertId { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public required string Severity { get; init; }
    public required string ResourceId { get; init; }
    public required string RecipientName { get; init; }
    public required string CustomerId { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string? PortalUrl { get; init; }
}
