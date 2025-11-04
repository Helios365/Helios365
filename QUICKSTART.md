# Helios365 - Quick Start Guide

## What You Have

✅ Complete .NET 9 solution with:
- **Helios365.Core** - Models, services, repositories  
- Solution file with project references ready
- Cosmos DB repositories implemented
- Health check service implemented
- 26+ files created

## Setup Instructions

### 1. Install .NET 9

```bash
# On macOS with Homebrew
brew install dotnet

# Verify
dotnet --version  # Should show 9.0.x
```

### 2. Extract Files

Download `helios365-dotnet-complete.tar.gz` and extract:

```bash
cd /Users/anton/Source/helios
tar -xzf ~/Downloads/helios365-dotnet-complete.tar.gz
cd helios365
```

### 3. Restore and Build

```bash
# Restore NuGet packages
dotnet restore

# Build the solution
dotnet build

# Expected output: Build succeeded
```

### 4. Project Structure

```
helios365/
├── Helios365.sln               # Main solution
├── src/
│   └── Helios365.Core/         # ✅ Complete
│       ├── Models/             # 5 models
│       ├── Services/           # 2 services
│       ├── Repositories/       # 2 repositories
│       └── Exceptions/         # Exception hierarchy
└── tests/                       # Ready for tests
```

## What's Implemented

### Models (✅ Complete)
- `Alert` - With status tracking and methods
- `Customer` - With configuration
- `HealthCheckConfig` - HTTP/TCP/Azure checks
- `NotificationConfig` - Multi-channel notifications
- `RemediationRule` - Remediation actions

### Repositories (✅ Complete)
- `IAlertRepository` / `AlertRepository`
  - CRUD operations
  - Query by status
  - Query by customer
- `ICustomerRepository` / `CustomerRepository`
  - CRUD operations
  - List active customers

### Services (✅ Partial)
- `IHealthCheckService` / `HealthCheckService`
  - HTTP GET/POST checks
  - TCP port checks
  - Async with cancellation
- `INotificationService` (interface only - implement in Processor)

### Features
- ✅ Async/await everywhere
- ✅ Dependency injection ready
- ✅ Logging throughout
- ✅ Custom exceptions
- ✅ Cosmos DB SDK integration
- ✅ Cancellation token support

## Next Steps

### 1. Create Processor Project

```bash
# In the helios365 directory
dotnet new func --name Helios365.Processor --worker-runtime dotnet-isolated --target-framework net9.0

# Move to src folder
mv Helios365.Processor src/

# Add to solution
dotnet sln add src/Helios365.Processor/Helios365.Processor.csproj

# Add reference to Core
cd src/Helios365.Processor
dotnet add reference ../Helios365.Core/Helios365.Core.csproj
```

### 2. Add Durable Functions

```bash
cd src/Helios365.Processor
dotnet add package Microsoft.Azure.Functions.Worker.Extensions.DurableTask
```

### 3. Create Platform Project

```bash
cd ../..
dotnet new webapp --name Helios365.Platform --framework net9.0
mv Helios365.Platform src/
dotnet sln add src/Helios365.Platform/Helios365.Platform.csproj
cd src/Helios365.Platform
dotnet add reference ../Helios365.Core/Helios365.Core.csproj
```

## Testing Core

Create a simple test:

```bash
cd /Users/anton/Source/helios/helios365
dotnet new xunit --name Helios365.Core.Tests --framework net9.0
mv Helios365.Core.Tests tests/
dotnet sln add tests/Helios365.Core.Tests/Helios365.Core.Tests.csproj
cd tests/Helios365.Core.Tests
dotnet add reference ../../src/Helios365.Core/Helios365.Core.csproj
```

Example test:

```csharp
using Helios365.Core.Models;
using Xunit;

public class AlertTests
{
    [Fact]
    public void Alert_CreatesWithReceivedStatus()
    {
        var alert = new Alert
        {
            Id = "test-1",
            CustomerId = "customer-1",
            ResourceId = "/subscriptions/test",
            ResourceType = "Microsoft.Web/sites",
            AlertType = "ServiceHealthAlert"
        };

        Assert.Equal(AlertStatus.Received, alert.Status);
        Assert.True(alert.IsActive());
    }

    [Fact]
    public void Alert_MarkStatus_UpdatesStatus()
    {
        var alert = new Alert { Id = "test-1" };
        var originalTime = alert.UpdatedAt;

        Thread.Sleep(10);
        alert.MarkStatus(AlertStatus.Checking);

        Assert.Equal(AlertStatus.Checking, alert.Status);
        Assert.True(alert.UpdatedAt > originalTime);
    }
}
```

Run tests:
```bash
dotnet test
```

## Open in IDE

### Visual Studio 2022
```bash
open Helios365.sln
```

### VS Code
```bash
code .
```

### JetBrains Rider
```bash
rider Helios365.sln
```

## Common Commands

```bash
# Build
dotnet build

# Clean
dotnet clean

# Restore packages
dotnet restore

# Run tests
dotnet test

# Format code
dotnet format

# List projects in solution
dotnet sln list
```

## What's Missing (To Implement)

These are intentionally left for you to implement:

1. **NotificationService** - Implement the service
2. **Processor Functions** - Azure Functions with Durable orchestration
3. **Platform Pages** - Razor pages for dashboard
4. **Agent Function** - Customer-side function
5. **Tests** - Unit tests for all components
6. **Infrastructure** - Bicep templates

## Configuration Needed

When you run the functions/platform, you'll need:

**Cosmos DB:**
- Connection string
- Database name: `helios365`
- Containers: `alerts`, `customers`

**Azure Functions:**
- Storage account for Durable Functions

## Resources

- .NET 9 Docs: https://learn.microsoft.com/dotnet/core/whats-new/dotnet-9
- Azure Functions: https://learn.microsoft.com/azure/azure-functions/
- Durable Functions: https://learn.microsoft.com/azure/azure-functions/durable/
- Cosmos DB SDK: https://learn.microsoft.com/azure/cosmos-db/nosql/sdk-dotnet-v3

Ready to build! 🚀
