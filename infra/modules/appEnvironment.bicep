// Container Apps environment on the Consumption workload profile, VNet-injected so egress to Storage
// uses the private endpoint. Logs go to Azure Monitor, which avoids the workspace shared key that
// the log-analytics destination would require.

@description('Azure region for the environment.')
param location string

@description('Managed environment name.')
param name string

@description('Resource tags.')
param tags object = {}

@description('Delegated Container Apps infrastructure subnet.')
param infrastructureSubnetId string

@description('Log Analytics workspace that receives diagnostic logs.')
param logAnalyticsWorkspaceId string

resource managedEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'azure-monitor'
    }
    vnetConfiguration: {
      internal: false
      infrastructureSubnetId: infrastructureSubnetId
    }
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
    zoneRedundant: false
  }
}

resource diagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  scope: managedEnvironment
  name: 'to-log-analytics'
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      {
        category: 'ContainerAppConsoleLogs'
        enabled: true
      }
      {
        category: 'ContainerAppSystemLogs'
        enabled: true
      }
    ]
  }
}

output environmentId string = managedEnvironment.id
output defaultDomain string = managedEnvironment.properties.defaultDomain
