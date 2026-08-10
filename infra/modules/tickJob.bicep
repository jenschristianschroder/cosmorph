// Scheduled Container Apps Job that advances world time. One execution drains a bounded batch and
// exits; nothing runs between executions, so idle cost stays at zero.

@description('Azure region for the job.')
param location string

@description('Job name.')
param name string

@description('Resource tags.')
param tags object = {}

@description('Managed environment that hosts the job.')
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

@description('Cron expression. World advancement normally runs once per minute.')
param cronExpression string = '*/1 * * * *'

@description('Maximum worlds advanced by one execution.')
@minValue(1)
@maxValue(500)
param maxWorldsPerRun int = 25

@description('Maximum minute buckets drained by one execution.')
@minValue(1)
@maxValue(1440)
param maxBucketsPerRun int = 30

@description('Seconds an execution may run before Container Apps stops it.')
@minValue(60)
@maxValue(1800)
param replicaTimeoutSeconds int = 600

param cpu string = '0.25'
param memory string = '0.5Gi'

resource job 'Microsoft.App/jobs@2024-03-01' = {
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
      triggerType: 'Schedule'
      replicaTimeout: replicaTimeoutSeconds
      replicaRetryLimit: 0
      scheduleTriggerConfig: {
        cronExpression: cronExpression
        // Overlapping executions would double-advance worlds; leases also guard this at runtime.
        parallelism: 1
        replicaCompletionCount: 1
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
          name: 'tickjob'
          image: image
          resources: {
            cpu: json(cpu)
            memory: memory
          }
          env: [
            {
              name: 'DOTNET_ENVIRONMENT'
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
              name: 'Cosmorph__Simulation__MaxWorldsPerRun'
              value: string(maxWorldsPerRun)
            }
            {
              name: 'Cosmorph__Simulation__MaxBucketsPerRun'
              value: string(maxBucketsPerRun)
            }
          ]
        }
      ]
    }
  }
}

output principalId string = job.identity.principalId
output jobName string = job.name
