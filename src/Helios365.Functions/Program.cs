using Helios365.Functions.Extensions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices((context, services) =>
    {
        // Only add Application Insights in non-development environments
        var isDevelopment = context.HostingEnvironment.IsDevelopment();
        if (!isDevelopment)
        {
            services.AddApplicationInsightsTelemetryWorkerService();
            services.ConfigureFunctionsApplicationInsights();
        }

        var configuration = context.Configuration;

        // Clean, organized service registration
        services.AddCosmosDb(configuration, context.HostingEnvironment.IsDevelopment());
        services.AddRepositories();
        services.AddExternalServices(configuration);
    })
    .ConfigureLogging((context, logging) =>
    {
        // Optimize logging for development
        if (context.HostingEnvironment.IsDevelopment())
        {
            logging.SetMinimumLevel(LogLevel.Information);
            // Remove some noisy loggers during development
            logging.AddFilter("Microsoft.Azure.Functions.Worker", LogLevel.Warning);
            logging.AddFilter("Microsoft.Azure.Cosmos", LogLevel.Warning);
            logging.AddFilter("Azure.Core", LogLevel.Warning);
        }
    })
    .Build();

host.Run();
