using Helios365.Core.Exceptions;
using Helios365.Core.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Helios365.Core.Repositories;

public class ActionRepository : IActionRepository
{
    private readonly Container _container;
    private readonly ILogger<ActionRepository> _logger;

    public ActionRepository(CosmosClient cosmosClient, string databaseName, string containerName, ILogger<ActionRepository> logger)
    {
        _container = cosmosClient.GetContainer(databaseName, containerName);
        _logger = logger;
    }

    public async Task<ActionBase?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = new QueryDefinition("SELECT * FROM c WHERE c.id = @id")
                .WithParameter("@id", id);

            var iterator = _container.GetItemQueryIterator<JsonDocument>(query);
            
            if (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(cancellationToken);
                var doc = response.FirstOrDefault();
                return doc != null ? DeserializeAction(doc) : null;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving action {ActionId}", id);
            throw new RepositoryException($"Failed to retrieve action: {ex.Message}", ex);
        }
    }

    public async Task<ActionBase> CreateAsync(ActionBase item, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _container.CreateItemAsync(item, new PartitionKey(item.CustomerId), cancellationToken: cancellationToken);
            _logger.LogInformation("Created action {ActionId} for customer {CustomerId}", item.Id, item.CustomerId);
            return response.Resource;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating action {ActionId}", item.Id);
            throw new RepositoryException($"Failed to create action: {ex.Message}", ex);
        }
    }

    public async Task<ActionBase> UpdateAsync(string id, ActionBase item, CancellationToken cancellationToken = default)
    {
        try
        {
            item.Id = id;
            item.UpdatedAt = DateTime.UtcNow;
            var response = await _container.UpsertItemAsync(item, new PartitionKey(item.CustomerId), cancellationToken: cancellationToken);
            _logger.LogInformation("Updated action {ActionId}", id);
            return response.Resource;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating action {ActionId}", id);
            throw new RepositoryException($"Failed to update action: {ex.Message}", ex);
        }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var action = await GetAsync(id, cancellationToken);
            if (action == null)
            {
                return false;
            }

            await _container.DeleteItemAsync<ActionBase>(id, new PartitionKey(action.CustomerId), cancellationToken: cancellationToken);
            _logger.LogInformation("Deleted action {ActionId}", id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting action {ActionId}", id);
            throw new RepositoryException($"Failed to delete action: {ex.Message}", ex);
        }
    }

    public async Task<IEnumerable<ActionBase>> ListAsync(int limit = 100, int offset = 0, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = new QueryDefinition("SELECT * FROM c ORDER BY c.order OFFSET @offset LIMIT @limit")
                .WithParameter("@offset", offset)
                .WithParameter("@limit", limit);

            var results = new List<ActionBase>();
            var iterator = _container.GetItemQueryIterator<JsonDocument>(query);

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(cancellationToken);
                results.AddRange(response.Select(DeserializeAction).Where(a => a != null)!);
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing actions");
            throw new RepositoryException($"Failed to list actions: {ex.Message}", ex);
        }
    }

    public async Task<IEnumerable<ActionBase>> ListByResourceAsync(string resourceId, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = new QueryDefinition("SELECT * FROM c WHERE c.resourceId = @resourceId ORDER BY c.order")
                .WithParameter("@resourceId", resourceId);

            var results = new List<ActionBase>();
            var iterator = _container.GetItemQueryIterator<JsonDocument>(query);

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(cancellationToken);
                results.AddRange(response.Select(DeserializeAction).Where(a => a != null)!);
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing actions for resource {ResourceId}", resourceId);
            throw new RepositoryException($"Failed to list actions for resource: {ex.Message}", ex);
        }
    }

    public async Task<IEnumerable<ActionBase>> ListDefaultActionsAsync(string customerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = new QueryDefinition("SELECT * FROM c WHERE c.customerId = @customerId AND (c.resourceId = null OR NOT IS_DEFINED(c.resourceId)) ORDER BY c.order")
                .WithParameter("@customerId", customerId);

            var results = new List<ActionBase>();
            var iterator = _container.GetItemQueryIterator<JsonDocument>(query, requestOptions: new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(customerId)
            });

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(cancellationToken);
                results.AddRange(response.Select(DeserializeAction).Where(a => a != null)!);
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing default actions for customer {CustomerId}", customerId);
            throw new RepositoryException($"Failed to list default actions: {ex.Message}", ex);
        }
    }

    public async Task<IEnumerable<ActionBase>> ListAutomaticActionsAsync(string customerId, string? resourceId, CancellationToken cancellationToken = default)
    {
        try
        {
            string queryText;
            if (string.IsNullOrEmpty(resourceId))
            {
                // Get default actions (resourceId is null)
                queryText = "SELECT * FROM c WHERE c.customerId = @customerId AND (c.resourceId = null OR NOT IS_DEFINED(c.resourceId)) AND c.enabled = true AND c.mode = 'Automatic' ORDER BY c.order";
            }
            else
            {
                // Get resource-specific actions
                queryText = "SELECT * FROM c WHERE c.customerId = @customerId AND c.resourceId = @resourceId AND c.enabled = true AND c.mode = 'Automatic' ORDER BY c.order";
            }

            var query = new QueryDefinition(queryText)
                .WithParameter("@customerId", customerId);

            if (!string.IsNullOrEmpty(resourceId))
            {
                query = query.WithParameter("@resourceId", resourceId);
            }

            var results = new List<ActionBase>();
            var iterator = _container.GetItemQueryIterator<JsonDocument>(query, requestOptions: new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(customerId)
            });

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(cancellationToken);
                results.AddRange(response.Select(DeserializeAction).Where(a => a != null)!);
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing automatic actions for customer {CustomerId}", customerId);
            throw new RepositoryException($"Failed to list automatic actions: {ex.Message}", ex);
        }
    }

    private ActionBase? DeserializeAction(JsonDocument document)
    {
        try
        {
            var root = document.RootElement;
            
            if (!root.TryGetProperty("type", out var typeProperty))
            {
                _logger.LogWarning("Action document missing 'type' property");
                return null;
            }

            var typeString = typeProperty.GetString();
            var json = document.RootElement.GetRawText();

            return typeString switch
            {
                "HealthCheck" => JsonSerializer.Deserialize<HealthCheckAction>(json),
                "Restart" => JsonSerializer.Deserialize<RestartAction>(json),
                "Scale" => JsonSerializer.Deserialize<ScaleAction>(json),
                _ => null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deserializing action");
            return null;
        }
    }
}
