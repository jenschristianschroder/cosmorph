// Deterministic data-plane role assignment on an existing Azure AI (Cognitive Services) account.
// Deploy this module with a resourceGroup() scope when the model lives in another resource group.

@description('Name of the existing Azure AI account that hosts the model deployment.')
param aiAccountName string

@description('Object id of the system-assigned identity receiving the role.')
param principalId string

// Cognitive Services OpenAI User: inference only, no management or deployment rights.
var openAiUserRoleId = '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'

resource aiAccount 'Microsoft.CognitiveServices/accounts@2024-10-01' existing = {
  name: aiAccountName
}

resource assignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: aiAccount
  name: guid(aiAccount.id, principalId, openAiUserRoleId)
  properties: {
    principalId: principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', openAiUserRoleId)
  }
}
