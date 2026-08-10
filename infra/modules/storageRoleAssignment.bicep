// Deterministic role assignment on a storage account. Names derive from scope, principal and role,
// so redeployment is idempotent.

@description('Name of the storage account that scopes the assignment.')
param storageAccountName string

@description('Object id of the system-assigned identity receiving the role.')
param principalId string

@description('Role definition GUID, for example Storage Blob Data Contributor.')
param roleDefinitionId string

resource storageAccount 'Microsoft.Storage/storageAccounts@2024-01-01' existing = {
  name: storageAccountName
}

resource assignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storageAccount
  name: guid(storageAccount.id, principalId, roleDefinitionId)
  properties: {
    principalId: principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleDefinitionId)
  }
}
