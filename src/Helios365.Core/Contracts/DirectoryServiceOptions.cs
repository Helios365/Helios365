namespace Helios365.Core.Contracts;

public class DirectoryServiceOptions
{
    public DirectoryServiceGroups Groups { get; set; } = new();
}

public class DirectoryServiceGroups
{
    public string Admin { get; set; } = string.Empty;
    public string Operator { get; set; } = string.Empty;
    public string Reader { get; set; } = string.Empty;
}
