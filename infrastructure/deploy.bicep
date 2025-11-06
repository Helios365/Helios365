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

@description('Administrator email address for notifications')
param adminEmail string

@description('Data location for Azure Communication Services')
@allowed(['United States', 'Europe', 'Australia', 'United Kingdom', 'France', 'Germany', 'Switzerland', 'Norway', 'Canada', 'India', 'Asia Pacific', 'Africa'])
param dataLocation string = 'Europe'

// Variables
var uniqueSuffix = substring(uniqueString(resourceGroup().id), 0, 4)
var resourceNames = {
  cosmosDb: '${environment}-${appName}-${uniqueSuffix}-cosmos'
  storageAccount: toLower('${environment}${appName}${uniqueSuffix}st')
  functionApp: '${environment}-${appName}-${uniqueSuffix}-func'
  appServicePlan: '${environment}-${appName}-${uniqueSuffix}-plan'
  keyVault: '${environment}-${appName}-${uniqueSuffix}-kv'
  applicationInsights: '${environment}-${appName}-${uniqueSuffix}-ai'
  logAnalytics: '${environment}-${appName}-${uniqueSuffix}-law'
  communicationService: '${environment}-${appName}-${uniqueSuffix}-acs'
}

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
    networkAclBypass: 'AzureServices'
    publicNetworkAccess: 'Enabled'
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
resource customersContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
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
    }
    options: currentConfig.cosmosDbThroughput != null ? { throughput: currentConfig.cosmosDbThroughput } : {}
  }
}

resource resourcesContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
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
    }
    options: currentConfig.cosmosDbThroughput != null ? { throughput: currentConfig.cosmosDbThroughput } : {}
  }
}

resource alertsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
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
    }
    options: currentConfig.cosmosDbThroughput != null ? { throughput: currentConfig.cosmosDbThroughput } : {}
  }
}

resource servicePrincipalsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
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
    }
    options: currentConfig.cosmosDbThroughput != null ? { throughput: currentConfig.cosmosDbThroughput } : {}
  }
}

resource actionsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
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
      defaultAction: 'Allow'
      ipRules: []
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

// App Service Plan
resource appServicePlan 'Microsoft.Web/serverfarms@2023-01-01' = {
  name: resourceNames.appServicePlan
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
        { name: 'AzureCommunicationServicesConnectionString', value: communicationService.listKeys().primaryConnectionString }
        { name: 'FromEmail', value: adminEmail }
        { name: 'ASPNETCORE_ENVIRONMENT', value: environment == 'dev' ? 'Development' : 'Production' }
        { name: 'AZURE_FUNCTIONS_ENVIRONMENT', value: environment == 'dev' ? 'Development' : 'Production' }
      ]
    }
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

// Outputs
output functionAppName string = functionApp.name
output functionAppHostName string = functionApp.properties.defaultHostName
output functionAppUrl string = 'https://${functionApp.properties.defaultHostName}'
output cosmosDbName string = cosmosDb.name
output cosmosDbEndpoint string = cosmosDb.properties.documentEndpoint
output storageAccountName string = storageAccount.name
output keyVaultName string = keyVault.name
output keyVaultUri string = keyVault.properties.vaultUri
output applicationInsightsName string = applicationInsights.name
output communicationServiceName string = communicationService.name
output resourceGroupName string = resourceGroup().name
output subscriptionId string = subscription().subscriptionId

output deploymentInstructions string = '''
🎉 Helios365 infrastructure deployed successfully!

Next steps:
1. Deploy Function App code using the post-deployment script
2. Configure monitoring dashboards in Application Insights  
3. Test the alert ingestion API endpoint
4. Add your first customer and resources via the API

Function App URL: https://${functionApp.properties.defaultHostName}
Key Vault: ${keyVault.properties.vaultUri}
Cosmos DB: ${cosmosDb.properties.documentEndpoint}
'''

output costEstimate object = {
  environment: environment
  estimatedMonthlyCost: environment == 'dev' ? '$15-30' : environment == 'staging' ? '$50-100' : '$150-300'
  breakdown: {
    cosmosDb: environment == 'dev' ? 'Serverless (~$5-15)' : 'Provisioned (~$25-50)'
    functions: environment == 'dev' ? 'Consumption (~$1-5)' : 'Premium (~$150-200)'
    storage: '~$1-5'
    keyVault: '~$0.50'
    other: '~$2-10'
  }
}
