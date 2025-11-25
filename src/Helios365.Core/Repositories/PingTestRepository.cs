using Helios365.Core.Exceptions;
using Helios365.Core.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace Helios365.Core.Repositories;

public interface IPingTestRepository
{
    Task<PingTest?> GetByResourceIdAsync(string customerId, string resourceId, CancellationToken cancellationToken = default);
    Task<PingTest> CreateAsync(PingTest item, CancellationToken cancellationToken = default);
    Task<PingTest> UpdateAsync(string id, PingTest item, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, string customerId, CancellationToken cancellationToken = default);
}

public class PingTestRepository : IPingTestRepository
{
    private readonly Container _container;
    private readonly ILogger<PingTestRepository> _logger;

    public PingTestRepository(CosmosClient cosmosClient, string databaseName, string containerName, ILogger<PingTestRepository> logger)
    {
        _container = cosmosClient.GetContainer(databaseName, containerName);
        _logger = logger;
    }

    public async Task<PingTest?> GetByResourceIdAsync(string customerId, string resourceId, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = new QueryDefinition("SELECT * FROM c WHERE c.customerId = @customerId AND c.resourceId = @resourceId")
                .WithParameter("@customerId", customerId)
                .WithParameter("@resourceId", resourceId);

            var iterator = _container.GetItemQueryIterator<PingTest>(query, requestOptions: new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(customerId)
            });

            if (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(cancellationToken);
                return response.FirstOrDefault();
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving ping test for customer {CustomerId} resource {ResourceId}", customerId, resourceId);
            throw new RepositoryException($"Failed to retrieve ping test: {ex.Message}", ex);
        }
    }

    public async Task<PingTest> CreateAsync(PingTest item, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _container.CreateItemAsync(item, new PartitionKey(item.CustomerId), cancellationToken: cancellationToken);
            _logger.LogInformation("Created ping test {PingTestId} for resource {ResourceId}", item.Id, item.ResourceId);
            return response.Resource;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating ping test {PingTestId}", item.Id);
            throw new RepositoryException($"Failed to create ping test: {ex.Message}", ex);
        }
    }

    public async Task<PingTest> UpdateAsync(string id, PingTest item, CancellationToken cancellationToken = default)
    {
        try
        {
            item.Id = id;
            var response = await _container.UpsertItemAsync(item, new PartitionKey(item.CustomerId), cancellationToken: cancellationToken);
            _logger.LogInformation("Updated ping test {PingTestId}", id);
            return response.Resource;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating ping test {PingTestId}", id);
            throw new RepositoryException($"Failed to update ping test: {ex.Message}", ex);
        }
    }

    public async Task<bool> DeleteAsync(string id, string customerId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _container.DeleteItemAsync<PingTest>(id, new PartitionKey(customerId), cancellationToken: cancellationToken);
            _logger.LogInformation("Deleted ping test {PingTestId}", id);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting ping test {PingTestId}", id);
            throw new RepositoryException($"Failed to delete ping test: {ex.Message}", ex);
        }
    }
}
