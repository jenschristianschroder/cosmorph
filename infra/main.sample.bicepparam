// Sample parameters. Copy this file per environment and supply the model resource that already
// exists in the subscription. Nothing here is a secret: names, URIs and deployment names only.
using 'main.bicep'

param environmentName = 'dev'
param location = 'swedencentral'
param modelEndpoint = 'https://replace-me.openai.azure.com/'
param modelDeployment = 'gpt-4o-mini'
param modelAccountName = 'replace-me'
param useRegistryImages = false
param tags = {
  application: 'cosmorph'
  environment: 'dev'
  owner: 'replace-me'
  costCenter: 'replace-me'
}
