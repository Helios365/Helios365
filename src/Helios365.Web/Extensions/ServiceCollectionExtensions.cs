using Helios365.Core.Repositories;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Helios365.Web.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCosmosDb(this IServiceCollection services, IConfiguration configuration, bool isDevelopment)
    {
        services.AddSingleton<CosmosClient>(sp =>
        {
            var connectionString = configuration["CosmosDb:ConnectionString"];
            
            // For development without CosmosDB, we can use the Cosmos DB Emulator or skip
            if (string.IsNullOrEmpty(connectionString))
            {
                if (isDevelopment)
                {
                    // Use Cosmos DB Emulator connection string for local development
                    connectionString = "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";
                }
                else
                {
                    throw new InvalidOperationException("CosmosDb:ConnectionString is required");
                }
            }
            
            var options = new CosmosClientOptions()
            {
                ApplicationName = "Helios365.Web",
                // Use Gateway mode in Development (firewall-friendly), Direct mode in Production (better performance)
                ConnectionMode = isDevelopment ? ConnectionMode.Gateway : ConnectionMode.Direct,
                MaxRetryAttemptsOnRateLimitedRequests = isDevelopment ? 1 : 3,
                MaxRetryWaitTimeOnRateLimitedRequests = TimeSpan.FromSeconds(isDevelopment ? 5 : 10),
                RequestTimeout = TimeSpan.FromSeconds(isDevelopment ? 5 : 30)
            };
            
            return new CosmosClient(connectionString, options);
        });

        return services;
    }

    public static IServiceCollection AddRepositories(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICustomerRepository>(sp =>
        {
            var cosmosClient = sp.GetRequiredService<CosmosClient>();
            var logger = sp.GetRequiredService<ILogger<CustomerRepository>>();
            var databaseName = configuration["CosmosDb:DatabaseName"] ?? "helios365";
            var containerName = configuration["CosmosDb:CustomersContainer"] ?? "customers";
            return new CustomerRepository(cosmosClient, databaseName, containerName, logger);
        });

        services.AddScoped<IAlertRepository>(sp =>
        {
            var cosmosClient = sp.GetRequiredService<CosmosClient>();
            var logger = sp.GetRequiredService<ILogger<AlertRepository>>();
            var databaseName = configuration["CosmosDb:DatabaseName"] ?? "helios365";
            var containerName = configuration["CosmosDb:AlertsContainer"] ?? "alerts";
            return new AlertRepository(cosmosClient, databaseName, containerName, logger);
        });

        services.AddScoped<IResourceRepository>(sp =>
        {
            var cosmosClient = sp.GetRequiredService<CosmosClient>();
            var logger = sp.GetRequiredService<ILogger<ResourceRepository>>();
            var databaseName = configuration["CosmosDb:DatabaseName"] ?? "helios365";
            var containerName = configuration["CosmosDb:ResourcesContainer"] ?? "resources";
            return new ResourceRepository(cosmosClient, databaseName, containerName, logger);
        });

        services.AddScoped<IServicePrincipalRepository>(sp =>
        {
            var cosmosClient = sp.GetRequiredService<CosmosClient>();
            var logger = sp.GetRequiredService<ILogger<ServicePrincipalRepository>>();
            var databaseName = configuration["CosmosDb:DatabaseName"] ?? "helios365";
            var containerName = configuration["CosmosDb:ServicePrincipalsContainer"] ?? "servicePrincipals";
            return new ServicePrincipalRepository(cosmosClient, databaseName, containerName, logger);
        });

        services.AddScoped<IActionRepository>(sp =>
        {
            var cosmosClient = sp.GetRequiredService<CosmosClient>();
            var logger = sp.GetRequiredService<ILogger<ActionRepository>>();
            var databaseName = configuration["CosmosDb:DatabaseName"] ?? "helios365";
            var containerName = configuration["CosmosDb:ActionsContainer"] ?? "actions";
            return new ActionRepository(cosmosClient, databaseName, containerName, logger);
        });

        return services;
    }
}