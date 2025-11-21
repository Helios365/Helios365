using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Compute;
using Helios365.Core.Models;
using Microsoft.Extensions.Logging;

namespace Helios365.Core.Services;

public interface IVirtualMachineService
{
    Task<bool> RestartAsync(ServicePrincipal servicePrincipal, string resourceId, RestartAction action, CancellationToken cancellationToken = default);
}

public class VirtualMachineService : IVirtualMachineService
{
    private readonly IArmClientFactory _armClientFactory;
    private readonly ILogger<VirtualMachineService> _logger;

    public VirtualMachineService(IArmClientFactory armClientFactory, ILogger<VirtualMachineService> logger)
    {
        _armClientFactory = armClientFactory;
        _logger = logger;
    }

    public async Task<bool> RestartAsync(ServicePrincipal servicePrincipal, string resourceId, RestartAction action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(servicePrincipal);
        ArgumentNullException.ThrowIfNull(action);

        if (string.IsNullOrWhiteSpace(resourceId))
        {
            throw new ArgumentException("ResourceId is required.", nameof(resourceId));
        }

        try
        {
            var armClient = await _armClientFactory.CreateAsync(servicePrincipal, cancellationToken).ConfigureAwait(false);
            var virtualMachine = armClient.GetVirtualMachineResource(new ResourceIdentifier(resourceId));

            if (action.WaitBeforeSeconds > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(action.WaitBeforeSeconds), cancellationToken).ConfigureAwait(false);
            }

            await virtualMachine.RestartAsync(WaitUntil.Completed, cancellationToken).ConfigureAwait(false);
            await DelayAfterActionAsync(action.WaitAfterSeconds, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Restarted virtual machine {ResourceId} using service principal {ServicePrincipalId}", resourceId, servicePrincipal.Id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restart virtual machine {ResourceId}", resourceId);
            return false;
        }
    }

    private static async Task DelayAfterActionAsync(int waitAfterSeconds, CancellationToken cancellationToken)
    {
        if (waitAfterSeconds > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(waitAfterSeconds), cancellationToken).ConfigureAwait(false);
        }
    }
}
