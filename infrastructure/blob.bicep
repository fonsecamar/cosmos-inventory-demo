@description('Storage account name, max length 44 characters, lowercase')
param storageAccountName string

@description('Resource cocation')
param location string = resourceGroup().location

resource blob 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    isHnsEnabled: true
  }
}
