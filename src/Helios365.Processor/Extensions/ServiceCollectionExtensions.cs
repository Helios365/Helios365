using Azure.Communication.Email;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Helios365.Core.Repositories;
using Helios365.Core.Services;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Helios365.Processor.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCosmosDb(this IServiceCollection services, IConfiguration configuration, bool isDevelopment)
    {
        services.AddSingleton<CosmosClient>(sp =>
        {
            var connectionString = configuration["CosmosDbConnectionString"]
                ?? throw new InvalidOperationException("CosmosDbConnectionString is required");
            
            var options = new CosmosClientOptions()
            {
                ApplicationName = "Helios365.Processor",
                // Use Gateway mode in Development (firewall-friendly), Direct mode in Production (better performance)
                ConnectionMode = isDevelopment ? ConnectionMode.Gateway : ConnectionMode.Direct,
                MaxRetryAttemptsOnRateLimitedRequests = isDevelopment ? 1 : 3,
                MaxRetryWaitTimeOnRateLimitedRequests = TimeSpan.FromSeconds(isDevelopment ? 5 : 10),
                RequestTimeout = TimeSpan.FromSeconds(isDevelopment ? 10 : 30)
            };
            
            return new CosmosClient(connectionString, options);
        });

        return services;
    }

    public static IServiceCollection AddRepositories(this IServiceCollection services)
    {
        // Use lazy initialization to speed up startup - repositories are created only when needed
        services.AddScoped<ICustomerRepository>(sp =>
        {
            // Lazy resolution - services resolved only when repository is actually used
            var cosmosClient = sp.GetRequiredService<CosmosClient>();
            var logger = sp.GetRequiredService<ILogger<CustomerRepository>>();
            var configuration = sp.GetRequiredService<IConfiguration>();
            
            var databaseName = configuration["CosmosDbDatabaseName"] ?? "helios365";
            var containerName = configuration["CosmosDbCustomersContainer"] ?? "customers";
            
            return new CustomerRepository(cosmosClient, databaseName, containerName, logger);
        });

        services.AddScoped<IResourceRepository>(sp =>
        {
            var cosmosClient = sp.GetRequiredService<CosmosClient>();
            var logger = sp.GetRequiredService<ILogger<ResourceRepository>>();
            var configuration = sp.GetRequiredService<IConfiguration>();
            
            var databaseName = configuration["CosmosDbDatabaseName"] ?? "helios365";
            var containerName = configuration["CosmosDbResourcesContainer"] ?? "resources";
            
            return new ResourceRepository(cosmosClient, databaseName, containerName, logger);
        });

        services.AddScoped<IAlertRepository>(sp =>
        {
            var cosmosClient = sp.GetRequiredService<CosmosClient>();
            var logger = sp.GetRequiredService<ILogger<AlertRepository>>();
            var configuration = sp.GetRequiredService<IConfiguration>();
            
            var databaseName = configuration["CosmosDbDatabaseName"] ?? "helios365";
            var containerName = configuration["CosmosDbAlertsContainer"] ?? "alerts";
            
            return new AlertRepository(cosmosClient, databaseName, containerName, logger);
        });

        services.AddScoped<IServicePrincipalRepository>(sp =>
        {
            var cosmosClient = sp.GetRequiredService<CosmosClient>();
            var logger = sp.GetRequiredService<ILogger<ServicePrincipalRepository>>();
            var configuration = sp.GetRequiredService<IConfiguration>();
            
            var databaseName = configuration["CosmosDbDatabaseName"] ?? "helios365";
            var containerName = configuration["CosmosDbServicePrincipalsContainer"] ?? "servicePrincipals";
            
            return new ServicePrincipalRepository(cosmosClient, databaseName, containerName, logger);
        });

        services.AddScoped<IActionRepository>(sp =>
        {
            var cosmosClient = sp.GetRequiredService<CosmosClient>();
            var logger = sp.GetRequiredService<ILogger<ActionRepository>>();
            var configuration = sp.GetRequiredService<IConfiguration>();
            
            var databaseName = configuration["CosmosDbDatabaseName"] ?? "helios365";
            var containerName = configuration["CosmosDbActionsContainer"] ?? "actions";
            
            return new ActionRepository(cosmosClient, databaseName, containerName, logger);
        });

        return services;
    }

    public static IServiceCollection AddExternalServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Key Vault
        var keyVaultUri = configuration["KeyVaultUri"];
        if (!string.IsNullOrEmpty(keyVaultUri))
        {
            services.AddSingleton<SecretClient>(sp =>
            {
                return new SecretClient(new Uri(keyVaultUri), new DefaultAzureCredential());
            });
        }

        // HTTP Client for ActionExecutor
        services.AddHttpClient<IActionExecutor, ActionExecutor>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // Email Service - now always uses real Azure Communication Services
        var acsConnectionString = configuration["AzureCommunicationServicesConnectionString"]
            ?? throw new InvalidOperationException("AzureCommunicationServicesConnectionString is required");

        services.AddSingleton<IEmailService>(sp =>
        {
            var fromEmail = configuration["FromEmail"] ?? "alerts@helios365.io";
            var emailClient = new EmailClient(acsConnectionString);
            var logger = sp.GetRequiredService<ILogger<EmailService>>();
            return new EmailService(emailClient, fromEmail, logger);
        });

        return services;
    }
}