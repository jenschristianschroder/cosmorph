// Basic registry with admin credentials disabled. Runtimes pull with AcrPull on their own
// system-assigned identity; nothing stores a registry password.

@description('Azure region for the registry.')
param location string

@description('Registry name (5-50 alphanumeric characters).')
param name string

@description('Resource tags.')
param tags object = {}

resource registry 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: false
    anonymousPullEnabled: false
    dataEndpointEnabled: false
    publicNetworkAccess: 'Enabled'
  }
}

output registryId string = registry.id
output registryName string = registry.name
output loginServer string = registry.properties.loginServer
