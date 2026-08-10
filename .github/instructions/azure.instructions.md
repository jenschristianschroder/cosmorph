---
applyTo: "infra/**/*.bicep,infra/**/*.bicepparam,.github/workflows/*.yml,.github/workflows/*.yaml,**/Dockerfile,**/Dockerfile.*"
---

# Cosmorph Azure and delivery instructions

Optimize the MVP for low idle cost, keyless operation, and the subscription's storage policies.

## Target resources

Provision with idempotent Bicep modules:

- One resource group per environment.
- One VNet with a delegated Container Apps infrastructure subnet and a separate private-endpoint subnet.
- One Azure Container Apps environment using the Consumption workload profile.
- One externally accessible Container App for the SPA and API, with minReplicas 0 and a small bounded maxReplicas value.
- One scheduled Container Apps Job for world ticks, normally on cron */1 * * * *, with small CPU and memory allocations and bounded parallelism.
- One Standard general-purpose v2 Storage account using LRS for the initial environment.
- One Blob private endpoint and the recommended privatelink.blob.core.windows.net private DNS zone linked to the VNet.
- One Basic Azure Container Registry with admin credentials disabled. Keep its public endpoint only if organizational policy permits; private ACR requires Premium and should not be introduced accidentally.
- A low-retention, sampled observability setup with explicit daily caps where supported.
- An existing or separately provisioned Azure AI model resource and deployment, passed by resource identifier and endpoint rather than API key.

Do not provision Queue or Table private endpoints unless application code actually uses those services. Azure Storage needs a distinct private endpoint per service, so unused services create recurring cost.

## Mandatory Storage configuration

The Storage account must set:

- publicNetworkAccess: Disabled
- allowSharedKeyAccess: false
- allowBlobPublicAccess: false
- minimumTlsVersion: TLS1_2 or later when supported
- supportsHttpsTrafficOnly: true

Use the normal blob service hostname in application configuration; private DNS resolves it to the private endpoint. Do not configure the application with the privatelink hostname.

Add automated deployment assertions for these properties and for the existence of the Blob private endpoint and DNS link.

## Managed identities and RBAC

- Enable a distinct system-assigned identity on the web/API app and TickJob.
- Grant the web/API identity Storage Blob Data Reader at the narrowest practical scope.
- Grant the TickJob identity Storage Blob Data Contributor at the narrowest practical scope.
- Grant only the identity that calls the model the required Azure AI data-plane user role.
- Grant each runtime identity AcrPull if that runtime pulls from ACR.
- Use system as the identity for managed-identity-capable Container Apps scale rules.
- Avoid subscription- or resource-group-wide data-plane roles.
- Role-assignment names must be deterministic.

System-assigned identity creation can create an initial ACR-pull ordering problem. Prefer a documented two-phase deployment or a harmless bootstrap image, then grant AcrPull and deploy the application image. If a user-assigned identity is genuinely required for bootstrap, isolate it to image pull, explain the exception in an ADR, and do not use credentials.

## GitHub Actions

- Authenticate to Azure with GitHub OIDC workload identity federation and azure/login.
- Set permissions contents: read and id-token: write at the job level.
- Store tenant, subscription, and client identifiers as configuration variables, not secrets.
- Never create or store a client secret, publish profile, storage key, registry password, or Azure credential JSON.
- Build and test before authenticating to Azure when practical.
- Pin actions to reviewed commit SHAs for production workflows.
- Separate infrastructure validation, image build, and environment deployment.
- Require an environment approval for production.
- Do not deploy from pull requests originating from untrusted forks.

GitHub-hosted runners cannot use an Azure resource's system-assigned identity. OIDC federation is the explicit deployment-time exception to the runtime system-assigned-identity rule.

## Cost controls

- Keep the API at zero minimum replicas until latency measurements require otherwise.
- Jobs stop after completing their bounded batch.
- Do not create an always-on worker merely to represent persistent world time.
- Invoke the model only for significant events.
- Use Blob Storage rather than adding a database during the vertical slice.
- Avoid Dapr, NAT Gateway, Application Gateway, Front Door, Redis, Service Bus, SignalR, Defender upgrades, Premium ACR, and extra private endpoints unless a requirement and cost estimate justify them.
- Parameterize SKU, retention, maximum replicas, tick batch size, and model deployment.
- Add resource tags for application, environment, owner, and cost center when provided.
- Never estimate or hard-code an Azure price in code; document the metered dimensions instead.

Validate Bicep, lint container images, and add a what-if step before deployment. Infrastructure tests must fail if shared-key access or public Storage networking is re-enabled.

