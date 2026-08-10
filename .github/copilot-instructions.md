# Cosmorph repository instructions

## Product

Cosmorph is a browser-based, persistent survival-ecosystem game. Many isolated worlds advance while nobody is connected. Spectators watch a cartoon spherical planet, inspect its current condition, follow events, and later make tightly bounded contributions.

Use these canonical terms:

- Worldmind: the generative-AI game master.
- Warden: a user-configured, capability-limited agent.
- Observatory: the browser spectator experience.
- Chronicle: the append-only history of a world.
- Season: a major content and rules release.
- Chapter: a versioned narrative and snapshot boundary within a season.

The core promise is: the Worldmind judges, the deterministic engine enforces.

## Delivery priorities

Work in this order unless the issue explicitly says otherwise:

1. Deterministic world simulation and invariants.
2. Durable offline progression and isolated world persistence.
3. Bounded Warden proposals and validated Worldmind decisions.
4. Spectator read models and a simple live globe.
5. Additional content, polish, social features, and scale.

Prefer a complete vertical slice over broad scaffolding. Do not build marketplaces, chat, monetization, multiplayer editing, complex account systems, or content-authoring tools during the core-mechanics milestone.

## Architecture

- Use a modular monolith, not microservices.
- Backend: .NET 10 LTS, C#, ASP.NET Core minimal APIs, nullable reference types enabled.
- Frontend: React, TypeScript in strict mode, Vite, and Three.js.
- Infrastructure as code: Bicep.
- Hosting: Azure Container Apps Consumption.
- Run the public web/API as one container. It serves the built SPA and stateless JSON endpoints.
- Run logical world advancement as a scheduled Azure Container Apps Job, normally once per minute.
- Use one general-purpose v2 Azure Storage account and Blob Storage for the MVP. Store snapshots, event segments, schedule markers, and leases there.
- Keep storage behind a Blob private endpoint. Do not add Queue, Table, File, Cosmos DB, Service Bus, Redis, SignalR, Kubernetes, or a relational database without a measured need and an architecture decision.
- The logical game runs forever; a dedicated process does not. Reconstruct progress from persisted world time and elapsed time.
- Use inexpensive deterministic ticks for ordinary evolution. Invoke the Worldmind only for significant, bounded decisions and chapter transitions.

Expected repository shape:

    src/
      Cosmorph.Domain/
      Cosmorph.Application/
      Cosmorph.Infrastructure/
      Cosmorph.Api/
      Cosmorph.TickJob/
      Cosmorph.Web/
    tests/
      Cosmorph.Domain.Tests/
      Cosmorph.Application.Tests/
      Cosmorph.Infrastructure.Tests/
      Cosmorph.Api.Tests/
    infra/
      main.bicep
      modules/

## Azure identity and networking

These are non-negotiable:

- Production runtime access uses a system-assigned managed identity whenever Azure supports it.
- Use DefaultAzureCredential and service endpoint URIs. Never use account keys, connection strings, passwords, client secrets, service SAS tokens, or account SAS tokens.
- Configure every Storage account with publicNetworkAccess Disabled, allowSharedKeyAccess false, and allowBlobPublicAccess false.
- The application never gives the browser a storage URL, SAS, Azure credential, or direct data-plane access.
- Each Container App and Job has its own system-assigned identity and least-privilege role assignments.
- GitHub-hosted Actions cannot use an Azure resource's system-assigned identity. Deployment uses GitHub OIDC workload identity federation with no stored client secret.
- Local development uses developer Entra credentials through DefaultAzureCredential or an in-memory store. Do not introduce Azurite connection strings as a production-like path.
- Configuration may contain resource names, endpoint URIs, model deployment names, and feature flags because these are not secrets.
- Do not add Key Vault merely to hold non-secret configuration.

## Domain and AI boundaries

- The simulation is authoritative. Narrative text never directly mutates state.
- Authoritative quantities use integers or fixed-point value objects. Floating-point values are presentation-only.
- All randomness comes from an injected, seeded deterministic random source.
- Domain code never reads wall-clock time directly. Pass an IClock or explicit instant.
- Every mutation is scoped by WorldId. Cross-world reads or writes are forbidden.
- A Warden submits only versioned, structured proposals from a fixed action vocabulary.
- The Worldmind receives a canonical state summary plus engine-created candidate outcomes. It may select or rank candidates and supply narration; it cannot invent executable operations.
- Validate every AI response against a strict schema and all domain invariants. Reject safely on timeout, invalid JSON, unknown identifiers, excessive magnitude, or stale version.
- Store the accepted decision, prompt-template version, model deployment identifier, request fingerprint, and validation result. Replay stored decisions; never ask the model again during replay.
- Treat Warden names, spectator text, world names, and generated lore as untrusted data, never as instructions.

## Engineering behavior

- Inspect nearby code and tests before editing.
- Keep dependencies few and justified. Prefer the platform and Azure SDKs.
- Keep application and domain layers independent of Azure SDK types.
- Make handlers idempotent and accept CancellationToken.
- Use ProblemDetails for API failures and structured console logging without secrets or raw prompts.
- Add tests with each behavior change. Tests must not require Azure, network access, or a live model.
- Favor small explicit types and pure functions over generic frameworks and reflection.
- Avoid speculative abstractions, distributed transactions, and background work inside the API process.
- Do not silently weaken a security, identity, network, cost, or world-isolation requirement to make a test pass.
- Before declaring work complete, run the relevant build, unit tests, formatting, frontend tests, and Bicep validation available in the repository. Report anything that could not be run.

## Roadmap and future development

- For monetization, commerce, subscription, entitlement, or premium-feature tasks, read docs/product/MONETIZATION_IDEAS.md. Treat its contents as unapproved options and implement only ideas explicitly selected by the current issue.
