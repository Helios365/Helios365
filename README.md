# Helios365

🌞 24/7/365 Automated Incident Response for Azure

## Overview

Helios365 is an intelligent incident response platform that automatically detects, validates, and remediates Azure resource issues before they impact your users.

## Solution Structure

```
Helios365/
├── src/
│   ├── Helios365.Core/              # Core library (models, services, repositories)
│   ├── Helios365.Processor/         # Azure Functions (alert processing)
│   ├── Helios365.Platform/          # Web dashboard (Razor Pages)
│   └── Helios365.Agent/             # Customer-side function template
├── tests/
│   ├── Helios365.Core.Tests/
│   └── Helios365.Processor.Tests/
└── infrastructure/
    └── bicep/                       # Azure infrastructure as code
```

## Quick Start

### Prerequisites

- .NET 8 SDK
- Azure subscription
- Azure Cosmos DB instance
- Visual Studio 2022 or VS Code

### Setup

```bash
# Clone the repository
git clone https://github.com/helios365/helios365.git
cd helios365

# Restore packages
dotnet restore

# Build solution
dotnet build

# Run tests
dotnet test
```

### Running Locally

**Processor (Azure Functions):**
```bash
cd src/Helios365.Processor
func start
```

**Platform (Web Dashboard):**
```bash
cd src/Helios365.Platform
dotnet run
```

## Configuration

### local.settings.json (Processor)

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "CosmosDbConnectionString": "your-cosmos-connection-string",
    "CosmosDbDatabaseName": "helios365",
    "CosmosDbAlertsContainer": "alerts",
    "CosmosDbCustomersContainer": "customers"
  }
}
```

### appsettings.json (Platform)

```json
{
  "CosmosDb": {
    "ConnectionString": "your-cosmos-connection-string",
    "DatabaseName": "helios365",
    "AlertsContainer": "alerts",
    "CustomersContainer": "customers"
  }
}
```

## Architecture

### Alert Processing Workflow

1. **Alert Received** → HTTP trigger ingests alert from Azure Monitor
2. **Routing** → Identifies customer and loads configuration
3. **Health Check** → Validates if the issue is real (HTTP/TCP checks)
4. **Remediation** → Automatically restarts/scales resource if configured
5. **Recheck** → Waits 5 minutes and validates fix
6. **Escalation** → Notifies on-call if still failing

### Technologies

- **.NET 9** - Modern C# with latest features
- **Azure Functions (Isolated Worker)** - Serverless compute
- **Durable Functions** - Workflow orchestration
- **Azure Cosmos DB** - NoSQL database
- **ASP.NET Core Razor Pages** - Web dashboard
- **Azure Identity** - Authentication

## Projects

### Helios365.Core

Shared library containing:
- **Models**: Alert, Customer, HealthCheckConfig, NotificationConfig, RemediationRule
- **Services**: HealthCheckService, NotificationService
- **Repositories**: AlertRepository, CustomerRepository (Cosmos SDK)
- **Exceptions**: Custom exception hierarchy

### Helios365.Processor

Azure Functions app:
- **Triggers**: HTTP endpoint for alert ingestion
- **Orchestrators**: Durable function for alert workflow
- **Activities**: SaveAlert, HealthCheck, Remediation, Notification

### Helios365.Platform

ASP.NET Core web app:
- **Dashboard**: Real-time alert monitoring
- **Alerts Page**: View and manage alerts
- **Customers Page**: Manage customer configurations

### Helios365.Agent

Customer-side function template:
- Deploys in customer's Azure tenant
- Executes remediation actions with Managed Identity
- Limited permissions (restart, scale only)

## Development

### Building

```bash
dotnet build
```

### Testing

```bash
dotnet test
dotnet test --collect:"XPlat Code Coverage"
```

### Code Formatting

```bash
dotnet format
```

## Deployment

### Deploy to Azure

```bash
cd infrastructure/bicep
az deployment group create \
  --resource-group helios365-prod \
  --template-file main.bicep \
  --parameters environmentName=prod
```

### CI/CD

GitHub Actions workflows are included:
- `.github/workflows/build.yml` - Build and test
- `.github/workflows/deploy.yml` - Deploy to Azure

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

## License

MIT License - see [LICENSE](LICENSE) for details.

## Support

- [Documentation](https://docs.helios365.io)
- [GitHub Issues](https://github.com/helios365/helios365/issues)
- [Discussions](https://github.com/helios365/helios365/discussions)

## Roadmap

- [ ] Multi-cloud support (AWS, GCP)
- [ ] Machine learning for anomaly detection
- [ ] Mobile app
- [ ] Advanced analytics dashboard
- [ ] Slack/Teams bot integration

---

Built with ☀️ by the Helios365 team
