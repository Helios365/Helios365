using Helios365.Core.Models;

namespace Helios365.Core.Services;

public interface INotificationService
{
    Task<bool> SendAsync(Alert alert, NotificationConfig config, string? message = null, CancellationToken cancellationToken = default);
}
