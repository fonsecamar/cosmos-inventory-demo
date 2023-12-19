@description('Resource location')
param location string = resourceGroup().location

@description('Function app name')
param functionAppName string

@description('Storage account name')
param storageAccountName string

@description('Cosmos DB account name')
param cosmosAccountName string

@description('Cosmos DB database name')
param inventoryDatabase string

@description('Cosmos DB ledger container name')
param ledgerContainer string

@description('Cosmos DB snapshot container name')
param snapshotContainer string

@description('Cosmos DB Sync Inventory container name')
param syncInventoryContainer string



resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: functionAppName
  location: location
  properties: {
    retentionInDays: 30
    sku: {
      name: 'PerGB2018'
    }
  }
}

resource appInsightsApi 'Microsoft.Insights/components@2020-02-02' = {
  name: 'api-${functionAppName}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

resource appInsightsWorker 'Microsoft.Insights/components@2020-02-02' = {
  name: 'worker-${functionAppName}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

resource plan 'Microsoft.Web/serverfarms@2020-12-01' = {
  name: '${functionAppName}Plan'
  location: location
  kind: 'functionapp'
  sku: {
    name: 'B1'
  }
}

resource blob 'Microsoft.Storage/storageAccounts@2022-09-01' existing = {
  name: storageAccountName
}

resource functionAppApi 'Microsoft.Web/sites@2022-03-01' = {
  name: 'api-${functionAppName}'
  location: location
  kind: 'functionapp'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    siteConfig: {
      use32BitWorkerProcess: false
      netFrameworkVersion: 'v8.0'
      
      appSettings: [
        {
          name: 'AzureWebJobsStorage'
          value: 'DefaultEndpointsProtocol=https;AccountName=${blob.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${blob.listKeys().keys[0].value}'
        }
        {
          name: 'WEBSITE_CONTENTAZUREFILECONNECTIONSTRING'
          value: 'DefaultEndpointsProtocol=https;AccountName=${blob.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${blob.listKeys().keys[0].value}'
        }
        {
          name: 'APPINSIGHTS_INSTRUMENTATIONKEY'
          value: appInsightsApi.properties.InstrumentationKey
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: 'InstrumentationKey=${appInsightsApi.properties.InstrumentationKey}'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'WEBSITE_RUN_FROM_PACKAGE'
          value: '1'
        }
        {
          name: 'WEBSITE_USE_PLACEHOLDER_DOTNETISOLATED'
          value: '1'
        }
        {
          name: 'CosmosInventoryConnection__accountEndpoint'
          value: 'https://${cosmosAccountName}.documents.azure.com:443/'
        }
        {
          name: 'inventoryDatabase'
          value: inventoryDatabase
        }
        {
          name: 'ledgerContainer'
          value: ledgerContainer
        }
        {
          name: 'snapshotContainer'
          value: snapshotContainer
        }
        {
          name: 'syncInventoryContainer'
          value: syncInventoryContainer 
        }
        {
          name: 'lowAvailabilityThreshold'
          value: '5'
        }
      ]
    }
    httpsOnly: true
  }
}

resource functionAppWorker 'Microsoft.Web/sites@2022-03-01' = {
  name: 'worker-${functionAppName}'
  location: location
  kind: 'functionapp'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    siteConfig: {
      use32BitWorkerProcess: false
      netFrameworkVersion: 'v8.0'
      
      appSettings: [
        {
          name: 'AzureWebJobsStorage'
          value: 'DefaultEndpointsProtocol=https;AccountName=${blob.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${blob.listKeys().keys[0].value}'
        }
        {
          name: 'WEBSITE_CONTENTAZUREFILECONNECTIONSTRING'
          value: 'DefaultEndpointsProtocol=https;AccountName=${blob.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${blob.listKeys().keys[0].value}'
        }
        {
          name: 'APPINSIGHTS_INSTRUMENTATIONKEY'
          value: appInsightsWorker.properties.InstrumentationKey
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: 'InstrumentationKey=${appInsightsWorker.properties.InstrumentationKey}'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'WEBSITE_RUN_FROM_PACKAGE'
          value: '1'
        }
        {
          name: 'WEBSITE_USE_PLACEHOLDER_DOTNETISOLATED'
          value: '1'
        }
        {
          name: 'CosmosInventoryConnection__accountEndpoint'
          value: 'https://${cosmosAccountName}.documents.azure.com:443/'
        }
        {
          name: 'inventoryDatabase'
          value: inventoryDatabase
        }
        {
          name: 'ledgerContainer'
          value: ledgerContainer
        }
        {
          name: 'snapshotContainer'
          value: snapshotContainer
        }
      ]
    }
    httpsOnly: true
  }
}

resource cosmos 'Microsoft.DocumentDB/databaseAccounts@2022-08-15' existing = {
  name: cosmosAccountName
}

resource roleAssignmentCosmosApi 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2022-08-15' = {
  name: guid(cosmos.id, functionAppApi.id, 'CosmosContributor')
  parent: cosmos
  properties: {
    scope: cosmos.id
    roleDefinitionId: resourceId('Microsoft.DocumentDB/databaseAccounts/sqlRoleDefinitions', cosmos.name, '00000000-0000-0000-0000-000000000002') //Cosmos DB Built-in Data Contributor
    principalId: functionAppApi.identity.principalId
  }
}

resource roleAssignmentCosmosWorker 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2022-08-15' = {
  name: guid(cosmos.id, functionAppWorker.id, 'CosmosContributor')
  parent: cosmos
  properties: {
    scope: cosmos.id
    roleDefinitionId: resourceId('Microsoft.DocumentDB/databaseAccounts/sqlRoleDefinitions', cosmos.name, '00000000-0000-0000-0000-000000000002') //Cosmos DB Built-in Data Contributor
    principalId: functionAppWorker.identity.principalId
  }
}
