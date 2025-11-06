# Helios365

🌞 24/7/365 Automated Incident Response for Azure


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

