using System.Net.Http;
using Azure.Communication.Email;
using Azure.Communication.Sms;
using Helios365.Core.Repositories;
using Helios365.Core.Services.Clients;
using Helios365.Core.Services.Handlers;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Azure.Security.KeyVault.Secrets;
using Azure.Identity;
using Azure.ResourceManager.Resources;
using Azure.Core;
using Microsoft.Graph;
using Helios365.Core.Models;
using Helios365.Core.Contracts;
using Helios365.Core.Services;
using Microsoft.Extensions.Options;

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
        services.AddSingleton(new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60)
        });

        services.Configure<CommunicationServiceOptions>(options =>
        {
            var communicationSection = configuration.GetSection("CommunicationServices");
            options.ConnectionString = communicationSection["ConnectionString"]
                ?? configuration["AzureCommunicationServicesConnectionString"]
                ?? string.Empty;
            options.EmailSender = communicationSection["EmailSender"]
                ?? configuration["FromEmail"]
                ?? string.Empty;
            options.SmsSender = communicationSection["SmsSender"]
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

        services.AddScoped<ICommunicationService, CommunicationService>();

        services.AddScoped<IWebTestService, WebTestService>();
        services.AddScoped<IMetricsClient, MetricsClient>();
        services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential());
        services.AddSingleton(sp =>
        {
            var credential = sp.GetRequiredService<TokenCredential>();
            var scopes = new[] { "https://graph.microsoft.com/.default" };
            return new GraphServiceClient(credential, scopes);
        });
        services.AddScoped<IDirectoryService, DirectoryService>();
        services.AddScoped<IDirectorySyncService, DirectorySyncService>();
        services.Configure<DirectoryServiceOptions>(configuration.GetSection("DirectoryService"));

        // Key Vault Secret repository
        var keyVaultUri = configuration["KeyVault:VaultUri"];
        if (!string.IsNullOrEmpty(keyVaultUri))
        {
            services.AddSingleton(new SecretClient(new Uri(keyVaultUri), new DefaultAzureCredential()));
            services.AddScoped<ISecretRepository, SecretRepository>();
            services.AddScoped<ICredentialProvider, CredentialProvider>();
            services.AddScoped<IArmClientFactory, ArmClientFactory>();
            services.AddScoped<IResourceGraphClient, ResourceGraphClient>();
            services.AddScoped<IResourceHandler, AppServiceResourceHandler>();
            services.AddScoped<IResourceHandler, VirtualMachineResourceHandler>();
            services.AddScoped<IResourceHandler, MySqlResourceHandler>();
            services.AddScoped<IResourceHandler, ServiceBusResourceHandler>();
            services.AddScoped<IResourceService, ResourceService>();
            services.AddScoped<IResourceSyncService, ResourceSyncService>();
        }
        else
        {
            // For environments without Key Vault, resource discovery won't be available.
        }

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

        services.AddScoped<IWebTestRepository>(sp =>
        {
            var cosmosClient = sp.GetRequiredService<CosmosClient>();
            var logger = sp.GetRequiredService<ILogger<WebTestRepository>>();
            var databaseName = configuration["CosmosDb:DatabaseName"] ?? "helios365";
            var containerName = configuration["CosmosDb:WebTestsContainer"] ?? "webTests";
            return new WebTestRepository(cosmosClient, databaseName, containerName, logger);
        });

        services.AddScoped<IUserRepository>(sp =>
        {
            var cosmosClient = sp.GetRequiredService<CosmosClient>();
            var logger = sp.GetRequiredService<ILogger<UserRepository>>();
            var databaseName = configuration["CosmosDb:DatabaseName"] ?? "helios365";
            var containerName = configuration["CosmosDb:UsersContainer"] ?? "users";
            return new UserRepository(cosmosClient, databaseName, containerName, logger);
        });

        services.AddScoped<IOnCallPlanRepository>(sp =>
        {
            var cosmosClient = sp.GetRequiredService<CosmosClient>();
            var logger = sp.GetRequiredService<ILogger<OnCallPlanRepository>>();
            var databaseName = configuration["CosmosDb:DatabaseName"] ?? "helios365";
            var containerName = configuration["CosmosDb:OnCallPlansContainer"] ?? "onCallPlans";
            return new OnCallPlanRepository(cosmosClient, databaseName, containerName, logger);
        });

        services.AddScoped<IOnCallTeamRepository>(sp =>
        {
            var cosmosClient = sp.GetRequiredService<CosmosClient>();
            var logger = sp.GetRequiredService<ILogger<OnCallTeamRepository>>();
            var databaseName = configuration["CosmosDb:DatabaseName"] ?? "helios365";
            var containerName = configuration["CosmosDb:OnCallTeamsContainer"] ?? "onCallTeams";
            return new OnCallTeamRepository(cosmosClient, databaseName, containerName, logger);
        });

        services.AddScoped<ICustomerPlanBindingRepository>(sp =>
        {
            var cosmosClient = sp.GetRequiredService<CosmosClient>();
            var logger = sp.GetRequiredService<ILogger<CustomerPlanBindingRepository>>();
            var databaseName = configuration["CosmosDb:DatabaseName"] ?? "helios365";
            var containerName = configuration["CosmosDb:PlanBindingsContainer"] ?? "planBindings";
            return new CustomerPlanBindingRepository(cosmosClient, databaseName, containerName, logger);
        });

        services.AddScoped<IScheduleSliceRepository>(sp =>
        {
            var cosmosClient = sp.GetRequiredService<CosmosClient>();
            var logger = sp.GetRequiredService<ILogger<ScheduleSliceRepository>>();
            var databaseName = configuration["CosmosDb:DatabaseName"] ?? "helios365";
            var containerName = configuration["CosmosDb:ScheduleSlicesContainer"] ?? "scheduleSlices";
            return new ScheduleSliceRepository(cosmosClient, databaseName, containerName, logger);
        });

        services.AddScoped<IOnCallScheduleGenerator, OnCallScheduleGenerator>();
        services.AddScoped<IOnCallScheduleService, OnCallScheduleService>();

        return services;
    }
}
