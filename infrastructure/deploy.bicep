targetScope = 'resourceGroup'

@description('The environment to deploy (dev, staging, prod)')
@allowed(['dev', 'stage', 'prod'])
param environment string = 'dev'

@description('Azure region for all resources')
param location string = resourceGroup().location

@description('Name of the application (used as prefix for resource names)')
@minLength(3)
@maxLength(6)
param appName string = 'helios'

@description('Email sender address for notifications')
param emailSender string

@description('SMS sender number for Azure Communication Services (E.164 format, e.g. +15555555555)')
param smsSender string = ''

@description('Data location for Azure Communication Services')
@allowed(['United States', 'Europe', 'Australia', 'United Kingdom', 'France', 'Germany', 'Switzerland', 'Norway', 'Canada', 'India', 'Asia Pacific', 'Africa'])
param dataLocation string = 'Europe'

@description('DNS zone name for custom domain (e.g., helios.example.com). Leave empty to skip DNS provisioning.')
param dnsZoneName string = ''

@description('Hostname prefix for the web app CNAME within the DNS zone (e.g., portal -> portal.helios.example.com)')
param dnsWebAppRecord string = 'portal'

@description('Azure AD Domain for authentication')
param azureAdDomain string = ''

@description('Azure AD Tenant ID for authentication')
param azureAdTenantId string = ''

@description('Azure AD Client ID for authentication')
param azureAdClientId string = ''

@description('Entra security group object IDs for directory-driven features')
param directoryServiceGroups object = {
  admin: ''
  operator: ''
  reader: ''
}

@description('Allowed IP addresses for Cosmos DB access')
param allowedIpAddresses array = []

// Variables
var uniqueSuffix = substring(uniqueString(resourceGroup().id), 0, 4)
var resourceNames = {
  cosmosDb: '${environment}-${appName}-${uniqueSuffix}-cosmos'
  storageAccount: toLower('${environment}${appName}${uniqueSuffix}st')
  functionApp: '${environment}-${appName}-${uniqueSuffix}-func'
  webApp: '${environment}-${appName}-${uniqueSuffix}-web'
  functionAppPlan: '${environment}-${appName}-${uniqueSuffix}-func-asp'
  webAppPlan: '${environment}-${appName}-${uniqueSuffix}-app-asp'
  keyVault: '${environment}-${appName}-${uniqueSuffix}-kv'
  applicationInsights: '${environment}-${appName}-${uniqueSuffix}-ai'
  logAnalytics: '${environment}-${appName}-${uniqueSuffix}-law'
  communicationService: '${environment}-${appName}-${uniqueSuffix}-acs'
  emailService: '${environment}-${appName}-${uniqueSuffix}-email'
}

var cosmosIpRules = [for ip in allowedIpAddresses: {
  ipAddressOrRange: ip
}]

var environmentConfig = {
  dev: {
    cosmosDbCapabilities: [
      { name: 'EnableServerless' }
    ]
    appServicePlanSku: { name: 'Y1', tier: 'Dynamic' }
    isZoneRedundant: false
    cosmosDbThroughput: null
    storageAccountSku: 'Standard_LRS'
  }
  staging: {
    cosmosDbCapabilities: []
    appServicePlanSku: { name: 'EP1', tier: 'ElasticPremium' }
    isZoneRedundant: false
    cosmosDbThroughput: 400
    storageAccountSku: 'Standard_LRS'
  }
  prod: {
    cosmosDbCapabilities: []
    appServicePlanSku: { name: 'EP2', tier: 'ElasticPremium' }
    isZoneRedundant: true
    cosmosDbThroughput: 800
    storageAccountSku: 'Standard_ZRS'
  }
}

var currentConfig = environmentConfig[environment]

var commonTags = {
  Environment: environment
  Application: appName
  'Deployed-By': 'Bicep-Template'
}

var dnsZoneEnabled = dnsZoneName != ''

// Log Analytics Workspace
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: resourceNames.logAnalytics
  location: location
  tags: commonTags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: environment == 'prod' ? 90 : 30
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
  }
}

// Application Insights
resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: resourceNames.applicationInsights
  location: location
  tags: commonTags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    Flow_Type: 'Redfield'
    Request_Source: 'IbizaWebAppExtensionCreate'
    WorkspaceResourceId: logAnalytics.id
  }
}

// Storage Account
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: resourceNames.storageAccount
  location: location
  tags: commonTags
  sku: { name: currentConfig.storageAccountSku }
  kind: 'StorageV2'
  properties: {
    dnsEndpointType: 'Standard'
    defaultToOAuthAuthentication: false
    publicNetworkAccess: 'Enabled'
    allowCrossTenantReplication: false
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: true
    networkAcls: {
      bypass: 'AzureServices'
      virtualNetworkRules: []
      ipRules: []
      defaultAction: 'Allow'
    }
    supportsHttpsTrafficOnly: true
    encryption: {
      requireInfrastructureEncryption: false
      services: {
        file: { keyType: 'Account', enabled: true }
        blob: { keyType: 'Account', enabled: true }
      }
      keySource: 'Microsoft.Storage'
    }
    accessTier: 'Hot'
  }
}

// Cosmos DB Account
resource cosmosDb 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' = {
  name: resourceNames.cosmosDb
  location: location
  tags: commonTags
  kind: 'GlobalDocumentDB'
  properties: {
    minimalTlsVersion: 'Tls12'
    databaseAccountOfferType: 'Standard'
    consistencyPolicy: { defaultConsistencyLevel: 'Session' }
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false // Explicitly disable zone redundancy for all environments initially
      }
    ]
    capabilities: currentConfig.cosmosDbCapabilities
    //enableFreeTier: environment == 'dev'
    backupPolicy: environment == 'dev' ? {
      type: 'Periodic'
      periodicModeProperties: {
        backupIntervalInMinutes: 1440 // 24 hours
        backupRetentionIntervalInHours: 168 // 7 days
      }
    } : {
      type: 'Continuous'
      continuousModeProperties: { tier: 'Continuous30Days' }
    }
    enableAutomaticFailover: environment == 'prod'
    enableMultipleWriteLocations: false
    disableKeyBasedMetadataWriteAccess: true
    disableLocalAuth: false
    ipRules: cosmosIpRules
    networkAclBypass: 'AzureServices'
    publicNetworkAccess: 'Enabled'
    analyticalStorageConfiguration: {
      schemaType: 'WellDefined'
    }
    defaultIdentity: 'FirstPartyIdentity'
  }
}

// Cosmos DB Database
resource cosmosDatabase 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-05-15' = {
  parent: cosmosDb
  name: 'helios365'
  properties: {
    
    resource: { id: 'helios365' }
    options: currentConfig.cosmosDbThroughput != null ? { throughput: currentConfig.cosmosDbThroughput } : {}
  }
}

// Cosmos DB Containers
resource customersContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2025-04-15' = {
  parent: cosmosDatabase
  name: 'customers'
  properties: {
    resource: {
      id: 'customers'
      partitionKey: { paths: ['/id'], kind: 'Hash' }
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
        includedPaths: [{ path: '/*' }]
        excludedPaths: [{ path: '/"_etag"/?' }]
      }
      defaultTtl: -1
      conflictResolutionPolicy: {
        mode: 'LastWriterWins'
        conflictResolutionPath: '/_ts'
      }
    }
    options: currentConfig.cosmosDbThroughput != null ? { throughput: currentConfig.cosmosDbThroughput } : {}
  }
}

resource resourcesContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2025-04-15' = {
  parent: cosmosDatabase
  name: 'resources'
  properties: {
    resource: {
      id: 'resources'
      partitionKey: { paths: ['/customerId'], kind: 'Hash' }
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
        includedPaths: [{ path: '/*' }]
        excludedPaths: [{ path: '/"_etag"/?' }]
      }
      defaultTtl: -1
      conflictResolutionPolicy: {
        mode: 'LastWriterWins'
        conflictResolutionPath: '/_ts'
      }
    }
    options: currentConfig.cosmosDbThroughput != null ? { throughput: currentConfig.cosmosDbThroughput } : {}
  }
}

resource alertsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2025-04-15' = {
  parent: cosmosDatabase
  name: 'alerts'
  properties: {
    resource: {
      id: 'alerts'
      partitionKey: { paths: ['/customerId'], kind: 'Hash' }
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
        includedPaths: [{ path: '/*' }]
        excludedPaths: [{ path: '/"_etag"/?' }]
      }
      defaultTtl: -1
      conflictResolutionPolicy: {
        mode: 'LastWriterWins'
        conflictResolutionPath: '/_ts'
      }
    }
    options: currentConfig.cosmosDbThroughput != null ? { throughput: currentConfig.cosmosDbThroughput } : {}
  }
}

resource servicePrincipalsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2025-04-15' = {
  parent: cosmosDatabase
  name: 'servicePrincipals'
  properties: {
    resource: {
      id: 'servicePrincipals'
      partitionKey: { paths: ['/customerId'], kind: 'Hash' }
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
        includedPaths: [{ path: '/*' }]
        excludedPaths: [{ path: '/"_etag"/?' }]
      }
      defaultTtl: -1
      conflictResolutionPolicy: {
        mode: 'LastWriterWins'
        conflictResolutionPath: '/_ts'
      }
    }
    options: currentConfig.cosmosDbThroughput != null ? { throughput: currentConfig.cosmosDbThroughput } : {}
  }
}

resource usersContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2025-04-15' = {
  parent: cosmosDatabase
  name: 'users'
  properties: {
    resource: {
      id: 'users'
      partitionKey: { paths: ['/id'], kind: 'Hash' }
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
        includedPaths: [{ path: '/*' }]
        excludedPaths: [{ path: '/"_etag"/?' }]
      }
      defaultTtl: -1
      conflictResolutionPolicy: {
        mode: 'LastWriterWins'
        conflictResolutionPath: '/_ts'
      }
    }
    options: currentConfig.cosmosDbThroughput != null ? { throughput: currentConfig.cosmosDbThroughput } : {}
  }
}

  resource actionsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2025-04-15' = {
    parent: cosmosDatabase
    name: 'actions'
  properties: {
    resource: {
      id: 'actions'
      partitionKey: { paths: ['/customerId'], kind: 'Hash' }
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
        includedPaths: [{ path: '/*' }]
        excludedPaths: [{ path: '/"_etag"/?' }]
      }
      defaultTtl: -1
      // Keep explicit to avoid drift and use Cosmos default behavior (LWW on server timestamp)
      conflictResolutionPolicy: {
        mode: 'LastWriterWins'
        conflictResolutionPath: '/_ts'
      }
    }
    options: currentConfig.cosmosDbThroughput != null ? { throughput: currentConfig.cosmosDbThroughput } : {}
  }
}

  resource onCallPlansContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2025-04-15' = {
    parent: cosmosDatabase
    name: 'onCallPlans'
    properties: {
      resource: {
        id: 'onCallPlans'
        partitionKey: {
          paths: [
            '/id'
          ]
          kind: 'Hash'
        }
        indexingPolicy: {
          indexingMode: 'consistent'
          automatic: true
          includedPaths: [{ path: '/*' }]
          excludedPaths: [{ path: '/"_etag"/?' }]
        }
        defaultTtl: -1
        conflictResolutionPolicy: {
          mode: 'LastWriterWins'
          conflictResolutionPath: '/_ts'
        }
      }
      options: currentConfig.cosmosDbThroughput != null ? { throughput: currentConfig.cosmosDbThroughput } : {}
    }
  }

  resource onCallTeamsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2025-04-15' = {
    parent: cosmosDatabase
    name: 'onCallTeams'
    properties: {
      resource: {
        id: 'onCallTeams'
        partitionKey: {
          paths: [
            '/id'
          ]
          kind: 'Hash'
        }
        indexingPolicy: {
          indexingMode: 'consistent'
          automatic: true
          includedPaths: [{ path: '/*' }]
          excludedPaths: [{ path: '/"_etag"/?' }]
        }
        defaultTtl: -1
        conflictResolutionPolicy: {
          mode: 'LastWriterWins'
          conflictResolutionPath: '/_ts'
        }
      }
      options: currentConfig.cosmosDbThroughput != null ? { throughput: currentConfig.cosmosDbThroughput } : {}
    }
  }

  resource planBindingsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2025-04-15' = {
    parent: cosmosDatabase
    name: 'planBindings'
    properties: {
      resource: {
        id: 'planBindings'
        partitionKey: {
          paths: [
            '/customerId'
          ]
          kind: 'Hash'
        }
        indexingPolicy: {
          indexingMode: 'consistent'
          automatic: true
          includedPaths: [{ path: '/*' }]
          excludedPaths: [{ path: '/"_etag"/?' }]
        }
        defaultTtl: -1
        conflictResolutionPolicy: {
          mode: 'LastWriterWins'
          conflictResolutionPath: '/_ts'
        }
      }
      options: currentConfig.cosmosDbThroughput != null ? { throughput: currentConfig.cosmosDbThroughput } : {}
    }
  }

  resource scheduleSlicesContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2025-04-15' = {
    parent: cosmosDatabase
    name: 'scheduleSlices'
    properties: {
      resource: {
        id: 'scheduleSlices'
        partitionKey: {
          paths: [
            '/customerId'
          ]
          kind: 'Hash'
        }
        indexingPolicy: {
          indexingMode: 'consistent'
          automatic: true
          includedPaths: [{ path: '/*' }]
          excludedPaths: [{ path: '/"_etag"/?' }]
        }
        defaultTtl: -1
        conflictResolutionPolicy: {
          mode: 'LastWriterWins'
          conflictResolutionPath: '/_ts'
        }
      }
      options: currentConfig.cosmosDbThroughput != null ? { throughput: currentConfig.cosmosDbThroughput } : {}
    }
  }

// Key Vault
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: resourceNames.keyVault
  location: location
  tags: commonTags
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: subscription().tenantId
    enabledForDeployment: false
    enabledForTemplateDeployment: true
    enabledForDiskEncryption: false
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: environment == 'prod' ? 90 : 7
    // Remove enablePurgeProtection to avoid conflicts - it defaults based on enableSoftDelete
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Deny'
      ipRules: [for ip in allowedIpAddresses: { value: ip }]
      virtualNetworkRules: []
    }
    publicNetworkAccess: 'Enabled'
  }
}

// Azure Communication Services
resource communicationService 'Microsoft.Communication/CommunicationServices@2023-04-01' = {
  name: resourceNames.communicationService
  location: 'global'
  tags: commonTags
  properties: {
    dataLocation: dataLocation
  }
}

// Azure Email Communication Service
resource emailService 'Microsoft.Communication/emailServices@2023-03-31' = {
  name: resourceNames.emailService
  // Email Services must be deployed to 'global'
  location: 'global'
  tags: commonTags
  properties: {
    dataLocation: dataLocation
  }
}

// DNS Zone (optional)
resource dnsZone 'Microsoft.Network/dnsZones@2018-05-01' = if (dnsZoneEnabled) {
  name: dnsZoneName
  location: 'global'
  tags: commonTags
}

// App Service Plan
resource appServicePlan 'Microsoft.Web/serverfarms@2023-01-01' = {
  name: resourceNames.functionAppPlan
  location: location
  tags: commonTags
  sku: currentConfig.appServicePlanSku
  kind: 'functionapp'
  properties: {
    reserved: false
  }
}

// Function App
resource functionApp 'Microsoft.Web/sites@2023-01-01' = {
  name: resourceNames.functionApp
  location: location
  tags: commonTags
  kind: 'functionapp'
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      scmMinTlsVersion: '1.2'
      use32BitWorkerProcess: false
      netFrameworkVersion: 'v8.0'
      appSettings: [
        { name: 'FUNCTIONS_EXTENSION_VERSION', value: '~4' }
        { name: 'FUNCTIONS_WORKER_RUNTIME', value: 'dotnet-isolated' }
        { name: 'AzureWebJobsStorage', value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};EndpointSuffix=${az.environment().suffixes.storage};AccountKey=${storageAccount.listKeys().keys[0].value}' }
        { name: 'WEBSITE_CONTENTAZUREFILECONNECTIONSTRING', value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};EndpointSuffix=${az.environment().suffixes.storage};AccountKey=${storageAccount.listKeys().keys[0].value}' }
        { name: 'WEBSITE_CONTENTSHARE', value: toLower(resourceNames.functionApp) }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: applicationInsights.properties.ConnectionString }
        { name: 'APPINSIGHTS_INSTRUMENTATIONKEY', value: applicationInsights.properties.InstrumentationKey }
        { name: 'CosmosDbConnectionString', value: 'AccountEndpoint=${cosmosDb.properties.documentEndpoint};AccountKey=${cosmosDb.listKeys().primaryMasterKey};' }
        { name: 'CosmosDbDatabaseName', value: 'helios365' }
        { name: 'CosmosDbCustomersContainer', value: 'customers' }
        { name: 'CosmosDbResourcesContainer', value: 'resources' }
        { name: 'CosmosDbAlertsContainer', value: 'alerts' }
        { name: 'CosmosDbServicePrincipalsContainer', value: 'servicePrincipals' }
        { name: 'CosmosDbActionsContainer', value: 'actions' }
        { name: 'KeyVaultUri', value: keyVault.properties.vaultUri }
        { name: 'CommunicationServices__ConnectionString', value: communicationService.listKeys().primaryConnectionString }
        { name: 'CommunicationServices__EmailSender', value: emailSender }
        { name: 'CommunicationServices__SmsSender', value: smsSender }
        { name: 'ASPNETCORE_ENVIRONMENT', value: environment == 'dev' ? 'Development' : 'Production' }
        { name: 'AZURE_FUNCTIONS_ENVIRONMENT', value: environment == 'dev' ? 'Development' : 'Production' }
      ]
    }
  }
}

// App Service Plan for Web App (separate from Functions)
resource webAppServicePlan 'Microsoft.Web/serverfarms@2023-01-01' = {
  name: resourceNames.webAppPlan
  location: location
  tags: commonTags
  sku: { name: 'B1', tier: 'Basic' }
  kind: 'linux'
  properties: {
    reserved: true // Linux
  }
}

// Web App (Platform)
resource webApp 'Microsoft.Web/sites@2023-01-01' = {
  name: resourceNames.webApp
  location: location
  tags: commonTags
  kind: 'app,linux'
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: webAppServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|8.0'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      scmMinTlsVersion: '1.2'
      use32BitWorkerProcess: environment == 'dev' ? true : false
      appSettings: [
        { name: 'ASPNETCORE_ENVIRONMENT', value: environment == 'dev' ? 'Development' : 'Production' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: applicationInsights.properties.ConnectionString }
        { name: 'ApplicationInsights__InstrumentationKey', value: applicationInsights.properties.InstrumentationKey }
        // Azure AD Configuration (override appsettings.json)
        { name: 'AzureAd__Domain', value: azureAdDomain }
        { name: 'AzureAd__TenantId', value: azureAdTenantId }
        { name: 'AzureAd__ClientId', value: azureAdClientId }
        // Key Vault Configuration  
        { name: 'KeyVault__VaultUri', value: keyVault.properties.vaultUri }
        // Cosmos DB Configuration
        { name: 'CosmosDb__ConnectionString', value: 'AccountEndpoint=${cosmosDb.properties.documentEndpoint};AccountKey=${cosmosDb.listKeys().primaryMasterKey};' }
        { name: 'CosmosDb__DatabaseName', value: 'helios365' }
        { name: 'CosmosDb__CustomersContainer', value: 'customers' }
        { name: 'CosmosDb__ResourcesContainer', value: 'resources' }
        { name: 'CosmosDb__AlertsContainer', value: 'alerts' }
        { name: 'CosmosDb__ServicePrincipalsContainer', value: 'servicePrincipals' }
        { name: 'CosmosDb__ActionsContainer', value: 'actions' }
        { name: 'CosmosDb__UsersContainer', value: 'users' }
        { name: 'CosmosDb__WebTestsContainer', value: 'webTests' }
        { name: 'CosmosDb__OnCallPlansContainer', value: 'onCallPlans' }
        { name: 'CosmosDb__OnCallTeamsContainer', value: 'onCallTeams' }
        { name: 'CosmosDb__PlanBindingsContainer', value: 'planBindings' }
        { name: 'CosmosDb__ScheduleSlicesContainer', value: 'scheduleSlices' }
        // Communication Services
        { name: 'CommunicationServices__ConnectionString', value: communicationService.listKeys().primaryConnectionString }
        { name: 'CommunicationServices__EmailSender', value: emailSender }
        { name: 'CommunicationServices__SmsSender', value: smsSender }
        // Logging Configuration
        { name: 'Logging__LogLevel__Default', value: environment == 'dev' ? 'Debug' : 'Information' }
        { name: 'Logging__LogLevel__Microsoft.AspNetCore', value: environment == 'dev' ? 'Warning' : 'Warning' }
        // Directory Service Groups (for Graph lookups)
        { name: 'DirectoryService__Groups__Admin', value: directoryServiceGroups.admin }
        { name: 'DirectoryService__Groups__Operator', value: directoryServiceGroups.operator }
        { name: 'DirectoryService__Groups__Reader', value: directoryServiceGroups.reader }
      ]
    }
  }
}

// CNAME for web app (optional DNS)
resource webAppCname 'Microsoft.Network/dnsZones/CNAME@2018-05-01' = if (dnsZoneEnabled) {
  name: dnsWebAppRecord
  parent: dnsZone
  properties: {
    TTL: 300
    CNAMERecord: {
      cname: webApp.properties.defaultHostName
    }
  }
}

resource webAppTxt 'Microsoft.Network/dnsZones/TXT@2018-05-01' = if (dnsZoneEnabled) {
  name: 'asuid.${dnsWebAppRecord}'
  parent: dnsZone
  properties: {
    TTL: 3600
    TXTRecords: [
      {
        value: [
          webApp.properties.customDomainVerificationId
        ]
      }
    ]
  }
}
// Role Assignment - Key Vault Secrets User for Function App
resource keyVaultRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, functionApp.id, '4633458b-17de-408a-b874-0445c86b69e6')
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Role Assignment - Key Vault Secrets User for Web App
resource webAppKeyVaultRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, webApp.id, '4633458b-17de-408a-b874-0445c86b69e6')
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
    principalId: webApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Key Vault Secrets
resource cosmosDbConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  name: 'CosmosDb-ConnectionString'
  parent: keyVault
  properties: {
    value: 'AccountEndpoint=${cosmosDb.properties.documentEndpoint};AccountKey=${cosmosDb.listKeys().primaryMasterKey};'
  }
  dependsOn: [
    keyVaultRoleAssignment
    webAppKeyVaultRoleAssignment
  ]
}

resource communicationServicesConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  name: 'CommunicationServices-ConnectionString'
  parent: keyVault
  properties: {
    value: 'endpoint=${communicationService.properties.hostName};accesskey=${communicationService.listKeys().primaryKey}'
  }
  dependsOn: [
    keyVaultRoleAssignment
    webAppKeyVaultRoleAssignment
  ]
}

// Outputs
output functionAppName string = functionApp.name
output functionAppHostName string = functionApp.properties.defaultHostName
output functionAppUrl string = 'https://${functionApp.properties.defaultHostName}'
output webAppName string = webApp.name
output webAppHostName string = webApp.properties.defaultHostName
output webAppUrl string = 'https://${webApp.properties.defaultHostName}'
output cosmosDbName string = cosmosDb.name
output cosmosDbEndpoint string = cosmosDb.properties.documentEndpoint
output storageAccountName string = storageAccount.name
output keyVaultName string = keyVault.name
output keyVaultUri string = keyVault.properties.vaultUri
output applicationInsightsName string = applicationInsights.name
output communicationServiceName string = communicationService.name
output resourceGroupName string = resourceGroup().name

// Key Vault Secret Information
output cosmosDbSecretName string = cosmosDbConnectionStringSecret.name
output communicationServicesSecretName string = communicationServicesConnectionStringSecret.name
output subscriptionId string = subscription().subscriptionId
