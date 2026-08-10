// Externally reachable Container App that serves the Observatory and the JSON API. It scales to zero
// and reads world state with its own system-assigned identity.

@description('Azure region for the container app.')
param location string

@description('Container app name.')
param name string

@description('Resource tags.')
param tags object = {}

@description('Managed environment that hosts the app.')
param managedEnvironmentId string

@description('Fully qualified container image.')
param image string

@description('Registry login server used for managed-identity pulls. Empty for public images.')
param registryLoginServer string = ''

@description('Blob service URI of the world store.')
param storageBlobServiceUri string

@description('Azure AI model endpoint.')
param modelEndpoint string

@description('Azure AI model deployment name.')
param modelDeployment string

@description('Maximum replicas. The API stays at zero minimum replicas until latency requires otherwise.')
@minValue(1)
@maxValue(10)
param maxReplicas int = 3

param cpu string = '0.5'
param memory string = '1Gi'

resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: name
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    environmentId: managedEnvironmentId
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
      }
      registries: empty(registryLoginServer) ? [] : [
        {
          server: registryLoginServer
          identity: 'system'
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: image
          resources: {
            cpu: json(cpu)
            memory: memory
          }
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'Cosmorph__StorageBlobServiceUri'
              value: storageBlobServiceUri
            }
            {
              name: 'Cosmorph__ModelEndpoint'
              value: modelEndpoint
            }
            {
              name: 'Cosmorph__ModelDeployment'
              value: modelDeployment
            }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health/live'
                port: 8080
              }
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health/ready'
                port: 8080
              }
              periodSeconds: 15
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: maxReplicas
        rules: [
          {
            name: 'http'
            http: {
              metadata: {
                concurrentRequests: '40'
              }
            }
          }
        ]
      }
    }
  }
}

output principalId string = containerApp.identity.principalId
output fqdn string = containerApp.properties.configuration.ingress.fqdn
