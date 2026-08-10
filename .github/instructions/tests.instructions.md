---
applyTo: "tests/**/*.cs,src/Cosmorph.Web/**/*.test.ts,src/Cosmorph.Web/**/*.test.tsx,src/Cosmorph.Web/**/*.spec.ts,src/Cosmorph.Web/**/*.spec.tsx"
---

# Cosmorph test instructions

- Tests are deterministic, parallel-safe, and independent of execution order.
- Unit and integration tests must not require an Azure subscription, live storage, network, or live model.
- Use an in-memory IWorldStore, fake clock, seeded random source, and FakeGameMaster.
- Avoid sleeps. Advance the fake clock or invoke the scheduler directly.
- Name tests as behavior and outcome, not implementation detail.
- Build canonical state fixtures with test builders rather than large copied JSON documents.
- Keep golden files small, versioned, and intentional. Explain any update that changes canonical serialization.
- Assert events and externally visible state, not private method calls.
- Use property-oriented loops for thousands of seeds and ticks where a full property-testing dependency is unnecessary.
- Add contract tests that run the same persistence behavior against in-memory and Azure implementations when an opt-in Azure test environment is available.
- Test cancellation, timeouts, retries, idempotency, ETag conflicts, leases, and partial failures.
- Test malicious and malformed Worldmind responses through the real parser and validator.
- Test that spectator DTOs omit private Warden configuration, internal rationale, prompts, storage paths, and identities.
- Frontend tests cover overlay mapping, legend consistency, reduced motion, stale data, event cursor recovery, and disposal of Three.js resources.
- A failing invariant test must print the seed, tick, world version, and smallest useful state summary for reproduction.

