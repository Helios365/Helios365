# Helios365 Deployment Guide


## Quick Start

### 1. Extract & Build

```bash
tar -xzf helios365-net8-complete.tar.gz
cd helios365
dotnet restore
dotnet build
dotnet test  # Run tests
```

### 2. Setup Azure Resources

```bash
# Create resource group
az group create --name helios365-prod --location eastus

# Create Cosmos DB
az cosmosdb create \
  --name helios365-db \
  --resource-group helios365-prod

# Create database and containers
az cosmosdb sql database create \
  --account-name helios365-db \
  --resource-group helios365-prod \
  --name helios365

az cosmosdb sql container create \
  --account-name helios365-db \
  --resource-group helios365-prod \
  --database-name helios365 \
  --name customers \
  --partition-key-path "/id"

az cosmosdb sql container create \
  --account-name helios365-db \
  --resource-group helios365-prod \
  --database-name helios365 \
  --name alerts \
  --partition-key-path "/customerId"

az cosmosdb sql container create \
  --account-name helios365-db \
  --resource-group helios365-prod \
  --database-name helios365 \
  --name resources \
  --partition-key-path "/customerId"

az cosmosdb sql container create \
  --account-name helios365-db \
  --resource-group helios365-prod \
  --database-name helios365 \
  --name servicePrincipals \
  --partition-key-path "/customerId"

az cosmosdb sql container create \
  --account-name helios365-db \
  --resource-group helios365-prod \
  --database-name helios365 \
  --name actions \
  --partition-key-path "/customerId"

# Create Key Vault
az keyvault create \
  --name helios365-kv \
  --resource-group helios365-prod \
  --location eastus

# Create Azure Communication Services
az communication create \
  --name helios365-acs \
  --resource-group helios365-prod \
  --data-location UnitedStates

# Create Storage Account (for Durable Functions)
az storage account create \
  --name helios365storage \
  --resource-group helios365-prod \
  --location eastus \
  --sku Standard_LRS

# Create Function App
az functionapp create \
  --resource-group helios365-prod \
  --consumption-plan-location eastus \
  --runtime dotnet-isolated \
  --functions-version 4 \
  --name helios365-processor \
  --storage-account helios365storage

# Create App Service for Platform
az appservice plan create \
  --name helios365-plan \
  --resource-group helios365-prod \
  --sku B1

az webapp create \
  --name helios365-platform \
  --resource-group helios365-prod \
  --plan helios365-plan \
  --runtime "DOTNETCORE:8.0"
```

### 3. Configure Processor

```bash
cd src/Helios365.Processor
cp local.settings.json.example local.settings.json

# Edit local.settings.json with:
# - Cosmos DB connection string
# - Key Vault URI
# - Azure Communication Services connection string
```

### 4. Configure Platform

```bash
cd ../Helios365.Platform

# Edit appsettings.json with:
# - Cosmos DB connection string
```

### 5. Add Test Data

Use Azure Portal or code to add:

```json
// Customer
{
  "id": "customer-1",
  "name": "Contoso Ltd",
  "apiKey": "test-api-key-12345",
  "notificationEmails": ["oncall@contoso.com"],
  "escalationTimeoutMinutes": 5,
  "active": true
}

// Service Principal
{
  "id": "sp-1",
  "customerId": "customer-1",
  "name": "Production SP",
  "tenantId": "your-tenant-id",
  "clientId": "your-client-id",
  "clientSecretKeyVaultReference": "https://helios365-kv.vault.azure.net/secrets/sp-sp-1",
  "cloudEnvironment": "AzurePublicCloud",
  "active": true
}

// Store SP secret in Key Vault
az keyvault secret set \
  --vault-name helios365-kv \
  --name sp-sp-1 \
  --value "YOUR_CLIENT_SECRET"

// Resource
{
  "id": "resource-1",
  "customerId": "customer-1",
  "servicePrincipalId": "sp-1",
  "name": "Production API",
  "resourceId": "/subscriptions/YOUR_SUB/resourceGroups/prod-rg/providers/Microsoft.Web/sites/prod-api",
  "resourceType": "Microsoft.Web/sites",
  "useDefaultActions": false,
  "active": true
}

// Actions
// HealthCheck
{
  "id": "action-1",
  "resourceId": "resource-1",
  "customerId": "customer-1",
  "type": "HealthCheck",
  "mode": "Automatic",
  "order": 1,
  "enabled": true,
  "url": "https://prod-api.azurewebsites.net/health",
  "method": "GET",
  "expectedStatusCode": 200,
  "timeoutSeconds": 30
}

// Restart
{
  "id": "action-2",
  "resourceId": "resource-1",
  "customerId": "customer-1",
  "type": "Restart",
  "mode": "Automatic",
  "order": 2,
  "enabled": true,
  "waitBeforeSeconds": 0,
  "waitAfterSeconds": 300
}

// HealthCheck (recheck)
{
  "id": "action-3",
  "resourceId": "resource-1",
  "customerId": "customer-1",
  "type": "HealthCheck",
  "mode": "Automatic",
  "order": 3,
  "enabled": true,
  "url": "https://prod-api.azurewebsites.net/health",
  "method": "GET",
  "expectedStatusCode": 200,
  "timeoutSeconds": 30
}
```

### 6. Test Locally

```bash
# Terminal 1: Run Processor
cd src/Helios365.Processor
func start

# Terminal 2: Run Platform
cd src/Helios365.Platform
dotnet run

# Terminal 3: Send test alert
curl -X POST "http://localhost:7071/api/alerts?apiKey=test-api-key-12345" \
  -H "Content-Type: application/json" \
  -d '{
    "resourceId": "/subscriptions/YOUR_SUB/resourceGroups/prod-rg/providers/Microsoft.Web/sites/prod-api",
    "resourceType": "Microsoft.Web/sites",
    "alertType": "ServiceHealthAlert",
    "title": "App Service Down",
    "severity": "High"
  }'

# Check dashboard at https://localhost:5001
```

### 7. Deploy to Azure

```bash
# Deploy Processor
cd src/Helios365.Processor
func azure functionapp publish helios365-processor

# Deploy Platform
cd ../Helios365.Platform
az webapp deployment source config-zip \
  --resource-group helios365-prod \
  --name helios365-platform \
  --src publish.zip
```

## Monitoring

- View logs in Application Insights
- Check Durable Functions status in Azure Portal
- Monitor alerts in Platform dashboard

## Troubleshooting

**Alert not processing:**
- Check API key is correct
- Verify resource exists in database
- Check Function App logs

**Remediation not working:**
- Verify SP secret in Key Vault
- Check SP has permissions on resource
- Review ActionExecutor logs

**Emails not sending:**
- Verify Azure Communication Services connection string
- Check sender email is configured
- Review EmailService logs

## Production Checklist

- [ ] Configure Azure AD authentication for Platform
- [ ] Set up proper RBAC
- [ ] Enable Application Insights
- [ ] Configure alerts for Function failures
- [ ] Set up backup for Cosmos DB
- [ ] Configure custom domain for Platform
- [ ] Set up CI/CD pipeline
- [ ] Load test the system
- [ ] Document runbooks for on-call

## Security

- Use Managed Identity where possible
- Rotate API keys regularly
- Store all secrets in Key Vault
- Enable Cosmos DB encryption
- Configure network restrictions
- Enable audit logging

## Next Steps

- Add Azure AD authentication to Platform
- Create admin/viewer roles
- Add more resource types (VMs, SQL, etc.)
- Implement action history tracking
- Add metrics and analytics
- Create mobile app
- Multi-cloud support (AWS, GCP)

