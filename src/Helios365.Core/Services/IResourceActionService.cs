using Helios365.Core.Models;

namespace Helios365.Core.Services;

public interface IResourceActionService
{
    Task<bool> Execute(Resource resource, ActionBase action, CancellationToken cancellationToken = default);

    IReadOnlyCollection<ActionType> GetSupportedActions(Resource resource);
}
