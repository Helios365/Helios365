using Helios365.Processor.Extensions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices((context, services) =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        var configuration = context.Configuration;

        // Clean, organized service registration
        services.AddCosmosDb(configuration);
        services.AddRepositories();
        services.AddExternalServices(configuration);
    })
    .Build();

host.Run();
