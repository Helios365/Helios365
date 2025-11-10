# Contributing to Helios365

This document provides guidelines for contributing to the Helios365 project, including architecture patterns, coding conventions, and development workflows.

## Architecture & Project Structure

### Multi-Project Solution
- **Helios365.Core**: Shared models, repositories, services, and exceptions
- **Helios365.Platform**: ASP.NET Core web application (Razor Pages)
- **Helios365.Processor**: Azure Functions with Durable Functions support

### Design Patterns
- **Repository Pattern**: Use existing interfaces in `Helios365.Core.Repositories`
- **Dependency Injection**: Register services in `Program.cs`, use constructor injection
- **Azure-First Architecture**: Prefer Azure services for all infrastructure needs

## Code Style & Conventions

### C# Guidelines
- **Language Version**: C# 12 with nullable reference types enabled
- **Async Operations**: Use `async/await` for all I/O operations with `ConfigureAwait(false)` in libraries
- **Naming Conventions**:
  - PascalCase for public members, methods, and properties
  - camelCase for private fields and local variables
  - Interfaces prefixed with `I` (e.g., `ICustomerRepository`)

### Error Handling
- Use custom exceptions from `Helios365.Core.Exceptions`
- Return `Problem()` responses for API errors
- Log exceptions with structured logging using `ILogger<T>`

### Documentation
- Use XML comments for public APIs
- Focus inline comments on **why**, not **what**
- Keep README files updated for each project

## Azure Functions Guidelines

### Function App Configuration
- **Runtime**: .NET 8 Isolated Worker Model
- **Orchestration**: Use Durable Functions for complex workflows
- **Triggers**: HTTP triggers for APIs, Timer triggers for scheduled tasks
- **Dependency Injection**: Use host builder pattern in `Program.cs`

### Function Structure
```
Triggers/          # HTTP, Timer, and other trigger functions
Activities/        # Durable Function activities
Orchestrators/     # Durable Function orchestrators
```

## Database & Storage Guidelines

### Cosmos DB
- **Environment Configuration**: Serverless for dev, provisioned throughput for production
- **Container Naming**: Use plural nouns (`customers`, `alerts`, `resources`)
- **Repository Pattern**: Inherit from existing repositories in Core project
- **Async Operations**: All Cosmos DB operations must be async
- **Partitioning**: Consider partition key strategy for scalability

### Example Repository Implementation
```csharp
public class CustomerRepository : ICustomerRepository
{
    private readonly CosmosClient _cosmosClient;
    private readonly ILogger<CustomerRepository> _logger;
    
    // Always use async methods
    public async Task<Customer> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        // Implementation with proper error handling
    }
}
```

## Security & Configuration Management

### Configuration Pattern
**Files in Source Control** (safe, no secrets):
- `appsettings.json` - Base configuration with empty values
- `appsettings.Development.json` - Development template with placeholders
- `local.settings.json.example` - Azure Functions template

**Local Override Files** (gitignored, contains secrets):
- `appsettings.Development.local.json` - Local ASP.NET Core overrides
- `local.settings.json` - Local Azure Functions settings

### Secret Management
- **Development**: Use local override files (excluded from git)
- **Production**: Use Azure Key Vault with Managed Identity
- **Never commit secrets**: Double-check `.gitignore` includes override files

### Authentication
- Use Azure AD/Entra ID for user authentication
- Use Managed Identity for service-to-service authentication in Azure

## Infrastructure as Code

### Bicep Templates
- **Single File Approach**: Use `infrastructure/deploy.bicep` for all resources
- **Environment-Specific**: Parameter files for dev/staging/prod environments
- **Resource Naming**: `{environment}-{appName}-{uniqueSuffix}-{resourceType}`
- **Deploy to Azure**: Maintain `azuredeploy.json` compiled from Bicep

### Azure Resources
- **Cosmos DB**: Document database with serverless/provisioned options
- **Key Vault**: Secret and certificate management
- **Communication Services**: Email and SMS notifications
- **Application Insights**: Telemetry and monitoring
- **App Service**: Web application hosting
- **Function App**: Serverless compute platform

## Testing Guidelines

### Test Structure
```
tests/
  Helios365.Core.Tests/          # Unit tests for shared components
  Helios365.Platform.Tests/      # Integration tests for web app
  Helios365.Processor.Tests/     # Tests for Azure Functions
```

### Testing Approach
- **Unit Tests**: Mock external dependencies, test business logic
- **Integration Tests**: Use actual Azure dev infrastructure
- **Repository Tests**: Mock Cosmos DB client for unit tests, real Cosmos for integration
- **Use xUnit**: Standard testing framework across all projects

## Development Workflow

### Initial Setup
1. Clone repository and build solution
2. Copy configuration templates to local override files:
   ```powershell
   # ASP.NET Core
   Copy-Item "appsettings.Development.json" "appsettings.Development.local.json"
   
   # Azure Functions
   Copy-Item "local.settings.json.example" "local.settings.json"
   ```
3. Update local files with actual Azure resource connection strings
4. Grant Key Vault access to your user account

### Local Development
- **Use Azure Dev Infrastructure**: Connect to real Cosmos DB, Key Vault, etc.
- **No Emulators**: Prefer actual Azure services for realistic testing
- **Configuration Loading**: Local override files automatically take precedence

### Running the Application
```powershell
# ASP.NET Core Platform
cd src/Helios365.Platform
dotnet run

# Azure Functions
cd src/Helios365.Processor
func start
```

## File Organization Principles

### Project Dependencies
```
Core (no dependencies)
  ↑
Platform (references Core)
  ↑
Processor (references Core)
```

### Configuration Files
- Keep templates in source control with placeholder values
- Use descriptive placeholder names (`YOUR_COSMOS_CONNECTION_STRING`)
- Maintain parallel structure between ASP.NET Core and Functions configuration

### Documentation Structure
```
docs/
  CONFIGURATION_STRATEGY.md    # Configuration management details
  FUNCTIONS_CONFIGURATION.md   # Azure Functions specific config
  LOCAL_DEVELOPMENT.md         # Development environment setup
```

## Adding New Features

### Checklist for New Features
1. **Models**: Add to `Helios365.Core/Models` if shared across projects
2. **Repository**: Add interface to Core, implementation to specific project
3. **Configuration**: Update both template files and local override files
4. **Tests**: Add appropriate unit and integration tests
5. **Documentation**: Update relevant documentation files
6. **Infrastructure**: Update Bicep templates if new Azure resources needed

### Service Registration Example
```csharp
// In Program.cs
builder.Services.AddScoped<ICustomerRepository>(sp =>
{
    var cosmosClient = sp.GetRequiredService<CosmosClient>();
    var logger = sp.GetRequiredService<ILogger<CustomerRepository>>();
    var config = sp.GetRequiredService<IConfiguration>();
    
    return new CustomerRepository(
        cosmosClient, 
        config["CosmosDb:DatabaseName"], 
        config["CosmosDb:CustomersContainer"], 
        logger
    );
});
```

## Best Practices Summary

- **Async Everywhere**: Use async/await for all I/O operations
- **Structured Logging**: Use message templates with ILogger<T>
- **Configuration Binding**: Use IOptions<T> for strongly-typed configuration
- **Error Handling**: Use custom exceptions and structured error responses
- **Security First**: Never commit secrets, use Managed Identity in production
- **Azure Native**: Leverage Azure services for scalability and reliability
- **Documentation**: Keep docs updated with architectural decisions

## Getting Help

- **Configuration Issues**: Check `docs/CONFIGURATION_STRATEGY.md`
- **Azure Functions**: See `docs/FUNCTIONS_CONFIGURATION.md`
- **Local Development**: Follow `DEVELOPER_SETUP.md`
- **Architecture Questions**: Review this CONTRIBUTING.md file

Following these guidelines ensures consistency, security, and maintainability across the Helios365 codebase.