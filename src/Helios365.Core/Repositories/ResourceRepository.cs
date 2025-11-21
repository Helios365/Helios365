using Helios365.Core.Exceptions;
using Helios365.Core.Models;
using Helios365.Core.Utilities;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace Helios365.Core.Repositories;

public interface IResourceRepository : IRepository<Resource>
{
    Task<Resource?> GetByResourceIdAsync(string customerId, string resourceId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Resource>> ListByCustomerAsync(string customerId, int limit = 100, CancellationToken cancellationToken = default);
}

public class ResourceRepository : IResourceRepository
{
    private readonly Container _container;
    private readonly ILogger<ResourceRepository> _logger;

    public ResourceRepository(CosmosClient cosmosClient, string databaseName, string containerName, ILogger<ResourceRepository> logger)
    {
        _container = cosmosClient.GetContainer(databaseName, containerName);
        _logger = logger;
    }

    public async Task<Resource?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            // Note: Using customerId as partition key, so we need to extract it or pass it
            // For now, using cross-partition query
            var query = new QueryDefinition("SELECT * FROM c WHERE c.id = @id")
                .WithParameter("@id", id);

            var iterator = _container.GetItemQueryIterator<Resource>(query);
            
            if (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(cancellationToken);
                return response.FirstOrDefault();
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving resource {ResourceId}", id);
            throw new RepositoryException($"Failed to retrieve resource: {ex.Message}", ex);
        }
    }

    public async Task<Resource?> GetByResourceIdAsync(string customerId, string resourceId, CancellationToken cancellationToken = default)
    {
        try
        {
            var normalizedResourceId = ResourceIdNormalizer.Normalize(resourceId);

            var query = new QueryDefinition("SELECT * FROM c WHERE c.customerId = @customerId AND c.resourceId = @resourceId")
                .WithParameter("@customerId", customerId)
                .WithParameter("@resourceId", normalizedResourceId);

            var iterator = _container.GetItemQueryIterator<Resource>(query, requestOptions: new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(customerId)
            });
            
            if (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(cancellationToken);
                return response.FirstOrDefault();
            }

            _logger.LogDebug("Resource not found for customer {CustomerId} and resourceId {ResourceId}", customerId, resourceId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving resource by resourceId {ResourceId} for customer {CustomerId}", resourceId, customerId);
            throw new RepositoryException($"Failed to retrieve resource: {ex.Message}", ex);
        }
    }

    public async Task<Resource> CreateAsync(Resource item, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _container.CreateItemAsync(item, new PartitionKey(item.CustomerId), cancellationToken: cancellationToken);
            _logger.LogInformation("Created resource {ResourceId} for customer {CustomerId}", item.Id, item.CustomerId);
            return response.Resource;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating resource {ResourceId}", item.Id);
            throw new RepositoryException($"Failed to create resource: {ex.Message}", ex);
        }
    }

    public async Task<Resource> UpdateAsync(string id, Resource item, CancellationToken cancellationToken = default)
    {
        try
        {
            item.Id = id;
            item.UpdatedAt = DateTime.UtcNow;
            var response = await _container.UpsertItemAsync(item, new PartitionKey(item.CustomerId), cancellationToken: cancellationToken);
            _logger.LogInformation("Updated resource {ResourceId}", id);
            return response.Resource;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating resource {ResourceId}", id);
            throw new RepositoryException($"Failed to update resource: {ex.Message}", ex);
        }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            // Need to get the item first to know the partition key
            var resource = await GetAsync(id, cancellationToken);
            if (resource == null)
            {
                return false;
            }

            await _container.DeleteItemAsync<Resource>(id, new PartitionKey(resource.CustomerId), cancellationToken: cancellationToken);
            _logger.LogInformation("Deleted resource {ResourceId}", id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting resource {ResourceId}", id);
            throw new RepositoryException($"Failed to delete resource: {ex.Message}", ex);
        }
    }

    public async Task<IEnumerable<Resource>> ListAsync(int limit = 100, int offset = 0, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = new QueryDefinition("SELECT * FROM c ORDER BY c.name OFFSET @offset LIMIT @limit")
                .WithParameter("@offset", offset)
                .WithParameter("@limit", limit);

            var results = new List<Resource>();
            var iterator = _container.GetItemQueryIterator<Resource>(query);

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(cancellationToken);
                results.AddRange(response);
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing resources");
            throw new RepositoryException($"Failed to list resources: {ex.Message}", ex);
        }
    }

    public async Task<IEnumerable<Resource>> ListByCustomerAsync(string customerId, int limit = 100, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = new QueryDefinition("SELECT * FROM c WHERE c.customerId = @customerId ORDER BY c.name")
                .WithParameter("@customerId", customerId);

            var results = new List<Resource>();
            var iterator = _container.GetItemQueryIterator<Resource>(query, requestOptions: new QueryRequestOptions
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
            _logger.LogError(ex, "Error listing resources for customer {CustomerId}", customerId);
            throw new RepositoryException($"Failed to list resources for customer: {ex.Message}", ex);
        }
    }
}
