---
applyTo: "src/Cosmorph.Api/**/*.cs,src/Cosmorph.TickJob/**/*.cs,src/Cosmorph.Infrastructure/**/*.cs,tests/Cosmorph.Infrastructure.Tests/**/*.cs,tests/Cosmorph.Api.Tests/**/*.cs"
---

# Cosmorph backend, persistence, and Worldmind instructions

## Application boundaries

- Expose persistence through IWorldStore, scheduling through IWorldSchedule, AI through IGameMaster, time through IClock, and random generation through domain-owned interfaces.
- Provide in-memory implementations for local development and tests.
- Keep Azure Blob and Azure AI implementations in Cosmorph.Infrastructure.
- The API is stateless. Never run world ticks in an HTTP request or an in-process hosted background service.
- The TickJob is the only normal writer of simulation outcomes. Other mutations create validated commands for a later tick.

## Blob layout

Use a documented, versioned layout similar to:

    worlds/{worldId}/manifest.json
    worlds/{worldId}/snapshots/{tick:D20}.json.br
    worlds/{worldId}/events/{chapterId}/{segment:D10}.jsonl.br
    worlds/{worldId}/commands/{commandId}.json
    schedule/{yyyyMMddHHmm}/{shard}/{worldId}.json
    scheduler/watermark/{shard}.json
    audit/worldmind/{worldId}/{decisionId}.json

Exact names may evolve, but preserve these properties:

- WorldId is validated before composing a path. Never accept a caller-supplied blob path.
- Use optimistic concurrency with ETags and a short blob lease for advancing one world.
- Acquire the lease, reload the current manifest, advance, write immutable data, then conditionally replace the manifest.
- A retry after an uncertain write must detect already-committed TickNumber, command identifiers, and decision identifiers.
- Snapshots are compressed canonical JSON with an explicit schema version.
- Event segments are immutable after close. Do not create one blob per ordinary event.
- The scheduler uses minute buckets and a persisted watermark so an outage is caught up without scanning every world.
- Bound the number of buckets and worlds handled by one job execution; leave durable work for the next run.

Construct BlobServiceClient from the account Blob service URI and DefaultAzureCredential. Never construct it from a connection string. Do not log access tokens, authorization headers, full prompts, or user-supplied text.

## Worldmind

IGameMaster must accept a GameMasterRequest built by the application. The request contains:

- World and simulation version identifiers.
- A canonical, size-bounded world summary.
- The significant situation type.
- Engine-created CandidateOutcome values with identifiers and hard numeric limits.
- Relevant Chronicle facts represented as data.
- A prompt-template version and request fingerprint.

The model returns a strict GameMasterDecision:

- Selected candidate identifier.
- Optional ranking of remaining candidate identifiers.
- Short spectator narration.
- Short internal rationale suitable for audit, with no chain-of-thought request.

The Worldmind cannot return scripts, tool calls, URLs, storage paths, arbitrary state patches, action names, or numeric values outside the selected candidate. Configure structured output or JSON schema where supported, deserialize into a dedicated DTO, then map through validation into a domain value.

Use DefaultAzureCredential for the Azure model endpoint and assign the TickJob's system identity the narrow data-plane role required to invoke the model. Keep a deterministic FakeGameMaster for tests and local development. Production startup must fail clearly when it is configured to use the fake.

Minimize model cost:

- Never call the model for routine ticks.
- Deduplicate by request fingerprint.
- Cache and persist accepted decisions.
- Bound request and response sizes.
- Use one call for a grouped significant situation when safe.
- Set strict timeouts and limited retries with jitter.
- Record token usage and estimated cost as metrics without storing raw sensitive text.

## HTTP API

Start with:

- GET /health/live
- GET /health/ready
- GET /api/worlds/{worldId}
- GET /api/worlds/{worldId}/snapshot
- GET /api/worlds/{worldId}/events?after={sequence}&limit={boundedLimit}
- POST /api/worlds
- PUT /api/worlds/{worldId}/wardens/{wardenId}/charter

Anonymous spectator reads may be allowed. Mutation endpoints require an authenticated, authorized actor and an idempotency key. If production authentication is not configured yet, production mutations must fail closed; do not add a development-header bypass that can activate in Production.

Return presentation DTOs rather than persistence or domain objects. Use ProblemDetails, validate limits before allocation, paginate events, emit ETags, and support conditional GET. Never expose internal prompts, model responses, storage paths, identity details, or exception traces.

