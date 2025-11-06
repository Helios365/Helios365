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
    public static IServiceCollection AddCosmosDb(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration["CosmosDbConnectionString"];
        
        // Only register if connection string is valid
        if (!string.IsNullOrEmpty(connectionString) && !connectionString.Contains("localhost:8081"))
        {
            // For production - use the actual connection string
            services.AddSingleton<CosmosClient>(sp =>
            {
                var options = new CosmosClientOptions
                {
                    ApplicationName = "Helios365.Processor",
                    MaxRetryAttemptsOnRateLimitedRequests = 3,
                    MaxRetryWaitTimeOnRateLimitedRequests = TimeSpan.FromSeconds(10),
                    RequestTimeout = TimeSpan.FromSeconds(10)
                };
                return new CosmosClient(connectionString, options);
            });
        }
        else if (!string.IsNullOrEmpty(connectionString))
        {
            // For local development with emulator - optimized for fast startup
            services.AddSingleton<CosmosClient>(sp =>
            {
                var cosmosClientOptions = new CosmosClientOptions
                {
                    ApplicationName = "Helios365.Processor.Dev",
                    HttpClientFactory = () => new HttpClient(new HttpClientHandler()
                    {
                        ServerCertificateCustomValidationCallback = (req, cert, chain, errors) => true
                    }),
                    ConnectionMode = ConnectionMode.Gateway,
                    // Optimize for local development
                    RequestTimeout = TimeSpan.FromSeconds(5), // Shorter timeout for local
                    MaxRetryAttemptsOnRateLimitedRequests = 1, // Fewer retries for local
                    MaxRetryWaitTimeOnRateLimitedRequests = TimeSpan.FromSeconds(1),
                    OpenTcpConnectionTimeout = TimeSpan.FromSeconds(5),
                    IdleTcpConnectionTimeout = TimeSpan.FromMinutes(10),
                    MaxRequestsPerTcpConnection = 10,
                    MaxTcpConnectionsPerEndpoint = 2,
                    EnableContentResponseOnWrite = false // Skip reading response body for writes
                };
                return new CosmosClient(connectionString, cosmosClientOptions);
            });
        }
        else
        {
            // Mock for testing - you could implement ICosmosClient interface
            throw new InvalidOperationException("CosmosDbConnectionString is required");
        }
        
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
        // KeyVault - only if properly configured
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

        // Email Service - with fallback for local development
        var acsConnectionString = configuration["AzureCommunicationServicesConnectionString"];
        if (!string.IsNullOrEmpty(acsConnectionString))
        {
            services.AddSingleton<IEmailService>(sp =>
            {
                var fromEmail = configuration["FromEmail"] ?? "alerts@helios365.io";
                var emailClient = new EmailClient(acsConnectionString);
                var logger = sp.GetRequiredService<ILogger<EmailService>>();
                return new EmailService(emailClient, fromEmail, logger);
            });
        }
        else
        {
            // Local development mock
            services.AddSingleton<IEmailService, LocalDevEmailService>();
        }

        return services;
    }
}

// Simple local development email service
public class LocalDevEmailService : IEmailService
{
    private readonly ILogger<LocalDevEmailService> _logger;

    public LocalDevEmailService(ILogger<LocalDevEmailService> logger)
    {
        _logger = logger;
    }

    public Task SendEscalationEmailAsync(
        Core.Models.Alert alert, 
        Core.Models.Resource? resource, 
        Core.Models.Customer customer, 
        List<Core.Models.ActionBase> attemptedActions, 
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("LOCAL DEV: Email notification for alert {AlertId} from {CustomerName}", 
            alert.Id, customer.Name);
        return Task.CompletedTask;
    }
}