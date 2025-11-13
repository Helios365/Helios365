using Helios365.Core.Exceptions;
using Helios365.Core.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace Helios365.Core.Repositories;

public class CustomerRepository : ICustomerRepository
{
    private readonly Container _container;
    private readonly ILogger<CustomerRepository> _logger;

    public CustomerRepository(CosmosClient cosmosClient, string databaseName, string containerName, ILogger<CustomerRepository> logger)
    {
        _container = cosmosClient.GetContainer(databaseName, containerName);
        _logger = logger;
    }

    public async Task<Customer?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _container.ReadItemAsync<Customer>(id, new PartitionKey(id), cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogDebug("Customer {CustomerId} not found", id);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving customer {CustomerId}", id);
            throw new RepositoryException($"Failed to retrieve customer: {ex.Message}", ex);
        }
    }

    public async Task<Customer?> GetByApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = new QueryDefinition("SELECT * FROM c WHERE c.apiKey = @apiKey")
                .WithParameter("@apiKey", apiKey);

            var iterator = _container.GetItemQueryIterator<Customer>(query);
            
            if (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(cancellationToken);
                return response.FirstOrDefault();
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving customer by API key");
            throw new RepositoryException($"Failed to retrieve customer by API key: {ex.Message}", ex);
        }
    }

    public async Task<Customer> CreateAsync(Customer item, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(item.Id))
            {
                item.Id = Guid.NewGuid().ToString("N");
            }
            if (item.CreatedAt == default)
            {
                item.CreatedAt = DateTime.UtcNow;
            }
            item.UpdatedAt = DateTime.UtcNow;
            var response = await _container.CreateItemAsync(item, new PartitionKey(item.Id), cancellationToken: cancellationToken);
            _logger.LogInformation("Created customer {CustomerId}", item.Id);
            return response.Resource;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating customer {CustomerId}", item.Id);
            throw new RepositoryException($"Failed to create customer: {ex.Message}", ex);
        }
    }

    public async Task<Customer> UpdateAsync(string id, Customer item, CancellationToken cancellationToken = default)
    {
        try
        {
            item.Id = id;
            item.UpdatedAt = DateTime.UtcNow;
            var response = await _container.UpsertItemAsync(item, new PartitionKey(id), cancellationToken: cancellationToken);
            _logger.LogInformation("Updated customer {CustomerId}", id);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new ResourceNotFoundException($"Customer {id} not found");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating customer {CustomerId}", id);
            throw new RepositoryException($"Failed to update customer: {ex.Message}", ex);
        }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            await _container.DeleteItemAsync<Customer>(id, new PartitionKey(id), cancellationToken: cancellationToken);
            _logger.LogInformation("Deleted customer {CustomerId}", id);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogDebug("Customer {CustomerId} not found for deletion", id);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting customer {CustomerId}", id);
            throw new RepositoryException($"Failed to delete customer: {ex.Message}", ex);
        }
    }

    public async Task<IEnumerable<Customer>> ListAsync(int limit = 100, int offset = 0, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = new QueryDefinition("SELECT * FROM c ORDER BY c.name OFFSET @offset LIMIT @limit")
                .WithParameter("@offset", offset)
                .WithParameter("@limit", limit);

            var results = new List<Customer>();
            var iterator = _container.GetItemQueryIterator<Customer>(query);

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(cancellationToken);
                results.AddRange(response);
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing customers");
            throw new RepositoryException($"Failed to list customers: {ex.Message}", ex);
        }
    }

    public async Task<IEnumerable<Customer>> ListActiveAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = new QueryDefinition("SELECT * FROM c WHERE c.active = true ORDER BY c.name");

            var results = new List<Customer>();
            var iterator = _container.GetItemQueryIterator<Customer>(query);
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
            _logger.LogError(ex, "Error listing active customers");
            throw new RepositoryException($"Failed to list active customers: {ex.Message}", ex);
        }
    }
}
