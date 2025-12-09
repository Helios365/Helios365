namespace Helios365.Core.Models;

public class User
{
    public string Id { get; set; } = string.Empty; // Entra object id
    public string DisplayName { get; set; } = string.Empty;
    public string UserPrincipalName { get; set; } = string.Empty;
    public string? Mail { get; set; }
    public string? MobilePhone { get; set; }
    public IReadOnlyList<string> BusinessPhones { get; set; } = Array.Empty<string>();

    // App-specific fields
    public string? NotificationPhone { get; set; }
    public string? TimeZone { get; set; }
    public DateTimeOffset LastSyncedUtc { get; set; } = DateTimeOffset.UtcNow;
}
