// Sampled, low-retention observability with an explicit daily cap so an event storm cannot become an
// unbounded bill.

@description('Azure region for the workspace.')
param location string

@description('Log Analytics workspace name.')
param name string

@description('Resource tags.')
param tags object = {}

@description('Retention in days.')
@minValue(30)
@maxValue(730)
param retentionInDays int = 30

@description('Daily ingestion cap in GB.')
@minValue(1)
param dailyQuotaGb int = 1

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: retentionInDays
    workspaceCapping: {
      dailyQuotaGb: dailyQuotaGb
    }
    features: {
      disableLocalAuth: true
    }
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

output workspaceId string = workspace.id
output customerId string = workspace.properties.customerId
