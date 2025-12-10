using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace Helios365.Core.Repositories;

public interface IUserRepository
{
    Task<Models.User?> GetAsync(string id, CancellationToken cancellationToken = default);
    Task UpsertAsync(Models.User profile, CancellationToken cancellationToken = default);
}

public class UserRepository : IUserRepository
{
    private readonly Container container;
    private readonly ILogger<UserRepository> logger;

    public UserRepository(CosmosClient client, string databaseName, string containerName, ILogger<UserRepository> logger)
    {
        container = client.GetContainer(databaseName, containerName);
        this.logger = logger;
    }

    public async Task<Models.User?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Id is required", nameof(id));
        }

        try
        {
            var response = await container.ReadItemAsync<Models.User>(id, new PartitionKey(id), cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read directory user profile {Id}", id);
            return null;
        }
    }

    public async Task UpsertAsync(Models.User profile, CancellationToken cancellationToken = default)
    {
        if (profile == null) throw new ArgumentNullException(nameof(profile));
        if (string.IsNullOrWhiteSpace(profile.Id)) throw new ArgumentException("Profile id is required", nameof(profile));

        profile.LastSyncedUtc = DateTimeOffset.UtcNow;

        await container.UpsertItemAsync(profile, new PartitionKey(profile.Id), cancellationToken: cancellationToken);
    }
}
