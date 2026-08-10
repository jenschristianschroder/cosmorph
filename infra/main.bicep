// Cosmorph MVP environment: one public web/API container, one scheduled tick job, and one keyless
// general-purpose v2 Storage account reached over a Blob private endpoint.
//
// Two-phase deployment: system-assigned identities do not exist before the first deployment, so this
// template creates the app and job from a harmless public bootstrap image and grants AcrPull. The
// deployment workflow then publishes the real images and rolls them out.

targetScope = 'resourceGroup'

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Short environment name, for example dev or prod.')
@minLength(2)
@maxLength(10)
param environmentName string

@description('Short application prefix used to compose resource names.')
@minLength(3)
@maxLength(10)
param namePrefix string = 'cosmorph'

@description('Resource tags for application, environment, owner and cost center.')
param tags object = {
  application: 'cosmorph'
  environment: environmentName
}

@description('Storage SKU for this environment.')
@allowed([
  'Standard_LRS'
  'Standard_ZRS'
])
param storageSkuName string = 'Standard_LRS'

@description('Image for the web/API container. Defaults to a harmless bootstrap image for phase one.')
param apiImage string = 'mcr.microsoft.com/k8se/quickstart:latest'

@description('Image for the tick job. Defaults to a harmless bootstrap image for phase one.')
param tickJobImage string = 'mcr.microsoft.com/k8se/quickstart:latest'

@description('Set to true once both images are published to the registry.')
param useRegistryImages bool = false

@description('HTTPS endpoint of the existing Azure AI resource.')
param modelEndpoint string

@description('Model deployment name.')
param modelDeployment string

@description('Name of the existing Azure AI account. Leave empty to skip the model role assignment.')
param modelAccountName string = ''

@description('Resource group of the existing Azure AI account. Defaults to this resource group.')
param modelResourceGroupName string = resourceGroup().name

@description('Maximum replicas for the web/API container app.')
@minValue(1)
@maxValue(10)
param apiMaxReplicas int = 3

@description('Tick job cron expression.')
param tickCronExpression string = '*/1 * * * *'

@description('Maximum worlds advanced by one tick execution.')
@minValue(1)
@maxValue(500)
param maxWorldsPerRun int = 25

@description('Maximum minute buckets drained by one tick execution.')
@minValue(1)
@maxValue(1440)
param maxBucketsPerRun int = 30

@description('Daily Log Analytics ingestion cap in GB.')
@minValue(1)
param logDailyQuotaGb int = 1

// Storage Blob Data Reader / Contributor.
var storageBlobDataReaderRoleId = '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1'
var storageBlobDataContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'

var uniquePart = uniqueString(resourceGroup().id, environmentName)
var baseName = '${namePrefix}-${environmentName}'
var storageAccountName = toLower('${take(replace(namePrefix, '-', ''), 8)}${take(replace(environmentName, '-', ''), 6)}${take(uniquePart, 8)}')
var registryName = toLower('${take(replace(namePrefix, '-', ''), 10)}${take(uniquePart, 8)}')

module network 'modules/network.bicep' = {
  name: 'network'
  params: {
    name: '${baseName}-vnet'
    location: location
    tags: tags
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    name: storageAccountName
    location: location
    tags: tags
    skuName: storageSkuName
    privateEndpointSubnetId: network.outputs.privateEndpointSubnetId
    virtualNetworkId: network.outputs.virtualNetworkId
  }
}

module registry 'modules/registry.bicep' = {
  name: 'registry'
  params: {
    name: registryName
    location: location
    tags: tags
  }
}

module observability 'modules/observability.bicep' = {
  name: 'observability'
  params: {
    name: '${baseName}-logs'
    location: location
    tags: tags
    dailyQuotaGb: logDailyQuotaGb
  }
}

module appEnvironment 'modules/appEnvironment.bicep' = {
  name: 'app-environment'
  params: {
    name: '${baseName}-env'
    location: location
    tags: tags
    infrastructureSubnetId: network.outputs.infrastructureSubnetId
    logAnalyticsWorkspaceId: observability.outputs.workspaceId
  }
}

module webApp 'modules/webApp.bicep' = {
  name: 'web-app'
  params: {
    name: '${baseName}-web'
    location: location
    tags: tags
    managedEnvironmentId: appEnvironment.outputs.environmentId
    image: apiImage
    registryLoginServer: useRegistryImages ? registry.outputs.loginServer : ''
    storageBlobServiceUri: storage.outputs.blobServiceUri
    modelEndpoint: modelEndpoint
    modelDeployment: modelDeployment
    maxReplicas: apiMaxReplicas
  }
}

module tickJob 'modules/tickJob.bicep' = {
  name: 'tick-job'
  params: {
    name: '${baseName}-tick'
    location: location
    tags: tags
    managedEnvironmentId: appEnvironment.outputs.environmentId
    image: tickJobImage
    registryLoginServer: useRegistryImages ? registry.outputs.loginServer : ''
    storageBlobServiceUri: storage.outputs.blobServiceUri
    modelEndpoint: modelEndpoint
    modelDeployment: modelDeployment
    cronExpression: tickCronExpression
    maxWorldsPerRun: maxWorldsPerRun
    maxBucketsPerRun: maxBucketsPerRun
  }
}

// The API only reads world state; the tick job is the only normal writer.
module webAppStorageRole 'modules/storageRoleAssignment.bicep' = {
  name: 'web-app-storage-role'
  params: {
    storageAccountName: storage.outputs.storageAccountName
    principalId: webApp.outputs.principalId
    roleDefinitionId: storageBlobDataReaderRoleId
  }
}

module tickJobStorageRole 'modules/storageRoleAssignment.bicep' = {
  name: 'tick-job-storage-role'
  params: {
    storageAccountName: storage.outputs.storageAccountName
    principalId: tickJob.outputs.principalId
    roleDefinitionId: storageBlobDataContributorRoleId
  }
}

module webAppAcrRole 'modules/registryRoleAssignment.bicep' = {
  name: 'web-app-acr-role'
  params: {
    registryName: registry.outputs.registryName
    principalId: webApp.outputs.principalId
  }
}

module tickJobAcrRole 'modules/registryRoleAssignment.bicep' = {
  name: 'tick-job-acr-role'
  params: {
    registryName: registry.outputs.registryName
    principalId: tickJob.outputs.principalId
  }
}

// Only the tick job invokes the Worldmind, so only that identity receives the model data-plane role.
module tickJobModelRole 'modules/modelRoleAssignment.bicep' = if (!empty(modelAccountName)) {
  name: 'tick-job-model-role'
  scope: resourceGroup(modelResourceGroupName)
  params: {
    aiAccountName: modelAccountName
    principalId: tickJob.outputs.principalId
  }
}

output webAppFqdn string = webApp.outputs.fqdn
output registryLoginServer string = registry.outputs.loginServer
output storageBlobServiceUri string = storage.outputs.blobServiceUri
output tickJobName string = tickJob.outputs.jobName
