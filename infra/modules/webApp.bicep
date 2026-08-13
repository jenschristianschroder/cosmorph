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

@description('''
False during phase one, when the public bootstrap image is running. That image listens on a
different port and serves no health endpoints, so the application port and probes only apply once the
real image is published.
''')
param useApplicationImage bool = false

@description('Port the published application image listens on.')
param applicationPort int = 8080

@description('Port the public bootstrap image listens on during phase one.')
param bootstrapPort int = 80

@description('Blob service URI of the world store.')
param storageBlobServiceUri string

@description('Azure AI model endpoint.')
param modelEndpoint string

@description('Azure AI model deployment name.')
param modelDeployment string

@description('''
Entra directory (tenant) identifier used to validate mutation tokens. Not a secret: it is part of
every sign-in request the browser makes. Empty leaves the mutation surface closed.
''')
param authTenantId string = ''

@description('''
Entra application (client) identifier. It is both the Observatory's public client and the audience
the API accepts. Not a secret; no client secret or certificate exists.
''')
param authClientId string = ''

@description('''
Lets a signed-in caller take ownership of a world that nobody owns. Ownership is never taken from an
existing owner. Off unless an environment asks for it.
''')
param allowAdoptingUnownedWorlds bool = false

@description('Maximum replicas. The API stays at zero minimum replicas until latency requires otherwise.')
@minValue(1)
@maxValue(10)
param maxReplicas int = 3

param cpu string = '0.5'
param memory string = '1Gi'

var activePort = useApplicationImage ? applicationPort : bootstrapPort

// The bootstrap image answers neither health path, so probing it would fail the revision and the
// whole deployment would time out before the real image is ever published.
var applicationProbes = [
  {
    type: 'Liveness'
    httpGet: {
      path: '/health/live'
      port: applicationPort
    }
    periodSeconds: 30
  }
  {
    type: 'Readiness'
    httpGet: {
      path: '/health/ready'
      port: applicationPort
    }
    periodSeconds: 15
  }
]

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
        targetPort: activePort
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
            {
              name: 'Cosmorph__Authentication__TenantId'
              value: authTenantId
            }
            {
              name: 'Cosmorph__Authentication__ClientId'
              value: authClientId
            }
            {
              name: 'Cosmorph__AllowAdoptingUnownedWorlds'
              value: string(allowAdoptingUnownedWorlds)
            }
          ]
          probes: useApplicationImage ? applicationProbes : []
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
