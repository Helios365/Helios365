using Azure.Communication.Email;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Helios365.Core.Repositories;
using Helios365.Core.Services;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices((context, services) =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        var configuration = context.Configuration;

        // Cosmos DB
        services.AddSingleton<CosmosClient>(sp =>
        {
            var connectionString = configuration["CosmosDbConnectionString"]
                ?? throw new InvalidOperationException("CosmosDbConnectionString is required");
            return new CosmosClient(connectionString);
        });

        // Repositories
        services.AddScoped<ICustomerRepository>(sp =>
        {
            var cosmosClient = sp.GetRequiredService<CosmosClient>();
            var logger = sp.GetRequiredService<ILogger<CustomerRepository>>();
            var databaseName = configuration["CosmosDbDatabaseName"] ?? "helios365";
            var containerName = configuration["CosmosDbCustomersContainer"] ?? "customers";
            return new CustomerRepository(cosmosClient, databaseName, containerName, logger);
        });

        services.AddScoped<IResourceRepository>(sp =>
        {
            var cosmosClient = sp.GetRequiredService<CosmosClient>();
            var logger = sp.GetRequiredService<ILogger<ResourceRepository>>();
            var databaseName = configuration["CosmosDbDatabaseName"] ?? "helios365";
            var containerName = configuration["CosmosDbResourcesContainer"] ?? "resources";
            return new ResourceRepository(cosmosClient, databaseName, containerName, logger);
        });

        services.AddScoped<IAlertRepository>(sp =>
        {
            var cosmosClient = sp.GetRequiredService<CosmosClient>();
            var logger = sp.GetRequiredService<ILogger<AlertRepository>>();
            var databaseName = configuration["CosmosDbDatabaseName"] ?? "helios365";
            var containerName = configuration["CosmosDbAlertsContainer"] ?? "alerts";
            return new AlertRepository(cosmosClient, databaseName, containerName, logger);
        });

        services.AddScoped<IServicePrincipalRepository>(sp =>
        {
            var cosmosClient = sp.GetRequiredService<CosmosClient>();
            var logger = sp.GetRequiredService<ILogger<ServicePrincipalRepository>>();
            var databaseName = configuration["CosmosDbDatabaseName"] ?? "helios365";
            var containerName = configuration["CosmosDbServicePrincipalsContainer"] ?? "servicePrincipals";
            return new ServicePrincipalRepository(cosmosClient, databaseName, containerName, logger);
        });

        services.AddScoped<IActionRepository>(sp =>
        {
            var cosmosClient = sp.GetRequiredService<CosmosClient>();
            var logger = sp.GetRequiredService<ILogger<ActionRepository>>();
            var databaseName = configuration["CosmosDbDatabaseName"] ?? "helios365";
            var containerName = configuration["CosmosDbActionsContainer"] ?? "actions";
            return new ActionRepository(cosmosClient, databaseName, containerName, logger);
        });

        // Services
        services.AddSingleton(sp =>
        {
            var keyVaultUri = configuration["KeyVaultUri"] 
                ?? throw new InvalidOperationException("KeyVaultUri is required");
            return new SecretClient(new Uri(keyVaultUri), new DefaultAzureCredential());
        });

        services.AddHttpClient<IActionExecutor, ActionExecutor>();

        services.AddSingleton<IEmailService>(sp =>
        {
            var connectionString = configuration["AzureCommunicationServicesConnectionString"]
                ?? throw new InvalidOperationException("AzureCommunicationServicesConnectionString is required");
            var fromEmail = configuration["FromEmail"] ?? "alerts@helios365.io";
            var emailClient = new EmailClient(connectionString);
            var logger = sp.GetRequiredService<ILogger<EmailService>>();
            return new EmailService(emailClient, fromEmail, logger);
        });
    })
    .Build();

host.Run();
