targetScope = 'resourceGroup'

@description('Prefix for resource names (e.g., company name or project identifier)')
param prefix string

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

@description('IP addresses to allow access to Cosmos DB, Storage, and Key Vault (for local development/portal access)')
param allowedIpAddresses array = []

// Variables
var uniqueSuffix = substring(uniqueString(resourceGroup().id), 0, 4)
var resourceNames = {
  cosmosDb: '${prefix}-${appName}-${uniqueSuffix}-cosmos'
  storageAccount: toLower('${prefix}${appName}${uniqueSuffix}st')
  functionApp: '${prefix}-${appName}-${uniqueSuffix}-func'
  webApp: '${prefix}-${appName}-${uniqueSuffix}-web'
  functionAppPlan: '${prefix}-${appName}-${uniqueSuffix}-func-asp'
  webAppPlan: '${prefix}-${appName}-${uniqueSuffix}-web-asp'
  keyVault: '${prefix}-${appName}-${uniqueSuffix}-kv'
  applicationInsights: '${prefix}-${appName}-${uniqueSuffix}-ai'
  logAnalytics: '${prefix}-${appName}-${uniqueSuffix}-law'
  communicationService: '${prefix}-${appName}-${uniqueSuffix}-acs'
  emailService: '${prefix}-${appName}-${uniqueSuffix}-email'
  vnet: '${prefix}-${appName}-${uniqueSuffix}-vnet'
}

var commonTags = {
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
    retentionInDays: 90
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
  sku: { name: 'Standard_ZRS' }
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
      ipRules: [for ip in allowedIpAddresses: { value: ip }]
      defaultAction: 'Deny'
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

// Virtual Network
resource vnet 'Microsoft.Network/virtualNetworks@2023-05-01' = {
  name: resourceNames.vnet
  location: location
  tags: commonTags
  properties: {
    addressSpace: {
      addressPrefixes: ['10.0.0.0/16']
    }
    subnets: [
      {
        name: 'app-integration'
        properties: {
          addressPrefix: '10.0.1.0/24'
          delegations: [
            {
              name: 'Microsoft.Web.serverFarms'
              properties: {
                serviceName: 'Microsoft.Web/serverFarms'
              }
            }
          ]
        }
      }
      {
        name: 'private-endpoints'
        properties: {
          addressPrefix: '10.0.2.0/24'
          privateEndpointNetworkPolicies: 'Disabled'
        }
      }
    ]
  }
}

// Private DNS Zones
resource keyVaultPrivateDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: 'privatelink.vaultcore.azure.net'
  location: 'global'
  tags: commonTags
}

resource cosmosDbPrivateDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: 'privatelink.documents.azure.com'
  location: 'global'
  tags: commonTags
}

resource storageBlobPrivateDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: 'privatelink.blob.${az.environment().suffixes.storage}'
  location: 'global'
  tags: commonTags
}

resource storageFilePrivateDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: 'privatelink.file.${az.environment().suffixes.storage}'
  location: 'global'
  tags: commonTags
}

// VNet Links for Private DNS Zones
resource keyVaultDnsZoneLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  parent: keyVaultPrivateDnsZone
  name: '${resourceNames.vnet}-link'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: vnet.id
    }
  }
}

resource cosmosDbDnsZoneLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  parent: cosmosDbPrivateDnsZone
  name: '${resourceNames.vnet}-link'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: vnet.id
    }
  }
}

resource storageBlobDnsZoneLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  parent: storageBlobPrivateDnsZone
  name: '${resourceNames.vnet}-link'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: vnet.id
    }
  }
}

resource storageFileDnsZoneLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  parent: storageFilePrivateDnsZone
  name: '${resourceNames.vnet}-link'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: vnet.id
    }
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
    capabilities: []
    backupPolicy: {
      type: 'Continuous'
      continuousModeProperties: { tier: 'Continuous30Days' }
    }
    enableAutomaticFailover: true
    enableMultipleWriteLocations: false
    disableKeyBasedMetadataWriteAccess: true
    disableLocalAuth: false
    ipRules: [for ip in allowedIpAddresses: { ipAddressOrRange: ip }]
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
    options: { throughput: 800 }
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
    options: { throughput: 800 }
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
    options: { throughput: 800 }
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
    options: { throughput: 800 }
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
    options: { throughput: 800 }
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
    options: { throughput: 800 }
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
    options: { throughput: 800 }
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
      options: { throughput: 800 }
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
      options: { throughput: 800 }
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
      options: { throughput: 800 }
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
      options: { throughput: 800 }
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
    softDeleteRetentionInDays: 90
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
// Note: Dynamic Alpha Sender ID must be enabled manually in Azure Portal
// (Communication Services → SMS → Try Sending SMS → Enable Dynamic Alpha Sender ID)
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

// App Service Plan for Function App (Elastic Premium)
resource functionAppPlan 'Microsoft.Web/serverfarms@2023-01-01' = {
  name: resourceNames.functionAppPlan
  location: location
  tags: commonTags
  sku: { name: 'EP2', tier: 'ElasticPremium' }
  kind: 'linux'
  properties: {
    reserved: true
  }
}

// App Service Plan for Web App
resource webAppPlan 'Microsoft.Web/serverfarms@2023-01-01' = {
  name: resourceNames.webAppPlan
  location: location
  tags: commonTags
  sku: { name: 'P1v3', tier: 'PremiumV3' }
  kind: 'linux'
  properties: {
    reserved: true
  }
}

// Function App
resource functionApp 'Microsoft.Web/sites@2023-01-01' = {
  name: resourceNames.functionApp
  location: location
  tags: commonTags
  kind: 'functionapp,linux'
  identity: { type: 'SystemAssigned' }
  dependsOn: [
    storageBlobPrivateDnsZoneGroup
    storageFilePrivateDnsZoneGroup
    keyVaultPrivateDnsZoneGroup
    cosmosDbPrivateDnsZoneGroup
  ]
  properties: {
    serverFarmId: functionAppPlan.id
    httpsOnly: true
    virtualNetworkSubnetId: vnet.properties.subnets[0].id
    siteConfig: {
      linuxFxVersion: 'DOTNET-ISOLATED|8.0'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      scmMinTlsVersion: '1.2'
      use32BitWorkerProcess: false
      vnetRouteAllEnabled: true
      appSettings: [
        { name: 'FUNCTIONS_EXTENSION_VERSION', value: '~4' }
        { name: 'FUNCTIONS_WORKER_RUNTIME', value: 'dotnet-isolated' }
        { name: 'AzureWebJobsStorage', value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};EndpointSuffix=${az.environment().suffixes.storage};AccountKey=${storageAccount.listKeys().keys[0].value}' }
        { name: 'WEBSITE_CONTENTAZUREFILECONNECTIONSTRING', value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};EndpointSuffix=${az.environment().suffixes.storage};AccountKey=${storageAccount.listKeys().keys[0].value}' }
        { name: 'WEBSITE_CONTENTSHARE', value: toLower(resourceNames.functionApp) }
        { name: 'WEBSITE_CONTENTOVERVNET', value: '1' }
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
        { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
        { name: 'AZURE_FUNCTIONS_ENVIRONMENT', value: 'Production' }
      ]
    }
  }
}

// Web App (Platform)
resource webApp 'Microsoft.Web/sites@2023-01-01' = {
  name: resourceNames.webApp
  location: location
  tags: commonTags
  kind: 'app,linux'
  identity: { type: 'SystemAssigned' }
  dependsOn: [
    storageBlobPrivateDnsZoneGroup
    storageFilePrivateDnsZoneGroup
    keyVaultPrivateDnsZoneGroup
    cosmosDbPrivateDnsZoneGroup
  ]
  properties: {
    serverFarmId: webAppPlan.id
    httpsOnly: true
    virtualNetworkSubnetId: vnet.properties.subnets[0].id
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|8.0'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      scmMinTlsVersion: '1.2'
      use32BitWorkerProcess: false
      vnetRouteAllEnabled: true
      alwaysOn: true
      webSocketsEnabled: true
      appSettings: [
        { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
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
        { name: 'Logging__LogLevel__Default', value: 'Information' }
        { name: 'Logging__LogLevel__Microsoft.AspNetCore', value: 'Warning' }
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

// Private Endpoints
resource keyVaultPrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-05-01' = {
  name: '${resourceNames.keyVault}-pe'
  location: location
  tags: commonTags
  properties: {
    subnet: {
      id: vnet.properties.subnets[1].id
    }
    privateLinkServiceConnections: [
      {
        name: '${resourceNames.keyVault}-plsc'
        properties: {
          privateLinkServiceId: keyVault.id
          groupIds: ['vault']
        }
      }
    ]
  }
}

resource cosmosDbPrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-05-01' = {
  name: '${resourceNames.cosmosDb}-pe'
  location: location
  tags: commonTags
  properties: {
    subnet: {
      id: vnet.properties.subnets[1].id
    }
    privateLinkServiceConnections: [
      {
        name: '${resourceNames.cosmosDb}-plsc'
        properties: {
          privateLinkServiceId: cosmosDb.id
          groupIds: ['Sql']
        }
      }
    ]
  }
}

resource storageBlobPrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-05-01' = {
  name: '${resourceNames.storageAccount}-blob-pe'
  location: location
  tags: commonTags
  properties: {
    subnet: {
      id: vnet.properties.subnets[1].id
    }
    privateLinkServiceConnections: [
      {
        name: '${resourceNames.storageAccount}-blob-plsc'
        properties: {
          privateLinkServiceId: storageAccount.id
          groupIds: ['blob']
        }
      }
    ]
  }
}

resource storageFilePrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-05-01' = {
  name: '${resourceNames.storageAccount}-file-pe'
  location: location
  tags: commonTags
  properties: {
    subnet: {
      id: vnet.properties.subnets[1].id
    }
    privateLinkServiceConnections: [
      {
        name: '${resourceNames.storageAccount}-file-plsc'
        properties: {
          privateLinkServiceId: storageAccount.id
          groupIds: ['file']
        }
      }
    ]
  }
}

// Private DNS Zone Groups
resource keyVaultPrivateDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-05-01' = {
  parent: keyVaultPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'privatelink-vaultcore-azure-net'
        properties: {
          privateDnsZoneId: keyVaultPrivateDnsZone.id
        }
      }
    ]
  }
}

resource cosmosDbPrivateDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-05-01' = {
  parent: cosmosDbPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'privatelink-documents-azure-com'
        properties: {
          privateDnsZoneId: cosmosDbPrivateDnsZone.id
        }
      }
    ]
  }
}

resource storageBlobPrivateDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-05-01' = {
  parent: storageBlobPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'privatelink-blob-core-windows-net'
        properties: {
          privateDnsZoneId: storageBlobPrivateDnsZone.id
        }
      }
    ]
  }
}

resource storageFilePrivateDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-05-01' = {
  parent: storageFilePrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'privatelink-file-core-windows-net'
        properties: {
          privateDnsZoneId: storageFilePrivateDnsZone.id
        }
      }
    ]
  }
}

// Role Assignment - Key Vault Secrets Officer for Function App
resource keyVaultRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, functionApp.id, 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7')
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7')
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Role Assignment - Key Vault Secrets Officer for Web App
resource webAppKeyVaultRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, webApp.id, 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7')
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7')
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
