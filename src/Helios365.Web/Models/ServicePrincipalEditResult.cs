using Helios365.Core.Models;

namespace Helios365.Web.Models;

public class ServicePrincipalEditResult
{
    public ServicePrincipal Principal { get; set; } = new();
    public string? NewClientSecret { get; set; }
}

