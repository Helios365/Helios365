using Helios365.Core.Exceptions;
using Helios365.Core.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace Helios365.Core.Repositories;

public class ServicePrincipalRepository : IServicePrincipalRepository
{
    private readonly Container _container;
    private readonly ILogger<ServicePrincipalRepository> _logger;

    public ServicePrincipalRepository(CosmosClient cosmosClient, string databaseName, string containerName, ILogger<ServicePrincipalRepository> logger)
    {
        _container = cosmosClient.GetContainer(databaseName, containerName);
        _logger = logger;
    }

    public async Task<ServicePrincipal?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = new QueryDefinition("SELECT * FROM c WHERE c.id = @id")
                .WithParameter("@id", id);

            var iterator = _container.GetItemQueryIterator<ServicePrincipal>(query);
            
            if (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(cancellationToken);
                return response.FirstOrDefault();
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving service principal {ServicePrincipalId}", id);
            throw new RepositoryException($"Failed to retrieve service principal: {ex.Message}", ex);
        }
    }

    public async Task<ServicePrincipal> CreateAsync(ServicePrincipal item, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _container.CreateItemAsync(item, new PartitionKey(item.CustomerId), cancellationToken: cancellationToken);
            _logger.LogInformation("Created service principal {ServicePrincipalId} for customer {CustomerId}", item.Id, item.CustomerId);
            return response.Resource;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating service principal {ServicePrincipalId}", item.Id);
            throw new RepositoryException($"Failed to create service principal: {ex.Message}", ex);
        }
    }

    public async Task<ServicePrincipal> UpdateAsync(string id, ServicePrincipal item, CancellationToken cancellationToken = default)
    {
        try
        {
            item.Id = id;
            item.UpdatedAt = DateTime.UtcNow;
            var response = await _container.UpsertItemAsync(item, new PartitionKey(item.CustomerId), cancellationToken: cancellationToken);
            _logger.LogInformation("Updated service principal {ServicePrincipalId}", id);
            return response.Resource;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating service principal {ServicePrincipalId}", id);
            throw new RepositoryException($"Failed to update service principal: {ex.Message}", ex);
        }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var sp = await GetAsync(id, cancellationToken);
            if (sp == null)
            {
                return false;
            }

            await _container.DeleteItemAsync<ServicePrincipal>(id, new PartitionKey(sp.CustomerId), cancellationToken: cancellationToken);
            _logger.LogInformation("Deleted service principal {ServicePrincipalId}", id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting service principal {ServicePrincipalId}", id);
            throw new RepositoryException($"Failed to delete service principal: {ex.Message}", ex);
        }
    }

    public async Task<IEnumerable<ServicePrincipal>> ListAsync(int limit = 100, int offset = 0, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = new QueryDefinition("SELECT * FROM c ORDER BY c.name OFFSET @offset LIMIT @limit")
                .WithParameter("@offset", offset)
                .WithParameter("@limit", limit);

            var results = new List<ServicePrincipal>();
            var iterator = _container.GetItemQueryIterator<ServicePrincipal>(query);

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(cancellationToken);
                results.AddRange(response);
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing service principals");
            throw new RepositoryException($"Failed to list service principals: {ex.Message}", ex);
        }
    }

    public async Task<IEnumerable<ServicePrincipal>> ListByCustomerAsync(string customerId, int limit = 100, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = new QueryDefinition("SELECT * FROM c WHERE c.customerId = @customerId ORDER BY c.name")
                .WithParameter("@customerId", customerId);

            var results = new List<ServicePrincipal>();
            var iterator = _container.GetItemQueryIterator<ServicePrincipal>(query, requestOptions: new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(customerId)
            });
            
            var count = 0;

            while (iterator.HasMoreResults && count < limit)
            {
                var response = await iterator.ReadNextAsync(cancellationToken);
                results.AddRange(response.Take(limit - count));
                count += response.Count;
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing service principals for customer {CustomerId}", customerId);
            throw new RepositoryException($"Failed to list service principals for customer: {ex.Message}", ex);
        }
    }
}
