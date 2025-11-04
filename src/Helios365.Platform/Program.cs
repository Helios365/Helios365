using Helios365.Core.Repositories;
using Microsoft.Azure.Cosmos;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddRazorPages();

// Cosmos DB
builder.Services.AddSingleton<CosmosClient>(sp =>
{
    var connectionString = builder.Configuration["CosmosDb:ConnectionString"]
        ?? throw new InvalidOperationException("CosmosDb:ConnectionString is required");
    return new CosmosClient(connectionString);
});

// Repositories
builder.Services.AddScoped<ICustomerRepository>(sp =>
{
    var cosmosClient = sp.GetRequiredService<CosmosClient>();
    var logger = sp.GetRequiredService<ILogger<CustomerRepository>>();
    var databaseName = builder.Configuration["CosmosDb:DatabaseName"] ?? "helios365";
    var containerName = builder.Configuration["CosmosDb:CustomersContainer"] ?? "customers";
    return new CustomerRepository(cosmosClient, databaseName, containerName, logger);
});

builder.Services.AddScoped<IAlertRepository>(sp =>
{
    var cosmosClient = sp.GetRequiredService<CosmosClient>();
    var logger = sp.GetRequiredService<ILogger<AlertRepository>>();
    var databaseName = builder.Configuration["CosmosDb:DatabaseName"] ?? "helios365";
    var containerName = builder.Configuration["CosmosDb:AlertsContainer"] ?? "alerts";
    return new AlertRepository(cosmosClient, databaseName, containerName, logger);
});

builder.Services.AddScoped<IResourceRepository>(sp =>
{
    var cosmosClient = sp.GetRequiredService<CosmosClient>();
    var logger = sp.GetRequiredService<ILogger<ResourceRepository>>();
    var databaseName = builder.Configuration["CosmosDb:DatabaseName"] ?? "helios365";
    var containerName = builder.Configuration["CosmosDb:ResourcesContainer"] ?? "resources";
    return new ResourceRepository(cosmosClient, databaseName, containerName, logger);
});

builder.Services.AddScoped<IServicePrincipalRepository>(sp =>
{
    var cosmosClient = sp.GetRequiredService<CosmosClient>();
    var logger = sp.GetRequiredService<ILogger<ServicePrincipalRepository>>();
    var databaseName = builder.Configuration["CosmosDb:DatabaseName"] ?? "helios365";
    var containerName = builder.Configuration["CosmosDb:ServicePrincipalsContainer"] ?? "servicePrincipals";
    return new ServicePrincipalRepository(cosmosClient, databaseName, containerName, logger);
});

builder.Services.AddScoped<IActionRepository>(sp =>
{
    var cosmosClient = sp.GetRequiredService<CosmosClient>();
    var logger = sp.GetRequiredService<ILogger<ActionRepository>>();
    var databaseName = builder.Configuration["CosmosDb:DatabaseName"] ?? "helios365";
    var containerName = builder.Configuration["CosmosDb:ActionsContainer"] ?? "actions";
    return new ActionRepository(cosmosClient, databaseName, containerName, logger);
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.MapRazorPages();

app.Run();
