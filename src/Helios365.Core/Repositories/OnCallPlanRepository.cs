using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Helios365.Core.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace Helios365.Core.Repositories;

public interface IOnCallPlanRepository
{
    Task<OnCallPlan?> GetAsync(string id, CancellationToken cancellationToken = default);
    Task<IEnumerable<OnCallPlan>> ListAsync(int limit = 100, int offset = 0, CancellationToken cancellationToken = default);
    Task<OnCallPlan> UpsertAsync(OnCallPlan plan, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}

public interface IOnCallTeamRepository
{
    Task<OnCallTeam?> GetAsync(string id, CancellationToken cancellationToken = default);
    Task<IEnumerable<OnCallTeam>> ListAsync(int limit = 100, int offset = 0, CancellationToken cancellationToken = default);
    Task<OnCallTeam> UpsertAsync(OnCallTeam team, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}

public interface ICustomerPlanBindingRepository
{
    Task<CustomerPlanBinding?> GetAsync(string customerId, CancellationToken cancellationToken = default);
    Task<IEnumerable<CustomerPlanBinding>> ListAsync(int limit = 100, int offset = 0, CancellationToken cancellationToken = default);
    Task<CustomerPlanBinding> UpsertAsync(CustomerPlanBinding binding, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string customerId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Append-only slices for audit/SLA and quick lookup of who was on when.
/// </summary>
public interface IScheduleSliceRepository
{
    Task UpsertAsync(ScheduleSlice slice, CancellationToken cancellationToken = default);
    Task UpsertManyAsync(IEnumerable<ScheduleSlice> slices, CancellationToken cancellationToken = default);
    Task<IEnumerable<ScheduleSlice>> ListAsync(string customerId, DateTime fromUtc, DateTime toUtc, int limit = 500, CancellationToken cancellationToken = default);
    Task DeleteFutureAsync(string customerId, DateTime fromUtc, CancellationToken cancellationToken = default);
}

public class OnCallPlanRepository : IOnCallPlanRepository
{
    private readonly Container container;
    private readonly ILogger<OnCallPlanRepository> logger;

    public OnCallPlanRepository(CosmosClient client, string databaseName, string containerName, ILogger<OnCallPlanRepository> logger)
    {
        container = client.GetContainer(databaseName, containerName);
        this.logger = logger;
    }

    public async Task<OnCallPlan?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Id is required", nameof(id));

        try
        {
            var response = await container.ReadItemAsync<OnCallPlan>(id, new PartitionKey(id), cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read on-call plan {Id}", id);
            return null;
        }
    }

    public async Task<IEnumerable<OnCallPlan>> ListAsync(int limit = 100, int offset = 0, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = new QueryDefinition("SELECT * FROM c ORDER BY c.name OFFSET @offset LIMIT @limit")
                .WithParameter("@offset", offset)
                .WithParameter("@limit", limit);

            var results = new List<OnCallPlan>();
            var iterator = container.GetItemQueryIterator<OnCallPlan>(query);
            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(cancellationToken);
                results.AddRange(response);
            }

            return results;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Array.Empty<OnCallPlan>();
        }
    }

    public async Task<OnCallPlan> UpsertAsync(OnCallPlan plan, CancellationToken cancellationToken = default)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));
        if (string.IsNullOrWhiteSpace(plan.Id)) throw new ArgumentException("Plan id is required", nameof(plan));

        plan = plan with { Version = string.IsNullOrWhiteSpace(plan.Version) ? "v1" : plan.Version };
        var response = await container.UpsertItemAsync(plan, new PartitionKey(plan.Id), cancellationToken: cancellationToken);
        return response.Resource;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            await container.DeleteItemAsync<OnCallPlan>(id, new PartitionKey(id), cancellationToken: cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}

public class OnCallTeamRepository : IOnCallTeamRepository
{
    private readonly Container container;
    private readonly ILogger<OnCallTeamRepository> logger;

    public OnCallTeamRepository(CosmosClient client, string databaseName, string containerName, ILogger<OnCallTeamRepository> logger)
    {
        container = client.GetContainer(databaseName, containerName);
        this.logger = logger;
    }

    public async Task<OnCallTeam?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Id is required", nameof(id));

        try
        {
            var response = await container.ReadItemAsync<OnCallTeam>(id, new PartitionKey(id), cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read on-call team {Id}", id);
            return null;
        }
    }

    public async Task<IEnumerable<OnCallTeam>> ListAsync(int limit = 100, int offset = 0, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = new QueryDefinition("SELECT * FROM c ORDER BY c.name OFFSET @offset LIMIT @limit")
                .WithParameter("@offset", offset)
                .WithParameter("@limit", limit);

            var results = new List<OnCallTeam>();
            var iterator = container.GetItemQueryIterator<OnCallTeam>(query);
            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(cancellationToken);
                results.AddRange(response);
            }

            return results;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Array.Empty<OnCallTeam>();
        }
    }

    public async Task<OnCallTeam> UpsertAsync(OnCallTeam team, CancellationToken cancellationToken = default)
    {
        if (team == null) throw new ArgumentNullException(nameof(team));
        if (string.IsNullOrWhiteSpace(team.Id)) throw new ArgumentException("Team id is required", nameof(team));

        var response = await container.UpsertItemAsync(team, new PartitionKey(team.Id), cancellationToken: cancellationToken);
        return response.Resource;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            await container.DeleteItemAsync<OnCallTeam>(id, new PartitionKey(id), cancellationToken: cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}

public class CustomerPlanBindingRepository : ICustomerPlanBindingRepository
{
    private readonly Container container;
    private readonly ILogger<CustomerPlanBindingRepository> logger;

    public CustomerPlanBindingRepository(CosmosClient client, string databaseName, string containerName, ILogger<CustomerPlanBindingRepository> logger)
    {
        container = client.GetContainer(databaseName, containerName);
        this.logger = logger;
    }

    public async Task<CustomerPlanBinding?> GetAsync(string customerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(customerId)) throw new ArgumentException("CustomerId is required", nameof(customerId));

        try
        {
            var response = await container.ReadItemAsync<CustomerPlanBinding>(customerId, new PartitionKey(customerId), cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read binding for customer {CustomerId}", customerId);
            return null;
        }
    }

    public async Task<IEnumerable<CustomerPlanBinding>> ListAsync(int limit = 100, int offset = 0, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = new QueryDefinition("SELECT * FROM c ORDER BY c.customerId OFFSET @offset LIMIT @limit")
                .WithParameter("@offset", offset)
                .WithParameter("@limit", limit);

            var results = new List<CustomerPlanBinding>();
            var iterator = container.GetItemQueryIterator<CustomerPlanBinding>(query);
            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(cancellationToken);
                results.AddRange(response);
            }

            return results;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Array.Empty<CustomerPlanBinding>();
        }
    }

    public async Task<CustomerPlanBinding> UpsertAsync(CustomerPlanBinding binding, CancellationToken cancellationToken = default)
    {
        if (binding == null) throw new ArgumentNullException(nameof(binding));
        if (string.IsNullOrWhiteSpace(binding.CustomerId)) throw new ArgumentException("CustomerId is required", nameof(binding));

        var normalized = binding with { Id = string.IsNullOrWhiteSpace(binding.Id) ? binding.CustomerId : binding.Id };
        var response = await container.UpsertItemAsync(normalized, new PartitionKey(binding.CustomerId), cancellationToken: cancellationToken);
        return response.Resource;
    }

    public async Task<bool> DeleteAsync(string customerId, CancellationToken cancellationToken = default)
    {
        try
        {
            await container.DeleteItemAsync<CustomerPlanBinding>(customerId, new PartitionKey(customerId), cancellationToken: cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}

public class ScheduleSliceRepository : IScheduleSliceRepository
{
    private readonly Container container;
    private readonly ILogger<ScheduleSliceRepository> logger;

    public ScheduleSliceRepository(CosmosClient client, string databaseName, string containerName, ILogger<ScheduleSliceRepository> logger)
    {
        container = client.GetContainer(databaseName, containerName);
        this.logger = logger;
    }

    public async Task UpsertAsync(ScheduleSlice slice, CancellationToken cancellationToken = default)
    {
        if (slice == null) throw new ArgumentNullException(nameof(slice));
        if (string.IsNullOrWhiteSpace(slice.CustomerId)) throw new ArgumentException("CustomerId is required", nameof(slice));

        await container.UpsertItemAsync(slice, new PartitionKey(slice.CustomerId), cancellationToken: cancellationToken);
    }

    public async Task UpsertManyAsync(IEnumerable<ScheduleSlice> slices, CancellationToken cancellationToken = default)
    {
        foreach (var slice in slices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await UpsertAsync(slice, cancellationToken);
        }
    }

    public async Task<IEnumerable<ScheduleSlice>> ListAsync(string customerId, DateTime fromUtc, DateTime toUtc, int limit = 500, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(customerId)) throw new ArgumentException("CustomerId is required", nameof(customerId));

        try
        {
            var query = new QueryDefinition("SELECT * FROM c WHERE c.customerId = @customerId AND c.startUtc < @toUtc AND c.endUtc > @fromUtc ORDER BY c.startUtc")
                .WithParameter("@customerId", customerId)
                .WithParameter("@fromUtc", fromUtc)
                .WithParameter("@toUtc", toUtc);

            var results = new List<ScheduleSlice>();
            var iterator = container.GetItemQueryIterator<ScheduleSlice>(query, requestOptions: new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(customerId)
            });

            var count = 0;
            while (iterator.HasMoreResults && count < limit)
            {
                var response = await iterator.ReadNextAsync(cancellationToken);
                var items = response.Take(limit - count);
                results.AddRange(items);
                count += items.Count();
            }

            return results;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Array.Empty<ScheduleSlice>();
        }
    }

    public async Task DeleteFutureAsync(string customerId, DateTime fromUtc, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(customerId)) throw new ArgumentException("CustomerId is required", nameof(customerId));

        var query = new QueryDefinition("SELECT c.id, c.customerId FROM c WHERE c.customerId = @customerId AND c.startUtc >= @fromUtc")
            .WithParameter("@customerId", customerId)
            .WithParameter("@fromUtc", fromUtc);

        var iterator = container.GetItemQueryIterator<dynamic>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = new PartitionKey(customerId)
        });

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            foreach (var doc in response)
            {
                try
                {
                    string id = doc.id;
                    await container.DeleteItemAsync<ScheduleSlice>(id, new PartitionKey(customerId), cancellationToken: cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to delete future schedule slice for customer {CustomerId}", customerId);
                }
            }
        }
    }
}
