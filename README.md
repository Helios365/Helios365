# Helios365

🌞 24/7/365 Automated Incident Response for Azure

## Deploy to Azure

Deploy the complete Helios365 infrastructure to Azure in minutes:

### Deploy Infrastructure
[![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fhelios365%2FHelios365%2Frefs%2Fheads%2Fmain%2Finfrastructure%2Fazuredeploy.json)

**What gets deployed:**
- ✅ Azure Functions (Processor) - Serverless alert processing
- ✅ Cosmos DB with containers - NoSQL database for all data
- ✅ Storage Account - Function runtime storage
- ✅ Key Vault - Secure secrets management
- ✅ Application Insights - Monitoring and telemetry
- ✅ Azure Communication Services - Email notifications
- ✅ Proper RBAC permissions - Secure access configuration

### Deploy Entra Application

``` powershell
# Create App Registration
./scripts/New-AppRegistration.ps1

# Create App Registration Client Secret
Set-AzKeyVaultSecret -VaultName <environment>-helios-xxxx-kv -Name "AzureAd--ClientSecret" -SecretValue (ConvertTo-SecureString -String "****" -AsPlainText)

# Grant Web App Microsoft Graph permissions needed for reading Entra groups and users
./scripts/Grant-AppServiceGraphPermissions.ps1 -ResourceGroupName <rg> -AppServiceName <environment>-helios-xxxx-web

```

### Validate Domain in ACS

``` powershell
.\scripts\New-AcsEmailDomain.ps1 -ResourceGroupName <rg> -EmailServiceName <environment>-helios-xxxx-email -DomainName <domain>

# wait until DNS is replicated
.\scripts\New-AcsEmailDomain.ps1 -ResourceGroupName <rg> -EmailServiceName <environment>-helios-xxxx-email -DomainName <domain> -InitiateVerification
```

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
│   ├── Helios365.Core/           # Domain models, repositories, services
│   ├── Helios365.Functions/      # Azure Functions - alert processing
│   └── Helios365.Web/           # Blazor Server - web dashboard
└── tests/
    ├── Helios365.Core.Tests/     
    └── Helios365.Functions.Tests/
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
- Used for plain-text email and SMS notifications
- Settings:
  - `CommunicationServices:ConnectionString`
  - `CommunicationServices:EmailSender`
  - `CommunicationServices:SmsSender`


# Development

## Create local configuration files

``` powershell
# Create development settings (not committed to Git)
cp src/Helios365.Web/appsettings.json src/Helios365.Web/appsettings.Development.json
cp src/Helios365.Functions/local.settings.json.example src/Helios365.Functions/local.settings.json
```

## Deploy Bicep
New-AzResourceGroupDeployment -ResourceGroupName <RG> -TemplateFile .\infrastructure\deploy.bicep -TemplateParameterFile .\infrastructure\deploy.parameters.dev.json

## Login locally
To run application locally, you would need to do following, in order to make local web app pick upp the permissions needed.
``` powershell
Connect-AzAccount

Connect-MgGraph -Scopes "User.Read.All", "GroupMember.Read.All"

```

# Generate ARM template

``` bash
bicep build .\deploy.bicep --outfile .\azuredeploy.json
```
