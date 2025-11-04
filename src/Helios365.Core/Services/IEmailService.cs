using Helios365.Core.Models;

namespace Helios365.Core.Services;

public interface IEmailService
{
    Task SendEscalationEmailAsync(
        Alert alert,
        Resource? resource,
        Customer customer,
        List<ActionBase> attemptedActions,
        CancellationToken cancellationToken = default);
}
