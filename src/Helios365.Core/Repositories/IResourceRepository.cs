using Helios365.Core.Models;

namespace Helios365.Core.Repositories;

public interface IResourceRepository : IRepository<Resource>
{
    Task<Resource?> GetByResourceIdAsync(string customerId, string resourceId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Resource>> ListByCustomerAsync(string customerId, int limit = 100, CancellationToken cancellationToken = default);
}
