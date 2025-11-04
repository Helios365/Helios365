using Helios365.Core.Models;

namespace Helios365.Core.Repositories;

public interface ICustomerRepository : IRepository<Customer>
{
    Task<IEnumerable<Customer>> ListActiveAsync(int limit = 100, CancellationToken cancellationToken = default);
}
