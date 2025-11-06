# Helios365

🌞 24/7/365 Automated Incident Response for Azure

## Deploy to Azure

Deploy the complete Helios365 infrastructure to Azure in minutes:

### Deploy
[![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fhelios365%2FHelios365%2Frefs%2Fheads%2Fmain%2Finfrastructure%2Fdeploy.bicep)

**What gets deployed:**
- ✅ Azure Functions (Processor) - Serverless alert processing
- ✅ Cosmos DB with containers - NoSQL database for all data
- ✅ Storage Account - Function runtime storage
- ✅ Key Vault - Secure secrets management
- ✅ Application Insights - Monitoring and telemetry
- ✅ Azure Communication Services - Email notifications
- ✅ Proper RBAC permissions - Secure access configuration


## Workflow

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
│   ├── Helios365.Core/           
│   ├── Helios365.Processor/     
│   └── Helios365.Platform/      
└── tests/
    ├── Helios365.Core.Tests/     
    ├── Helios365.Processor.Tests/
    └── Helios365.Platform.Tests/
```


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

