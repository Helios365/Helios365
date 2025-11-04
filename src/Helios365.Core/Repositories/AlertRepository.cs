using Helios365.Core.Exceptions;
using Helios365.Core.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace Helios365.Core.Repositories;

public class AlertRepository : IAlertRepository
{
    private readonly Container _container;
    private readonly ILogger<AlertRepository> _logger;

    public AlertRepository(CosmosClient cosmosClient, string databaseName, string containerName, ILogger<AlertRepository> logger)
    {
        _container = cosmosClient.GetContainer(databaseName, containerName);
        _logger = logger;
    }

    public async Task<Alert?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _container.ReadItemAsync<Alert>(id, new PartitionKey(id), cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogDebug("Alert {AlertId} not found", id);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving alert {AlertId}", id);
            throw new RepositoryException($"Failed to retrieve alert: {ex.Message}", ex);
        }
    }

    public async Task<Alert> CreateAsync(Alert item, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _container.CreateItemAsync(item, new PartitionKey(item.Id), cancellationToken: cancellationToken);
            _logger.LogInformation("Created alert {AlertId}", item.Id);
            return response.Resource;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating alert {AlertId}", item.Id);
            throw new RepositoryException($"Failed to create alert: {ex.Message}", ex);
        }
    }

    public async Task<Alert> UpdateAsync(string id, Alert item, CancellationToken cancellationToken = default)
    {
        try
        {
            item.Id = id;
            item.UpdatedAt = DateTime.UtcNow;
            var response = await _container.UpsertItemAsync(item, new PartitionKey(id), cancellationToken: cancellationToken);
            _logger.LogInformation("Updated alert {AlertId}", id);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new ResourceNotFoundException($"Alert {id} not found");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating alert {AlertId}", id);
            throw new RepositoryException($"Failed to update alert: {ex.Message}", ex);
        }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            await _container.DeleteItemAsync<Alert>(id, new PartitionKey(id), cancellationToken: cancellationToken);
            _logger.LogInformation("Deleted alert {AlertId}", id);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogDebug("Alert {AlertId} not found for deletion", id);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting alert {AlertId}", id);
            throw new RepositoryException($"Failed to delete alert: {ex.Message}", ex);
        }
    }

    public async Task<IEnumerable<Alert>> ListAsync(int limit = 100, int offset = 0, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = new QueryDefinition("SELECT * FROM c ORDER BY c.createdAt DESC OFFSET @offset LIMIT @limit")
                .WithParameter("@offset", offset)
                .WithParameter("@limit", limit);

            var results = new List<Alert>();
            var iterator = _container.GetItemQueryIterator<Alert>(query);

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(cancellationToken);
                results.AddRange(response);
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing alerts");
            throw new RepositoryException($"Failed to list alerts: {ex.Message}", ex);
        }
    }

    public async Task<IEnumerable<Alert>> ListByStatusAsync(AlertStatus status, int limit = 100, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = new QueryDefinition("SELECT * FROM c WHERE c.status = @status ORDER BY c.createdAt DESC")
                .WithParameter("@status", status.ToString());

            var results = new List<Alert>();
            var iterator = _container.GetItemQueryIterator<Alert>(query);
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
            _logger.LogError(ex, "Error listing alerts by status {Status}", status);
            throw new RepositoryException($"Failed to list alerts by status: {ex.Message}", ex);
        }
    }

    public async Task<IEnumerable<Alert>> ListByCustomerAsync(string customerId, int limit = 100, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = new QueryDefinition("SELECT * FROM c WHERE c.customerId = @customerId ORDER BY c.createdAt DESC")
                .WithParameter("@customerId", customerId);

            var results = new List<Alert>();
            var iterator = _container.GetItemQueryIterator<Alert>(query);
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
            _logger.LogError(ex, "Error listing alerts for customer {CustomerId}", customerId);
            throw new RepositoryException($"Failed to list alerts for customer: {ex.Message}", ex);
        }
    }
}
