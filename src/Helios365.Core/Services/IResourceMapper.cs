using Helios365.Core.Models;

namespace Helios365.Core.Services;

/// <summary>
/// Maps Azure SDK resource representations into the Helios domain resource model.
/// Keeps Azure types at the edges of the system while letting core logic work with <see cref="Resource"/>.
/// </summary>
/// <typeparam name="TAzureResource">The Azure SDK resource type being mapped.</typeparam>
public interface IResourceMapper<in TAzureResource>
{
    /// <summary>
    /// Convert a raw Azure resource into a Helios <see cref="Resource"/> with the correct ownership context.
    /// </summary>
    /// <param name="azureResource">The Azure resource instance.</param>
    /// <param name="customerId">The owning customer id.</param>
    /// <param name="servicePrincipalId">The service principal used to discover the resource.</param>
    Resource Map(TAzureResource azureResource, string customerId, string servicePrincipalId);
}
