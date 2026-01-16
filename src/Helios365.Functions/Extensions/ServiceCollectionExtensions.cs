using System.Net.Http;
using Azure.Communication.Email;
using Azure.Communication.Sms;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Azure.ResourceManager.Resources;
using Helios365.Core.Contracts;
using Helios365.Core.Repositories;
using Helios365.Core.Services;
using Helios365.Core.Services.Clients;
using Helios365.Core.Services.Handlers;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Helios365.Functions.Extensions;

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

        services.AddScoped<IWebTestRepository>(sp =>
        {
            var cosmosClient = sp.GetRequiredService<CosmosClient>();
            var logger = sp.GetRequiredService<ILogger<WebTestRepository>>();
            var configuration = sp.GetRequiredService<IConfiguration>();

            var databaseName = configuration["CosmosDbDatabaseName"] ?? "helios365";
            var containerName = configuration["CosmosDbPingTestsContainer"] ?? "pingTests";

            return new WebTestRepository(cosmosClient, databaseName, containerName, logger);
        });

        services.AddScoped<IUserRepository>(sp =>
        {
            var cosmosClient = sp.GetRequiredService<CosmosClient>();
            var logger = sp.GetRequiredService<ILogger<UserRepository>>();
            var configuration = sp.GetRequiredService<IConfiguration>();

            var databaseName = configuration["CosmosDbDatabaseName"] ?? "helios365";
            var containerName = configuration["CosmosDbUsersContainer"] ?? "users";

            return new UserRepository(cosmosClient, databaseName, containerName, logger);
        });

        services.AddScoped<IOnCallPlanRepository>(sp =>
        {
            var cosmosClient = sp.GetRequiredService<CosmosClient>();
            var logger = sp.GetRequiredService<ILogger<OnCallPlanRepository>>();
            var configuration = sp.GetRequiredService<IConfiguration>();

            var databaseName = configuration["CosmosDbDatabaseName"] ?? "helios365";
            var containerName = configuration["CosmosDbOnCallPlansContainer"] ?? "onCallPlans";

            return new OnCallPlanRepository(cosmosClient, databaseName, containerName, logger);
        });

        services.AddScoped<IOnCallTeamRepository>(sp =>
        {
            var cosmosClient = sp.GetRequiredService<CosmosClient>();
            var logger = sp.GetRequiredService<ILogger<OnCallTeamRepository>>();
            var configuration = sp.GetRequiredService<IConfiguration>();

            var databaseName = configuration["CosmosDbDatabaseName"] ?? "helios365";
            var containerName = configuration["CosmosDbOnCallTeamsContainer"] ?? "onCallTeams";

            return new OnCallTeamRepository(cosmosClient, databaseName, containerName, logger);
        });

        services.AddScoped<ICustomerPlanBindingRepository>(sp =>
        {
            var cosmosClient = sp.GetRequiredService<CosmosClient>();
            var logger = sp.GetRequiredService<ILogger<CustomerPlanBindingRepository>>();
            var configuration = sp.GetRequiredService<IConfiguration>();

            var databaseName = configuration["CosmosDbDatabaseName"] ?? "helios365";
            var containerName = configuration["CosmosDbPlanBindingsContainer"] ?? "planBindings";

            return new CustomerPlanBindingRepository(cosmosClient, databaseName, containerName, logger);
        });

        services.AddScoped<IScheduleSliceRepository>(sp =>
        {
            var cosmosClient = sp.GetRequiredService<CosmosClient>();
            var logger = sp.GetRequiredService<ILogger<ScheduleSliceRepository>>();
            var configuration = sp.GetRequiredService<IConfiguration>();

            var databaseName = configuration["CosmosDbDatabaseName"] ?? "helios365";
            var containerName = configuration["CosmosDbScheduleSlicesContainer"] ?? "scheduleSlices";

            return new ScheduleSliceRepository(cosmosClient, databaseName, containerName, logger);
        });

        return services;
    }

    public static IServiceCollection AddExternalServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Key Vault
        var keyVaultUri = configuration["KeyVaultUri"];
        if (string.IsNullOrEmpty(keyVaultUri))
        {
            throw new InvalidOperationException("KeyVaultUri is required for external service integrations.");
        }

        services.Configure<CommunicationServiceOptions>(options =>
        {
            options.ConnectionString = configuration["CommunicationServices:ConnectionString"]
                ?? configuration["AzureCommunicationServicesConnectionString"]
                ?? string.Empty;
            options.EmailSender = configuration["CommunicationServices:EmailSender"]
                ?? configuration["FromEmail"]
                ?? string.Empty;
            options.SmsSender = configuration["CommunicationServices:SmsSender"]
                ?? configuration["FromSms"]
                ?? string.Empty;
        });

        services.AddSingleton<EmailClient>(sp =>
        {
            var communicationOptions = sp.GetRequiredService<IOptions<CommunicationServiceOptions>>().Value;
            if (string.IsNullOrWhiteSpace(communicationOptions.ConnectionString))
            {
                throw new InvalidOperationException("CommunicationServices:ConnectionString is required to send email or SMS.");
            }

            return new EmailClient(communicationOptions.ConnectionString);
        });

        services.AddSingleton<SmsClient>(sp =>
        {
            var communicationOptions = sp.GetRequiredService<IOptions<CommunicationServiceOptions>>().Value;
            if (string.IsNullOrWhiteSpace(communicationOptions.ConnectionString))
            {
                throw new InvalidOperationException("CommunicationServices:ConnectionString is required to send email or SMS.");
            }

            return new SmsClient(communicationOptions.ConnectionString);
        });

        services.AddSingleton<SecretClient>(_ => new SecretClient(new Uri(keyVaultUri), new DefaultAzureCredential()));

        services.AddSingleton(sp => new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60)
        });

        services.AddScoped<IWebTestService, WebTestService>();
        services.AddSingleton<IMetricsClient, MetricsClient>();

        services.AddSingleton<ISecretRepository>(sp =>
        {
            var secretClient = sp.GetRequiredService<SecretClient>();
            var logger = sp.GetRequiredService<ILogger<SecretRepository>>();
            return new SecretRepository(secretClient, logger);
        });

        services.AddSingleton<ICredentialProvider, CredentialProvider>();
        services.AddSingleton<IArmClientFactory, ArmClientFactory>();
        services.AddSingleton<IResourceGraphClient, ResourceGraphClient>();
        services.AddSingleton<IResourceHandler, AppServiceResourceHandler>();
        services.AddSingleton<IResourceHandler, VirtualMachineResourceHandler>();
        services.AddSingleton<IResourceHandler, MySqlResourceHandler>();
        services.AddSingleton<IResourceHandler, ServiceBusResourceHandler>();

        services.AddScoped<IResourceService, ResourceService>();
        services.AddScoped<IResourceSyncService, ResourceSyncService>();
        services.AddScoped<IAlertService, AlertService>();
        services.AddScoped<INotificationService, NotificationService>();

        services.AddScoped<IOnCallScheduleGenerator, OnCallScheduleGenerator>();
        services.AddScoped<IOnCallScheduleService, OnCallScheduleService>();

        return services;
    }
}
