using Newtonsoft.Json;

namespace Helios365.Core.Models;

public class User
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty; // Entra object id
    public string DisplayName { get; set; } = string.Empty;
    public string UserPrincipalName { get; set; } = string.Empty;
    public string? Mail { get; set; }
    public string? MobilePhone { get; set; }

    // App-specific fields
    public bool NotificationConsentGranted { get; set; }
    public string? TimeZone { get; set; }
    public DateTimeOffset LastSyncedUtc { get; set; } = DateTimeOffset.UtcNow;
}
