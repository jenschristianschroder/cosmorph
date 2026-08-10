---
applyTo: "src/Cosmorph.Domain/**/*.cs,src/Cosmorph.Application/**/*.cs,tests/Cosmorph.Domain.Tests/**/*.cs,tests/Cosmorph.Application.Tests/**/*.cs"
---

# Cosmorph domain and simulation instructions

Keep this code independent from ASP.NET Core, Azure SDKs, model SDKs, filesystems, and wall-clock APIs.

## Required model

Represent at least:

- WorldId, WorldSeed, SimulationVersion, SeasonId, ChapterId, TickNumber, and WorldInstant.
- WorldState containing immutable identity, current version, clock, cells, populations, Wardens, chapter state, and last committed event sequence.
- PlanetCell containing coordinates, biome, temperature, moisture, biomass, carrying capacity, and typed stress indicators.
- SpeciesPopulation containing species, cell, population, energy, health, and adaptation traits.
- WardenCharter containing only bounded enum goals, priorities, taboos, and a capability budget.
- WardenProposal, CandidateOutcome, GameMasterDecision, AppliedOutcome, and WorldEvent as distinct types.

Use a versioned equirectangular cell grid for the first implementation. Longitude wraps; latitude does not. Keep the topology behind an interface so a later equal-area or icosphere grid does not rewrite the simulation.

## Tick pipeline

A tick is a pure transition from an input state plus explicit context to a new state and ordered events:

1. Calculate seasonal temperature and moisture from world day, axial parameters, latitude, and seeded variation.
2. Update biomass and environmental stress.
3. Resolve species consumption, reproduction, mortality, migration, and adaptation.
4. Generate bounded Warden proposals from their charters and visible state.
5. Validate and resolve ordinary proposals with deterministic rules.
6. Detect significant situations and create engine-owned candidate outcomes for the Worldmind.
7. Apply only a validated Worldmind selection or a deterministic safe fallback.
8. Evaluate chapter progress and emit ordered, versioned events.

Do not put persistence, HTTP, serialization, logging, or model calls inside this pipeline.

## Offline progression

- Derive ticks due from persisted LastAdvancedAt, an explicit current instant, and the world's configured cadence.
- The result must not depend on how often a worker happened to wake up.
- Advance direct ticks in bounded batches. For long gaps, use a tested coarse-grained path that preserves conservation limits and records an explicit TimeCompressed event.
- Limit Worldmind calls during catch-up. Aggregate significant situations and request at most the configured number per catch-up window.
- A failed Worldmind call must not stop ordinary ecological progress. Apply a conservative deterministic fallback and record why.
- Retrying the same tick and decision input must produce the same committed result.

## Invariants

Enforce invariants in the domain, not only at API boundaries:

- No quantity becomes negative or exceeds its declared maximum.
- Population, biomass, energy, and action-budget changes stay within per-tick limits.
- Every referenced cell, species, Warden, candidate, season, and chapter belongs to the same world and version.
- A Warden can act only inside its granted scope and remaining budget.
- A GameMasterDecision can select only engine-created candidate identifiers.
- Events have strictly increasing sequence numbers and stable type/version names.
- Chapter and season migrations happen only at explicit boundaries.
- Existing world history is never rewritten by a content update.

Use checked arithmetic where overflow is possible. Prefer integer permille, basis points, and fixed-point value objects to doubles for authoritative values.

## Seasons and chapters

- Content packs and simulation rules are immutable and version-addressed.
- A world records the exact simulation and content version used for every event.
- A season provides capabilities and pressures, not a forced plot.
- A chapter closes with a snapshot and recap event.
- Migration code is explicit, one-way, testable, and runs only at a chapter boundary.

## Tests

Write table-driven and property-oriented tests for:

- Same seed plus same inputs produces byte-equivalent canonical state and events.
- Different seeds diverge.
- Longitude wrapping and latitude boundaries.
- Conservation and clamp invariants across thousands of ticks.
- Offline catch-up agrees with direct ticking within documented coarse-mode tolerances.
- Idempotent retries.
- Stale or malicious Warden and Worldmind inputs are rejected without partial mutation.
- Worlds never reference or mutate each other's identifiers.

