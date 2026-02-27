# Helios365

[![Build](https://github.com/Helios365/Helios365/actions/workflows/build.yml/badge.svg)](https://github.com/Helios365/Helios365/actions/workflows/build.yml)
[![License](https://img.shields.io/github/license/Helios365/Helios365)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![Azure](https://img.shields.io/badge/Azure-Functions%20%7C%20Cosmos%20DB%20%7C%20Blazor-0078D4)](https://azure.microsoft.com/)

24/7/365 Automated Incident Response for Azure

## Install

Deploy the complete Helios365 infrastructure to Azure in minutes:

### Deploy Infrastructure
[![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fhelios365%2FHelios365%2Frefs%2Fheads%2Fmain%2Finfrastructure%2Fdeploy.json)

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
./scripts/New-AppRegistration.ps1 -Hostname portal.<domain> -AdminGroupId <group object id> -OperatorGroupId <group object id> -ReaderGroupId <group object id>

# Create App Registration Client Secret
Set-AzKeyVaultSecret -VaultName <prefix>-helios-xxxx-kv -Name "AzureAd--ClientSecret" -SecretValue (ConvertTo-SecureString -String "****" -AsPlainText)

```

### Give Microsoft Graph permission to web app

``` powershell

# Grant Web App Microsoft Graph permissions needed for reading Entra groups and users
./scripts/Grant-AppServiceGraphPermissions.ps1 -ResourceGroupName <rg> -AppServiceName <prefix>-helios-xxxx-web

```


### Create managed certificate for web app and function
``` powershell
.\scripts\New-AppServiceManagedCert.ps1 -ResourceGroupName <rg> -AppServiceName <prefix>-helios-xxxx-web -HostName portal.<domain>
.\scripts\New-AppServiceManagedCert.ps1 -ResourceGroupName <rg> -AppServiceName <prefix>-helios-xxxx-func -HostName api.<domain>

```

### Validate Domain in ACS

``` powershell
.\scripts\New-AcsEmailDomain.ps1 -ResourceGroupName <rg> -EmailServiceName <prefix>-helios-xxxx-email -CommunicationServiceName <prefix>-helios-xxxx-acs -DomainName <domain>

# wait until DNS is replicated
.\scripts\New-AcsEmailDomain.ps1 -ResourceGroupName <rg> -EmailServiceName <prefix>-helios-xxxx-email -CommunicationServiceName <prefix>-helios-xxxx-acs -DomainName <domain> -InitiateVerification
```

### Enable SMS sending
Before being able to send SMS, Dynamic Alpha Sender Id (string as sender id) need to be enabled. And this can only be done from Azure Portal under "Telephony and SMS -> Alphanumberic Sender ID -> Enable Alphanumeric Sender ID".

## Workflow

```
Alert arrives → Validate ApiKey → Find Resource → Load Actions → Execute in Order → Escalate if needed
```

1. **Alert Ingestion**: POST /api/alerts?apiKey={key}
2. **Resource Lookup**: Find by (CustomerId + ResourceId)
3. **Action Resolution**: Get actions (default or resource-specific)
4. **Execute Actions**: In order, using Service Principal
5. **Escalation**: Email if all actions fail

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for project structure, configuration, development setup, and coding guidelines.
