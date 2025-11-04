using Helios365.Core.Models;

namespace Helios365.Core.Repositories;

public interface IAlertRepository : IRepository<Alert>
{
    Task<IEnumerable<Alert>> ListByStatusAsync(AlertStatus status, int limit = 100, CancellationToken cancellationToken = default);
    Task<IEnumerable<Alert>> ListByCustomerAsync(string customerId, int limit = 100, CancellationToken cancellationToken = default);
}
