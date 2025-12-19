namespace Helios365.Core.Contracts;
public enum DirectoryRole
{
    Admin,
    Operator,
    Reader
}
public class DirectoryUser
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string UserPrincipalName { get; set; } = string.Empty;
    public string? Mail { get; set; }
    public string? MobilePhone { get; set; }
}
