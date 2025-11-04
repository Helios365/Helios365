using Helios365.Core.Models;

namespace Helios365.Core.Services;

public interface IActionExecutor
{
    Task<bool> ExecuteAsync(ActionBase action, Resource resource, ServicePrincipal servicePrincipal, CancellationToken cancellationToken = default);
}
