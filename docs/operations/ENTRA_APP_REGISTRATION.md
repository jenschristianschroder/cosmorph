# Entra app registration for Observatory sign-in

The API refuses to start in Production without `Cosmorph__Authentication__TenantId` and
`Cosmorph__Authentication__ClientId`, and the deploy workflow fails in its first job if the matching
GitHub variables are empty. This page is how those two values come to exist.

Bicep cannot create directory objects, so this is a one-time manual step per tenant. It creates
**no secret**: the registration is a public client using authorization code with PKCE, and
`az ad app credential list` on it must stay empty forever.

Both values are directory identifiers, not credentials. They are configuration in the same sense as
a resource name, and are stored as GitHub *variables* rather than secrets.

## What gets created

One registration acting as both the API and its browser client:

| Property | Value |
| --- | --- |
| Sign-in audience | `AzureADMyOrg` (this directory only) |
| Application ID URI | `api://<appId>` |
| Exposed scope | `World.Write` |
| Platform | Single-page application |
| Redirect URIs | the Observatory origin and `http://localhost:5173/` |
| Credentials | none |

## Steps

Run these signed in to the target tenant (`az login`). `WEB_FQDN` is the container app's ingress
host; take it from the deploy workflow output or `az containerapp show`.

```bash
WEB_FQDN=$(az containerapp show -g rg-cosmorph-dev -n cosmorph-dev-web \
  --query properties.configuration.ingress.fqdn -o tsv)

APP_ID=$(az ad app create \
  --display-name cosmorph-observatory \
  --sign-in-audience AzureADMyOrg \
  --query appId -o tsv)

# The identifier URI has to exist before a scope can be exposed under it.
az ad app update --id "$APP_ID" --identifier-uris "api://$APP_ID"
```

Expose the scope. `az ad app update` cannot express `oauth2PermissionScopes`, so this goes through
the Graph directly. The scope id is any GUID you choose and never changes afterwards:

```bash
# Any GUID; on Windows use powershell -NoProfile -Command "[guid]::NewGuid().ToString()".
SCOPE_ID=$(cat /proc/sys/kernel/random/uuid)

az rest --method patch \
  --url "https://graph.microsoft.com/v1.0/applications(appId='$APP_ID')" \
  --headers Content-Type=application/json \
  --body "{
    \"api\": {
      \"oauth2PermissionScopes\": [{
        \"id\": \"$SCOPE_ID\",
        \"value\": \"World.Write\",
        \"type\": \"User\",
        \"isEnabled\": true,
        \"adminConsentDisplayName\": \"Create and configure worlds\",
        \"adminConsentDescription\": \"Allows the signed-in person to create a world and set the charters of the Wardens in worlds they own.\",
        \"userConsentDisplayName\": \"Create and configure your worlds\",
        \"userConsentDescription\": \"Lets you create a world and configure the Wardens in worlds you created.\"
      }]
    },
    \"spa\": {
      \"redirectUris\": [\"https://$WEB_FQDN/\", \"http://localhost:5173/\"]
    }
  }"
```

Then, as a **second** call, list the registration against itself. Graph rejects a
`preAuthorizedApplications` entry whose permission id does not exist yet, so this cannot be merged
into the patch above:

```bash
az rest --method patch \
  --url "https://graph.microsoft.com/v1.0/applications(appId='$APP_ID')" \
  --headers Content-Type=application/json \
  --body "{\"api\":{\"preAuthorizedApplications\":[{\"appId\":\"$APP_ID\",\"delegatedPermissionIds\":[\"$SCOPE_ID\"]}]}}"
```

That is what stops the Observatory being asked to consent to an API that is the same application.

A registration is only a definition; sign-in needs a service principal for it in this tenant, and
`az ad app create` does not make one:

```bash
az ad sp create --id "$APP_ID"
```

Confirm no credential exists, then read out the two identifiers:

```bash
az ad app credential list --id "$APP_ID"   # must print []
az ad app show --id "$APP_ID" --query "{appId:appId, spa:spa.redirectUris, scopes:api.oauth2PermissionScopes[].value}"
az account show --query tenantId -o tsv
```

## Wiring it up

```bash
gh variable set COSMORPH_AUTH_TENANT_ID --body "$(az account show --query tenantId -o tsv)"
gh variable set COSMORPH_AUTH_CLIENT_ID --body "$APP_ID"
```

Then dispatch the `deploy` workflow. The API reads them as
`Cosmorph__Authentication__TenantId` / `Cosmorph__Authentication__ClientId` container app
environment variables, and republishes the pair — plus the scope URI — from `GET /api/config` so the
static Observatory build can sign in without being compiled per tenant.

## Adding an environment

A second environment (its own container app host) needs its origin added to `spa.redirectUris`; the
list above is replaced wholesale by a `PATCH`, so include the existing entries. A separate tenant
needs its own registration and its own pair of variables.

## Local development

Leave both settings unset. `/api/config` then reports no auth block, the Observatory renders no
sign-in control, and `MutationEndpoints` accepts mutations from `local-developer` — but only when the
process is non-production **and** running the in-memory store **and** the fake Worldmind. There is no
header or flag that opens the mutation surface in a deployed environment.

## Removing it

```bash
az ad app delete --id "$APP_ID"
gh variable delete COSMORPH_AUTH_TENANT_ID
gh variable delete COSMORPH_AUTH_CLIENT_ID
```

Deleting the application deletes its service principal with it.

The next deployment then fails in `verify` rather than starting an API that cannot authenticate
anyone. Worlds already created keep their `ownerId`, which will refer to an object identifier that
no longer resolves; nobody can reconfigure those worlds' Wardens afterwards.
