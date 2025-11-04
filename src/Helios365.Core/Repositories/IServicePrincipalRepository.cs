using Helios365.Core.Models;

namespace Helios365.Core.Repositories;

public interface IServicePrincipalRepository : IRepository<ServicePrincipal>
{
    Task<IEnumerable<ServicePrincipal>> ListByCustomerAsync(string customerId, int limit = 100, CancellationToken cancellationToken = default);
}
