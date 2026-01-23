namespace Helios365.Core.Contracts;

public class CommunicationServiceOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public string EmailSender { get; set; } = string.Empty;

    public string SmsSender { get; set; } = string.Empty;

    /// <summary>
    /// Base URL for the Helios365 portal (e.g., "https://portal.helios365.com").
    /// Used to construct alert URLs in notifications.
    /// </summary>
    public string PortalBaseUrl { get; set; } = string.Empty;
}
