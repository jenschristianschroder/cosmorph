// Keyless Blob Storage. Shared keys and public network access are disabled, so the only supported
// access path is a managed identity over the private endpoint.

@description('Azure region for the storage account.')
param location string

@description('Storage account name (3-24 lowercase alphanumeric characters).')
param name string

@description('Resource tags.')
param tags object = {}

@description('Storage SKU. LRS is sufficient for the initial environment.')
@allowed([
  'Standard_LRS'
  'Standard_ZRS'
])
param skuName string = 'Standard_LRS'

@description('''
Blob container that holds snapshots, event segments, schedule markers and leases. This must match
BlobPaths.ContainerName in the application; world data lives under a worlds/ prefix inside it.
''')
param containerName string = 'cosmorph'

@description('Subnet that hosts the Blob private endpoint.')
param privateEndpointSubnetId string

@description('Virtual network linked to the private DNS zone.')
param virtualNetworkId string

resource storageAccount 'Microsoft.Storage/storageAccounts@2024-01-01' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: skuName
  }
  kind: 'StorageV2'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    allowCrossTenantReplication: false
    defaultToOAuthAuthentication: true
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    publicNetworkAccess: 'Disabled'
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Deny'
      ipRules: []
      virtualNetworkRules: []
    }
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2024-01-01' = {
  parent: storageAccount
  name: 'default'
  properties: {
    deleteRetentionPolicy: {
      enabled: true
      days: 7
    }
  }
}

resource worldsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' = {
  parent: blobService
  name: containerName
  properties: {
    publicAccess: 'None'
  }
}

// Only the Blob service is used, so only the Blob private endpoint is created. Each Storage service
// needs its own endpoint and every unused endpoint is a recurring cost.
resource privateDnsZone 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: 'privatelink.blob.${environment().suffixes.storage}'
  location: 'global'
  tags: tags
}

resource privateDnsZoneLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: privateDnsZone
  name: '${name}-vnet-link'
  location: 'global'
  tags: tags
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: virtualNetworkId
    }
  }
}

resource blobPrivateEndpoint 'Microsoft.Network/privateEndpoints@2024-05-01' = {
  name: '${name}-blob-pe'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: privateEndpointSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: 'blob'
        properties: {
          privateLinkServiceId: storageAccount.id
          groupIds: ['blob']
        }
      }
    ]
  }
}

resource blobPrivateDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-05-01' = {
  parent: blobPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'blob'
        properties: {
          privateDnsZoneId: privateDnsZone.id
        }
      }
    ]
  }
}

output storageAccountId string = storageAccount.id
output storageAccountName string = storageAccount.name

// Applications use the normal blob hostname; private DNS resolves it to the private endpoint.
output blobServiceUri string = storageAccount.properties.primaryEndpoints.blob
output containerName string = worldsContainer.name
