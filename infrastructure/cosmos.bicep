@description('Cosmos DB account name, max length 44 characters, lowercase')
param accountName string

@description('Location for the Cosmos DB account.')
param location string = resourceGroup().location

var databaseName = 'Inventory'

var containers = [
  {
    name: 'inventoryLedger'
    partitionKeys: ['/pk']
    index: {
      enabled: true
      includePaths: []
      excludePaths: [
        {
          path: '/*'
        }
      ]
      compositeIndexes: [
        [
          {
            path: '/pk'
            order: 'ascending'
          }
          {
            path: '/eventType'
            order: 'ascending'
          }
          {
            path: '/_ts'
            order: 'ascending'
          }
        ]
      ]
    }
  }
  {
    name: 'inventorySnapshot'
    partitionKeys: ['/id']
    index: {
      enabled: false
      includePaths: []
      excludePaths: []
      compositeIndexes: []
    }
  }
  {
    name: 'syncInventory'
    partitionKeys: ['/pk']
    index: {
      enabled: true
      includePaths: []
      excludePaths: [
        {
          path: '/*'
        }
      ]
      compositeIndexes: []
    }
  }
  {
    name: 'leases'
    partitionKeys: ['/id' ]
    index: {
      enabled: true
      includePaths: [
        {
          path: '/*'
        }
      ]
      excludePaths: []
      compositeIndexes: []
    }
  }
]

var locations = [
  {
    locationName: location
    failoverPriority: 0
    isZoneRedundant: false
  }
]

@description('Maximum autoscale throughput for the container')
@minValue(1000)
@maxValue(1000000)
param autoscaleMaxThroughput int = 1000

resource account 'Microsoft.DocumentDB/databaseAccounts@2022-05-15' = {
  name: toLower(accountName)
  kind: 'GlobalDocumentDB'
  location: location
  properties: {
    locations: locations
    databaseAccountOfferType: 'Standard'
  }
}

resource database 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2022-05-15' = {
  parent: account
  name: databaseName
  properties: {
    options: {
      autoscaleSettings: {
        maxThroughput: autoscaleMaxThroughput
      }
    }
    resource: {
      id: databaseName
    }
  }
}

resource container 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2022-05-15' = [for (config, i) in containers: {
  parent: database
  name: config.name
  properties: {
    resource: {
      id: config.name
      partitionKey: {
        paths: [for pk in config.partitionKeys: pk]
        kind: length(config.partitionKeys) == 1 ? 'Hash' : 'MultiHash'
        version: length(config.partitionKeys) == 1 ? 1 : 2
      }
      indexingPolicy: {
        automatic: config.index.enabled
        indexingMode: config.index.enabled ? 'consistent' : 'none'
        includedPaths: config.index.includePaths
        excludedPaths: config.index.excludePaths
        compositeIndexes: config.index.compositeIndexes
      }
    }
  }
}]

output cosmosAccountName string = account.name
output cosmosDatabaseName string = database.name
