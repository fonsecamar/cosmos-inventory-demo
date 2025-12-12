@description('Cosmos DB account name, max length 44 characters, lowercase')
param cosmosAccountName string = 'cdbinv${suffix}'

@description('Function App name')
param functionAppName string = 'funcinv${suffix}'

@description('Storage account name, max length 44 characters, lowercase')
param storageAccountName string = 'blinv${suffix}'

@description('Location for resource deployment')
param location string = resourceGroup().location

@description('Suffix for resource deployment')
param suffix string = uniqueString(resourceGroup().id)

module cosmosdb 'cosmos.bicep' = {
  scope: resourceGroup()
  name: 'cosmosDeploy'
  params: {
    accountName: cosmosAccountName
    location: location
  }
}

module blob 'blob.bicep' = {
  name: 'blobDeploy'
  params: {
    storageAccountName: storageAccountName
    location: location
  }
}

module function 'functions.bicep' = {
  name: 'functionDeploy'
  params: {
    location: location
    functionAppName: functionAppName
    storageAccountName: blob.outputs.storageAccountName
    cosmosAccountName: cosmosdb.outputs.cosmosAccountName
    inventoryDatabase: cosmosdb.outputs.cosmosDatabaseName
    ledgerContainer: 'inventoryLedger'
    snapshotContainer: 'inventorySnapshot'
    syncInventoryContainer: 'syncInventory'
  }
}
