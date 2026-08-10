# Cosmorph infrastructure

`main.bicep` provisions one environment: a VNet, a keyless Storage account reached over a Blob
private endpoint, a Basic container registry, a Log Analytics workspace with a daily cap, a Container
Apps environment on the Consumption workload profile, the public web/API container app and the
scheduled tick job.

## Deploying

```bash
az deployment group create \
  --resource-group <rg> \
  --template-file infra/main.bicep \
  --parameters environmentName=dev modelEndpoint=<https endpoint> modelDeployment=<deployment>
```

Both runtimes are created from a public bootstrap image and receive AcrPull on their own
system-assigned identity, because those identities do not exist before the first deployment. The
`deploy` workflow then builds the real images with `az acr build` and rolls them out.

## Deployment identity

GitHub Actions authenticates with OIDC workload identity federation. Configure these repository or
environment **variables** (they are identifiers and endpoints, not secrets):

| Variable | Purpose |
| --- | --- |
| `AZURE_CLIENT_ID` | Application id of the federated identity |
| `AZURE_TENANT_ID` | Entra tenant id |
| `AZURE_SUBSCRIPTION_ID` | Target subscription |
| `AZURE_RESOURCE_GROUP` | Target resource group |
| `COSMORPH_MODEL_ENDPOINT` | HTTPS endpoint of the existing Azure AI resource |
| `COSMORPH_MODEL_DEPLOYMENT` | Model deployment name |
| `COSMORPH_MODEL_ACCOUNT` | Azure AI account name used for the data-plane role assignment |

No storage key, registry password, publish profile or credential JSON is ever stored.

## Invariants covered by tests

`tests/Cosmorph.Infrastructure.Tests/DeploymentTemplateTests.cs` fails if a template re-enables
shared-key access or public Storage networking, drops the Blob private endpoint or its DNS zone,
introduces a secret parameter, grants a broad role, or unpins a workflow action. The deployment
workflow repeats the Storage assertions against the live resource after each deployment.

## Metered dimensions

Cost is driven by Container Apps vCPU-seconds and requests, Storage capacity and transactions, Log
Analytics ingestion, registry storage and build minutes, private endpoint hours, and model tokens.
The API scales to zero, the job exits after a bounded batch, and the Worldmind is invoked only for
significant decisions.
