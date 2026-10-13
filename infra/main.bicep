metadata description = 'Ideal Points: Static Web App, Functions on Flex Consumption, Azure SQL, storage and telemetry.'

@description('Builds every resource name. Function App, SQL server and Static Web App names are global, so a second deployment needs its own.')
@minLength(3)
@maxLength(11)
param namePrefix string = 'idealpoints'

@description('The Sleeper league this site serves (A2).')
@secure()
@minLength(1)
param leagueId string

@description('Owner user ids left out of every metric (A1), comma separated. Empty when the league has no placeholder team.')
@secure()
param excludedOwnerIds string = ''

@description('Where the error alert is sent.')
@secure()
@minLength(1)
param alertEmail string

@description('Object id of the Entra principal that administers SQL, a GUID.')
@minLength(36)
@maxLength(36)
param sqlAdminObjectId string

@description('That principal\'s display name.')
@minLength(1)
param sqlAdminLogin string

@description('Custom domain serving the site, without a scheme. Empty for none.')
param customDomain string = ''

@description('A name to show instead of the league\'s name in Sleeper. Empty uses Sleeper\'s.')
param overrideLeagueName string = ''

var location = resourceGroup().location

// Static Web Apps runs in fewer regions than everything else
var staticWebAppLocation = 'eastus2'

// Azure SQL General Purpose is open to this subscription in westus2
var sqlLocation = 'westus2'
var sqlDatabaseName = 'idealpoints'

// 5 GB a month free, spread over 31 days
var dailyIngestCapGb = '0.16'

var excludedOwnerSettings = [
  for (ownerId, index) in filter(map(split(excludedOwnerIds, ','), id => trim(id)), id => !empty(id)): {
    name: 'League__ExcludedOwnerIds__${index}'
    value: ownerId
  }
]

// Storage Blob Data Owner, Queue Data Contributor and Table Data Contributor:
// what the Functions host and the app's blob stores need. CI may assign only
// these (runbook, step 4).
var storageRoleIds = [
  'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
  '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
  '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'
]

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
    allowBlobPublicAccess: false
    // everything reaches storage through the Function App's identity
    allowSharedKeyAccess: false
  }

  resource blob 'blobServices' = {
    name: 'default'

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
  properties: {} // required even when empty: preflight rejects a static site without it
}

resource functions 'Microsoft.Web/sites@2024-04-01' = {
  name: 'func-${namePrefix}'
  location: location
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  // Flex Consumption deploys into this container, and only the storage
  // account is an inferred dependency
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
            type: 'SystemAssignedIdentity'
          }
        }
      }
      scaleAndConcurrency: {
        maximumInstanceCount: 40 // the platform minimum
        instanceMemoryMB: 512 // the smallest size costs the fewest GB-seconds
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
          ['https://${site.properties.defaultHostname}'],
          empty(customDomain) ? [] : ['https://${customDomain}']
        )
      }
      appSettings: concat(
        [
          {
            // the host and the app's blob stores both connect by identity
            name: 'AzureWebJobsStorage__accountName'
            value: storage.name
          }
          {
            name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
            value: insights.properties.ConnectionString
          }
          {
            name: 'ConnectionStrings__FantasyLeague'
            value: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${sqlDatabaseName};Authentication=Active Directory Default;Encrypt=True;Connect Timeout=90;'
          }
          {
            name: 'League__CurrentLeagueId'
            value: leagueId
          }
          {
            name: 'League__OverrideLeagueName'
            value: overrideLeagueName
          }
        ],
        excludedOwnerSettings
      )
    }
  }
}

resource storageRoleAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for roleId in storageRoleIds: {
    name: guid(storage.id, functions.id, roleId)
    scope: storage
    properties: {
      principalId: functions.identity.principalId
      principalType: 'ServicePrincipal'
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleId)
    }
  }
]

// The budget is in budget.bicep, deployed once by hand: its start date cannot
// change after creation, which a pipeline that redeploys every push trips over (A31)

resource sqlServer 'Microsoft.Sql/servers@2023-08-01' = {
  name: 'sql-${namePrefix}'
  location: sqlLocation
  properties: {
    minimalTlsVersion: '1.2'
    // Entra only: no SQL login exists to leak
    administrators: {
      administratorType: 'ActiveDirectory'
      login: sqlAdminLogin
      sid: sqlAdminObjectId
      tenantId: tenant().tenantId
      azureADOnlyAuthentication: true
    }
  }

  // Accepted exception (standards, domain 4; A30): Flex Consumption has no
  // fixed outbound address to allow instead, and Entra-only sign-in is the control
  resource allowAzure 'firewallRules' = {
    name: 'AllowAllWindowsAzureIps'
    properties: {
      startIpAddress: '0.0.0.0'
      endIpAddress: '0.0.0.0'
    }
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01' = {
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
    autoPauseDelay: 60 // the free offer requires auto-pause, and 60 is its minimum
    minCapacity: json('0.5')
    maxSizeBytes: 32 * 1024 * 1024 * 1024 // the free offer's 32 GB
    requestedBackupStorageRedundancy: 'Local' // geo-redundant backup is billed
  }
}

resource alerts 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: 'ag-${namePrefix}'
  location: 'global'
  properties: {
    groupShortName: 'alerts'
    enabled: true
    emailReceivers: [
      {
        name: 'owner'
        emailAddress: alertEmail
        useCommonAlertSchema: true
      }
    ]
  }
}

// About $0.10 a month. Sees what the app throws, not what it logs as an error (A30)
resource exceptionAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-${namePrefix}-exceptions'
  location: 'global'
  properties: {
    description: 'The Function App threw an exception.'
    severity: 2
    enabled: true
    scopes: [
      insights.id
    ]
    evaluationFrequency: 'PT15M'
    windowSize: 'PT1H'
    autoMitigate: true
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          criterionType: 'StaticThresholdCriterion'
          name: 'exceptions'
          metricName: 'exceptions/count'
          metricNamespace: 'microsoft.insights/components'
          operator: 'GreaterThan'
          threshold: 0
          timeAggregation: 'Count'
        }
      ]
    }
    actions: [
      {
        actionGroupId: alerts.id
      }
    ]
  }
}

output functionAppName string = functions.name
output functionAppUrl string = 'https://${functions.properties.defaultHostName}'
output staticWebAppName string = site.name
output staticWebAppUrl string = 'https://${site.properties.defaultHostname}'
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output sqlDatabaseName string = sqlDatabase.name
