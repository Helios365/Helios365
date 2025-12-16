namespace Helios365.Core.Contracts;

public class CommunicationServiceOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public string EmailSender { get; set; } = string.Empty;

    public string SmsSender { get; set; } = string.Empty;
}
