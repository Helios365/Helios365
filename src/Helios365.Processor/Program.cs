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

        // Configuration
        var configuration = context.Configuration;

        // Cosmos DB
        services.AddSingleton<CosmosClient>(sp =>
        {
            var connectionString = configuration["CosmosDbConnectionString"]
                ?? throw new InvalidOperationException("CosmosDbConnectionString is required");
            return new CosmosClient(connectionString);
        });

        // Repositories
        services.AddScoped<IAlertRepository>(sp =>
        {
            var cosmosClient = sp.GetRequiredService<CosmosClient>();
            var logger = sp.GetRequiredService<ILogger<AlertRepository>>();
            var databaseName = configuration["CosmosDbDatabaseName"] ?? "helios365";
            var containerName = configuration["CosmosDbAlertsContainer"] ?? "alerts";
            return new AlertRepository(cosmosClient, databaseName, containerName, logger);
        });

        services.AddScoped<ICustomerRepository>(sp =>
        {
            var cosmosClient = sp.GetRequiredService<CosmosClient>();
            var logger = sp.GetRequiredService<ILogger<CustomerRepository>>();
            var databaseName = configuration["CosmosDbDatabaseName"] ?? "helios365";
            var containerName = configuration["CosmosDbCustomersContainer"] ?? "customers";
            return new CustomerRepository(cosmosClient, databaseName, containerName, logger);
        });

        // Services
        services.AddHttpClient<IHealthCheckService, HealthCheckService>();
    })
    .Build();

host.Run();
