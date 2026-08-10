// Virtual network with a delegated Container Apps infrastructure subnet and a separate subnet for
// private endpoints. No NAT gateway, gateway or firewall: the MVP only needs private Blob access.

@description('Azure region for the virtual network.')
param location string

@description('Virtual network name.')
param name string

@description('Resource tags.')
param tags object = {}

@description('Address space of the virtual network.')
param addressPrefix string = '10.10.0.0/16'

@description('Address prefix of the Container Apps infrastructure subnet. Consumption requires at least /27.')
param infrastructureSubnetPrefix string = '10.10.0.0/23'

@description('Address prefix of the private endpoint subnet.')
param privateEndpointSubnetPrefix string = '10.10.2.0/27'

resource virtualNetwork 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    addressSpace: {
      addressPrefixes: [addressPrefix]
    }
    subnets: [
      {
        name: 'infrastructure'
        properties: {
          addressPrefix: infrastructureSubnetPrefix
          delegations: [
            {
              name: 'container-apps'
              properties: {
                serviceName: 'Microsoft.App/environments'
              }
            }
          ]
        }
      }
      {
        name: 'private-endpoints'
        properties: {
          addressPrefix: privateEndpointSubnetPrefix
          privateEndpointNetworkPolicies: 'Disabled'
        }
      }
    ]
  }
}

output virtualNetworkId string = virtualNetwork.id
output infrastructureSubnetId string = virtualNetwork.properties.subnets[0].id
output privateEndpointSubnetId string = virtualNetwork.properties.subnets[1].id
