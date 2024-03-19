@description('Cosmos DB account name, max length 44 characters, lowercase')
param accountName string

@description('Location for the Cosmos DB account.')
param location string = resourceGroup().location

@description('Maximum autoscale throughput for the container')
@minValue(1000)
@maxValue(1000000)
param autoscaleMaxThroughput int = 1000

var databaseName = 'Inventory'
var useDatabaseSharedThroughput = true

var containers = [
  {
    name: 'inventoryLedger'
    maxThroughput: 100000
    partitionKeys: ['/pk']
    index: {
      enabled: true
      includePaths: [
        {
          path: '/pk/?'
        }
      ]
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
    maxThroughput: 50000
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
    maxThroughput: 50000
    partitionKeys: ['/pk']
    index: {
      enabled: true
      includePaths: [
        {
          path: '/pk/?'
        }
      ]
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
    maxThroughput: 10000
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


resource account 'Microsoft.DocumentDB/databaseAccounts@2023-11-15' = {
  name: toLower(accountName)
  kind: 'GlobalDocumentDB'
  location: location
  properties: {
    locations: locations
    databaseAccountOfferType: 'Standard'
    backupPolicy: {
      type: 'Continuous'
    }
  }
}

resource database 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2023-11-15' = {
  parent: account
  name: databaseName
  properties: {
    options: {
      autoscaleSettings: useDatabaseSharedThroughput ? {
        maxThroughput: autoscaleMaxThroughput
      } : {}
    }
    resource: {
      id: databaseName
    }
  }
}

resource container 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2023-11-15' = [for (config, i) in containers: {
  parent: database
  name: config.name
  properties: {
    options: {
      autoscaleSettings: useDatabaseSharedThroughput ? {} : {
        maxThroughput: config.maxThroughput
      }
    }
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
