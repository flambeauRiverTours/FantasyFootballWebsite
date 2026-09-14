metadata description = 'Ideal Points: Static Web App, Functions on Consumption, storage and telemetry.'

@description('Short name used to build every resource name.')
@minLength(3)
@maxLength(11)
param namePrefix string = 'idealpoints'

@description('Where to deploy.')
param location string = resourceGroup().location

@description('Region for the Static Web App, which supports fewer locations than everything else.')
@allowed([
  'centralus'
  'eastus2'
  'eastasia'
  'westeurope'
  'westus2'
])
param staticWebAppLocation string = 'eastus2'

@description('The Sleeper league this site serves. Secure so it never reaches the deployment history.')
@secure()
param leagueId string

@description('Owning user ids excluded from every metric, comma separated. Empty for a league with no placeholder team.')
@secure()
param excludedOwnerIds string = ''

@description('Extra origins allowed to call the API, for front-end development against the deployed API. Empty in production; preview environments are deliberately not allowed.')
param devOrigins array = []

@description('Daily telemetry ingestion cap in GB, as a string so a fraction is possible. 0.16/day keeps a month inside the 5 GB free allocation.')
param dailyIngestCapGb string = '0.16'

@description('Most Flex Consumption instances that may run at once. Forty is the platform minimum.')
@minValue(40)
param maxScaleOut int = 40

@description('Memory per instance. The smallest size costs the fewest GB-seconds.')
@allowed([
  512
  2048
  4096
])
param instanceMemoryMb int = 512

@description('Monthly spend that triggers an alert, in the billing currency. The design expects zero.')
param budgetAmount int = 1

@description('Where to send the budget alert. Leave empty to skip the budget entirely.')
param budgetAlertEmail string = ''

@description('First day of the month the budget starts. Defaults to this month.')
param budgetStartDate string = utcNow('yyyy-MM-01')

@description('Region for the SQL server. Azure SQL General Purpose provisioning is restricted per subscription per region; this defaults to a region verified open on this subscription, independent of the resource group location.')
@allowed([
  'centralus'
  'eastasia'
  'westeurope'
  'westus2'
])
param sqlLocation string = 'westus2'

@description('Object id of the Entra principal that administers the SQL server. Empty skips the SQL resources entirely.')
param sqlAdminObjectId string = ''

@description('Display name of that principal, shown in the portal.')
param sqlAdminLogin string = ''

@description('Minutes of inactivity before the database pauses. Required on the free offer; 60 is the minimum.')
param sqlAutoPauseDelayMinutes int = 60

var excludedOwnerIdList = filter(map(split(excludedOwnerIds, ','), id => trim(id)), id => !empty(id))

var excludedOwnerSettings = [
  for (ownerId, index) in excludedOwnerIdList: {
    name: 'League__ExcludedOwnerIds__${index}'
    value: ownerId
  }
]

// no secret in it: Entra auth means there is no password to protect
var sqlConnectionSettings = deploySql ? [
  {
    name: 'ConnectionStrings__FantasyLeague'
    value: 'Server=tcp:${sqlName}${environment().suffixes.sqlServerHostname},1433;Database=${sqlDatabaseName};Authentication=Active Directory Default;Encrypt=True;'
  }
] : []
var storageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storage.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storage.listKeys().keys[0].value}'

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${namePrefix}'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    workspaceCapping: {
      dailyQuotaGb: json(dailyIngestCapGb)
    }
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${namePrefix}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
  }
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: 'st${uniqueString(resourceGroup().id)}'
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
  }

  resource blob 'blobServices' = {
    name: 'default'
    properties: {
      deleteRetentionPolicy: {
        enabled: false
      }
      containerDeleteRetentionPolicy: {
        enabled: false
      }
    }

    resource deployments 'containers' = {
      name: 'deployments'
    }
  }
}

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: 'plan-${namePrefix}'
  location: location
  kind: 'functionapp'
  sku: {
    name: 'FC1'
    tier: 'FlexConsumption'
  }
  properties: {
    reserved: true
  }
}

resource site 'Microsoft.Web/staticSites@2023-12-01' = {
  name: 'stapp-${namePrefix}'
  location: staticWebAppLocation
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {}
}

resource functions 'Microsoft.Web/sites@2023-12-01' = {
  name: 'func-${namePrefix}'
  location: location
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  // Flex Consumption deploys into this container, and only the storage
  // account is an inferred dependency.
  dependsOn: [
    storage::blob::deployments
  ]
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storage.properties.primaryEndpoints.blob}deployments'
          authentication: {
            type: 'StorageAccountConnectionString'
            storageAccountConnectionStringName: 'DEPLOYMENT_STORAGE_CONNECTION_STRING'
          }
        }
      }
      scaleAndConcurrency: {
        maximumInstanceCount: maxScaleOut
        instanceMemoryMB: instanceMemoryMb
        alwaysReady: []
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '10.0'
      }
    }
    siteConfig: {
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      http20Enabled: true
      cors: {
        allowedOrigins: concat(
          [
            'https://${site.properties.defaultHostname}'
          ],
          devOrigins
        )
      }
      appSettings: concat(
        [
          {
            name: 'AzureWebJobsStorage'
            value: storageConnectionString
          }
          {
            name: 'DEPLOYMENT_STORAGE_CONNECTION_STRING'
            value: storageConnectionString
          }
          {
            name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
            value: insights.properties.ConnectionString
          }
          {
            name: 'League__CurrentLeagueId'
            value: leagueId
          }
        ],
        excludedOwnerSettings,
        sqlConnectionSettings
      )
    }
  }
}

resource budget 'Microsoft.Consumption/budgets@2023-05-01' = if (!empty(budgetAlertEmail)) {
  name: 'budget-${namePrefix}'
  properties: {
    category: 'Cost'
    amount: budgetAmount
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: budgetStartDate
    }
    notifications: {
      anySpendAtAll: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 1
        thresholdType: 'Actual'
        contactEmails: [
          budgetAlertEmail
        ]
      }
      forecastToExceed: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        thresholdType: 'Forecasted'
        contactEmails: [
          budgetAlertEmail
        ]
      }
    }
  }
}

var deploySql = !empty(sqlAdminObjectId)
var sqlName = 'sql-${namePrefix}'
var sqlDatabaseName = 'idealpoints'

resource sqlServer 'Microsoft.Sql/servers@2024-05-01-preview' = if (deploySql) {
  name: sqlName
  location: sqlLocation
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    // A2: no password anywhere. Entra-only means there is no SQL login to leak.
    administrators: {
      administratorType: 'ActiveDirectory'
      login: sqlAdminLogin
      sid: sqlAdminObjectId
      tenantId: tenant().tenantId
      azureADOnlyAuthentication: true
    }
  }
  resource allowAzure 'firewallRules' = {
    name: 'AllowAllWindowsAzureIps'
    properties: {
      startIpAddress: '0.0.0.0'
      endIpAddress: '0.0.0.0'
    }
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2024-05-01-preview' = if (deploySql) {
  parent: sqlServer
  name: sqlDatabaseName
  location: sqlLocation
  sku: {
    name: 'GP_S_Gen5_2'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 2
  }
  properties: {
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
    autoPauseDelay: sqlAutoPauseDelayMinutes
    minCapacity: json('0.5')
    maxSizeBytes: 34359738368
    zoneRedundant: false
    requestedBackupStorageRedundancy: 'Local'
  }
}

@description('Name of the Function App, for the deploy workflow.')
output functionAppName string = functions.name

@description('Public base address of the API.')
output functionAppUrl string = 'https://${functions.properties.defaultHostName}'

@description('Managed identity of the Function App. T26 grants this access to SQL.')
output functionAppPrincipalId string = functions.identity.principalId

@description('Name of the Static Web App, for the deploy workflow.')
output staticWebAppName string = site.name

@description('Public address of the site.')
output staticWebAppUrl string = 'https://${site.properties.defaultHostname}'

@description('Fully qualified name of the SQL server, or empty when SQL is not deployed.')
output sqlServerFqdn string = sqlServer.?properties.fullyQualifiedDomainName ?? ''

@description('Name of the SQL server, for the migration step in T31.')
output sqlServerName string = deploySql ? sqlName : ''

@description('Database name, for T31.')
output sqlDatabaseName string = deploySql ? sqlDatabaseName : ''
