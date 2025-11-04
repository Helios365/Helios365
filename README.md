# Helios365 - .NET 8 Version

🌞 24/7/365 Automated Incident Response for Azure (Refactored Architecture)

## Architecture Changes

### What Changed
- ❌ **Removed Agent project** - No customer-side deployment needed
- ✅ **Service Principal based** - Processor uses customer SPs directly
- ✅ **Action system** - Flexible, ordered actions per resource
- ✅ **Email escalation** - Azure Communication Services

### New Models

**Customer**
- Added: `ApiKey` (for webhook authentication)
- Added: `NotificationEmails` (escalation recipients)
- Added: `EscalationTimeoutMinutes`

**ServicePrincipal** (NEW)
- Stores Azure SP credentials
- Supports multiple Azure clouds
- References Key Vault for secrets

**Resource** (NEW)
- Maps Azure resources to customers
- Links to Service Principal
- Can use default or custom actions

**Actions** (NEW - replaces RemediationRule)
- `HealthCheckAction` - HTTP/TCP health checks
- `RestartAction` - Restart Azure resources
- `ScaleAction` - Scale up/down resources
- Ordered execution
- Manual or Automatic mode

### Workflow

```
Alert arrives → Validate ApiKey → Find Resource → Load Actions → Execute in Order → Escalate if needed
```

1. **Alert Ingestion**: POST /api/alerts?apiKey={key}
2. **Resource Lookup**: Find by (CustomerId + ResourceId)
3. **Action Resolution**: Get actions (default or resource-specific)
4. **Execute Actions**: In order, using Service Principal
5. **Escalation**: Email if all actions fail

## Project Structure

```
helios365/
├── src/
│   ├── Helios365.Core/           # ✅ Models, Interfaces
│   ├── Helios365.Processor/      # 🔄 Azure Functions (TO IMPLEMENT)
│   └── Helios365.Platform/       # 🔄 Web Dashboard (TO IMPLEMENT)
└── tests/
    ├── Helios365.Core.Tests/     # 🔄 TO IMPLEMENT
    ├── Helios365.Processor.Tests/
    └── Helios365.Platform.Tests/
```

## What's Implemented

### Core (Partial ✅)
- ✅ All Models defined
- ✅ Repository interfaces
- ✅ Service interfaces  
- ⏳ Repository implementations (TODO)
- ⏳ Service implementations (TODO)

### Processor (Not Started)
- Need to implement all

### Platform (Not Started)
- Need to implement all

## Next Steps

1. Implement repository classes (Cosmos DB)
2. Implement ActionExecutor service
3. Implement EmailService (Azure Communication Services)
4. Create Processor with new workflow
5. Create Platform pages for managing SPs, Resources, Actions

## Configuration

### Cosmos DB Containers
- customers (partition: /id)
- servicePrincipals (partition: /customerId)
- resources (partition: /customerId)
- actions (partition: /customerId)
- alerts (partition: /customerId)

### Key Vault
Store Service Principal secrets:
- Format: `sp-{servicePrincipalId}`
- Value: Client Secret

### Azure Communication Services
For sending escalation emails

