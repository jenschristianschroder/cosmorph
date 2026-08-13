# Materials, constructions and Wardens

How a place accumulates something, what a Warden can build with it, and what changes on the wire when
it does. Everything here is integer arithmetic on a fixed tick, so a world is reproducible from its
seed alone.

## Materials

Every land cell holds a stock of three materials: **timber**, **stone** and **fibre**
(`Cosmorph.Domain.Ecology.CellResources`).

- A stock is a plain integer per material, capped at `MaxStock = 1000`. `Add` clamps at the ceiling
  rather than overflowing, so a cell can never hoard without bound.
- `TrySpend` is all or nothing. A cost that the stock does not cover in full deducts nothing.
- Water and ice hold nothing at all: `YieldPerTick` returns zero for any cell that is not land or has
  no carrying capacity.

**Yield is derived from the cell, not declared in the content pack.** `CellResources.YieldPerTick`
reads the biome, the elevation and how full the cell's biomass is against its carrying capacity:

| Material | Comes from | Scales with |
| --- | --- | --- |
| Timber | Rainforest 8, temperate forest 7, boreal forest 6, wetland 3 | Biomass fill |
| Stone | Mountain 8, desert 5, tundra 3, anything else 1, plus one per 400 m of elevation | Terrain only |
| Fibre | Grassland 7, wetland 6, rainforest 4, temperate forest 3, tundra 2, anything else 1 | Biomass fill |

Deriving the yield is what keeps `ContentPack.Season1.Version` at `season-1.0.0`. A world stored
before materials existed loads with an empty store and fills it on its next tick, with no
content-version change and therefore no world left unloadable.

`EcologyStep` adds the yield during the same pass that updates climate and stress, so accrual is part
of the ordinary tick and inherits its determinism.

## Constructions

A cell carries at most one structure (`Cosmorph.Domain.Ecology.Construction`), held on `WorldState`
in cell-index order.

| Kind | Effect | Ceiling at level 3, full condition |
| --- | --- | --- |
| Shelter | Absorbs some of the strain the species on the cell feel | 60 permille |
| Terrace | Raises the cell's biomass carrying capacity | 120 permille |
| Windbreak | Damps drought and flood accrual on the cell | 300 permille |

- Levels run 1 to 3. Each effect is `Condition.Scale(perLevel × Level)`, so a neglected structure
  helps less rather than helping the same amount forever.
- Cost rises with the level being reached — for example a shelter costs `40·L` timber, `10·L` stone
  and `30·L` fibre — and is paid out of the target cell's own stock. Materials are never moved
  between cells.
- Condition falls `DecayPerTick = 5` permille each tick. A structure that is never rebuilt stands for
  about 200 ticks, then is removed and a `ConstructionLost` event is written.
- Raising one writes `ConstructionRaised`. Both event types reach the Chronicle as their names, so no
  frontend change was needed to show them.

## The Warden action grammar

`WardenActionKind` gained `Build = 6` and the grammar version moved to **`warden-actions/2`**.

`WardenPlanner` proposes a build when the charter's highest-weighted goal maps to a structure, the
target cell can pay for it, and there is either nothing standing there or the same kind below its
top level:

| Goal | Structure |
| --- | --- |
| Preserve | Shelter |
| Expand | Terrace |
| Adapt | Windbreak |
| Cooperate, Hunt | nothing — such a Warden never builds |

`ProposalResolver` validates and then applies. A proposal is refused with:

- `InsufficientResources = 10` when the cell's stock does not cover the cost,
- `InsufficientBudget` when the charter's action budget is spent,
- `OutOfRange` when a different kind already stands on the cell, or the structure is already at
  `MaxLevel`.

A refused proposal changes nothing: no materials are spent and no budget is drawn.

## What changed on the wire

| Contract | Before | Now |
| --- | --- | --- |
| Stored snapshot | `world-snapshot/1` | writes `world-snapshot/2`, reads both |
| Spectator snapshot | `spectator-snapshot/1` | `spectator-snapshot/2`, two new optional cell arrays |
| Warden actions | `warden-actions/1` | `warden-actions/2` |
| Simulation version | `sim/1.0.0` | `sim/1.1.0` for newly created worlds |

- **Stored snapshots.** `CellColumns.Timber/Stone/Fibre` are nullable and `Constructions` defaults to
  empty, so a document written before this change still binds. Missing stock reads as zero and
  refills on the next tick. An unknown schema is still refused outright.
- **Spectator snapshots.** `resourceRichnessPermille` and `constructionKind` are optional, and
  `parseSnapshot` accepts `/1` and `/2`. A browser holding a cached older bundle degrades to the
  overlays it knows instead of reporting unreadable data.
- **New read.** `GET /api/worlds/{worldId}/cells/{cellIndex}` returns `spectator-cell/1`: terrain,
  climate, vitality, all four stresses, the stock and the next tick's yield, the structure if there
  is one, and the species living there. It is anonymous like the rest of the spectator surface,
  carries an ETag and `Cache-Control: public, max-age=5`, and answers an index outside the grid with
  exactly the 404 an unknown world gets. It carries **no Warden configuration**.
- During a rolling deploy the tick job may briefly write `/2` while an older API revision reads it.
  Both are redeployed in the same workflow run, so the window is a minute or two.

## Configuring a Warden

A Warden is configured by its **charter**: the species it looks after, the cells it governs, what it
is trying to do, and how much it may spend doing it.

**Reading.** `GET /api/worlds/{worldId}/wardens` returns `warden-charters/1`. It is owner-scoped, not
part of the spectator surface: it needs a bearer token carrying the mutation scope, answers a
non-owner with the same `404 World not found.` a stranger gets everywhere else, and sets
`Cache-Control: no-store`.

**Writing.** `PUT /api/worlds/{worldId}/wardens/{wardenId}/charter` takes the charter and requires an
`Idempotency-Key`. Writing to an unused warden id **creates** that Warden; writing to an existing one
replaces its configuration. The bounds are the ones on `WardenCharter`: a display name up to 40
characters, 1–3 goals with weights of 1–100, up to 4 taboos, a region of 1–512 cells that all lie
inside this world's grid, and budgets of 0–100. A charter outside them is refused with `400` before
anything is queued — it used to be accepted with `202` and then dropped silently by the domain.

**In the Observatory.** The Warden panel sits under the Chronicle and appears only when you are
signed in and the read succeeded, so a spectator never sees one. It lists each charter with its
remaining budget and the impact used this chapter, and its form creates or edits one. "Pick region"
turns clicks on the globe into region cells — while it is on, a click toggles a cell instead of
opening it, and the counter stops at 512 so the API is never asked for a region it would refuse.

**The change is pending until the next tick.** A charter write answers `202 Accepted`: it is queued
as a command and applied by the tick job, not by the API. The panel says so, and refetches once the
world version advances. Expect the new Warden to appear on the next tick, and its first action a tick
or more after that — it must find a cell it governs with enough materials, and it draws on its action
budget when it acts.
