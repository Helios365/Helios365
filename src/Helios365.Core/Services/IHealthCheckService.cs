using Helios365.Core.Models;

namespace Helios365.Core.Services;

public interface IHealthCheckService
{
    Task<bool> CheckAsync(HealthCheckConfig config, CancellationToken cancellationToken = default);
}
