using Helios365.Core.Models;

namespace Helios365.Core.Repositories;

public interface IActionRepository : IRepository<ActionBase>
{
    Task<IEnumerable<ActionBase>> ListByResourceAsync(string resourceId, CancellationToken cancellationToken = default);
    Task<IEnumerable<ActionBase>> ListDefaultActionsAsync(string customerId, CancellationToken cancellationToken = default);
    Task<IEnumerable<ActionBase>> ListAutomaticActionsAsync(string customerId, string? resourceId, CancellationToken cancellationToken = default);
}
