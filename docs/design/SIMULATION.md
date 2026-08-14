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
- **New reads.** `GET /api/worlds/{worldId}/cells/{cellIndex}` returns `spectator-cell/1`: terrain,
  climate, vitality, all four stresses, the stock and the next tick's yield, the structure if there
  is one, and the species living there. `…/neighbourhood` returns `spectator-neighbourhood/1`, the
  block of those same places around a centre. Both are anonymous like the rest of the spectator
  surface, carry an ETag and `Cache-Control: public, max-age=5`, and answer an index outside the grid
  with exactly the 404 an unknown world gets. Neither carries **any Warden configuration**.
- During a rolling deploy the tick job may briefly write `/2` while an older API revision reads it.
  Both are redeployed in the same workflow run, so the window is a minute or two.

## Looking at a place

Zooming into a cell used to give a smooth green field: the state texture is magnified with a linear
filter, so the closer you looked the less there was to see. Up close the Observatory now shows the
live state of the place itself rather than a colour standing in for it.

**The neighbourhood read.** `GET /api/worlds/{worldId}/cells/{cellIndex}/neighbourhood?radius=1`
returns `spectator-neighbourhood/1`: the centre and the block around it, `rows` × `columns`
row-major, each item **the same `spectator-cell/1` shape the single-cell read returns**. One mapper
and one parser serve both, so a place cannot describe itself differently depending on how it was
asked for.

- `radius` is 0, 1 or 2 — 1, 9 or 25 places. Anything else is `400` rather than quietly trimmed.
- Columns wrap across the date line; rows are **clipped** at the poles, because the grid is a
  cylinder and there is no row above the top one to fold onto. A block at the pole is therefore
  smaller than a block at the equator, which is why `rows` and `columns` are on the envelope instead
  of being re-derived from the radius.
- Anonymous, ETagged as `"{kind}-{worldId}-{version}"` with the reach in the kind so a radius-2 read
  is never served from a radius-1 entry, `Cache-Control: public, max-age=5`, a centre outside the
  grid answered with exactly the 404 an unknown world gets, and **no Warden configuration**.

**On the globe.** `uCellSnap` rises from 0 at a camera distance of 2.6 to 1 at 1.8, mixing each
sample toward its cell centre and drawing a cell edge, so cells sharpen into discrete tiles as you
close in while the distant planet keeps the soft look it had. The selected cell is outlined.
Labels — species and population, the centre's material stock, what is built there or the biome, and
the dominant stress — are positioned per frame through `cellToPoint`, hidden when the cell faces away
or the camera is further than 2.4, and marked `aria-hidden` because the panel is the accessible
route. The projection lives in `render/projection.ts`, away from WebGL, so a click and a label are
answering the same arithmetic: project a cell centre onto the sphere, read it back, and you get the
cell you started from.

**In the panel.** The inspector reads the centre out of the same block and lays the rest out as an
**Around here** grid, one card per place. The neighbourhood replaced the single-cell read in the
browser rather than joining it — the block already contains the centre, and polling both would have
doubled every read. It refetches when the snapshot version advances, so populations and materials
keep pace with the tick without touching the selection.

## What the planet shows up close

Sharp tiles are still only colours. Past the same threshold that sharpens them, the planet draws the
life of every cell facing the camera: the species as critters, the materials as things standing on
the ground, and the terrain, climate, biomass and causes of stress as the ground itself.

**The life read.** `GET /api/worlds/{worldId}/life` returns `spectator-life/1`: one column per thing
the close-up draws, each `gridWidth * gridHeight` long — `elevation`, `biomassPermille`, `timber`,
`stone`, `fibre`, `seaLevel`, and a `species` entry per species carrying its own `population` column.
The species are listed in the same order, and with the same ids, as the snapshot's world totals.

It deliberately carries **nothing the snapshot already carries** — no biome, temperature, moisture,
stress or construction — so the two reads can never disagree about the same cell; an API test asserts
those field names are absent. It is not folded into the snapshot either: that is polled every five
seconds by every viewer, including the ones looking at the whole globe. Anonymous, ETagged on the
world version, `max-age=5`, and no Warden or owner field anywhere in it.

The browser fetches it **only while the camera is close enough to draw it**. `GlobeScene` reports the
crossing edge-triggered, taking hold at a distance of 2.6 and letting go again at 2.75 — the gap is
there so a camera resting exactly on the threshold cannot flap the fetch on and off. `useWorldLife`
refetches when the snapshot version advances, so the critters keep pace with the tick without a poll
of their own, and drops its data on the way out.

**The ground.** A second texture, one RGBA texel per cell, nearest-filtered because it is read per
cell and never blended. Four bytes for five values, so two of them are packed:

| Channel | Carries |
| --- | --- |
| R | Elevation, 0–1000 scaled to the byte. Sea level lands at 133, so the coastline is one comparison |
| G | Biomass as a share of the cell's carrying capacity |
| B | Climate: `temperatureStep * 12 + moistureStep`, 21 temperature bands by 12 of moisture |
| A | Stress: `kind * 51 + round(strength / 1000 * 50)`, cause and strength riding together |

Stress is read from the world state rather than from the hatch byte of the state texture, because
that byte is zero on every overlay but Condition and Recent change — the causes of stress have to
show whichever overlay is chosen. Everything the close-up adds is multiplied by `uCellSnap`, so the
distant planet is pixel-for-pixel what it was before.

What each one looks like: relief lit from four elevation taps with a pale shore where land meets
water; a stipple of ground cover as thick as the biomass; frost when cold, a dusty pallor when hot
and dry, a darker sheen and rain when wet. Then one treatment per cause of stress — **drought**
cracks the ground, **disease** mottles it purple, **fire** flickers with embers, **flood** ripples a
sheen over it — so the four never read as the same trouble.

**The critters and props.** One `THREE.InstancedMesh` over a unit quad, billboarded in view space
from a centre on the sphere, silhouettes cut out of it per kind in the fragment shader. Anything that
has turned away from the camera is scaled to nothing, which is the cheapest back-face cull there is,
and the whole layer is hidden when the fade is zero — a viewer who never zooms in pays nothing.

`render/detail.ts` decides what stands where, with no WebGL and no `Math.random` in it. Per land
cell: one critter **per individual** capped at four per species, a conifer for timber, a boulder for
stone, a tuft for fibre — each sized by the stock, and only above a stock of 120 — and one prop for
whatever a Warden has built. Positions come from the cell centre, offset by an integer hash of the
cell and the slot and bounded to well inside the cell, so a thing is provably standing on the place
whose numbers put it there, and it is still standing there when the next poll lands five seconds
later. Buffers are allocated once at `min(32_768, cells * 16)` and refilled in place.

A species this bundle has never heard of — what a newer content pack sends an older browser — is
drawn as the silhouette of its archetype rather than dropped from the planet.

**Reduced motion needs no switch of its own.** The render loop only advances `uTime` when motion is
allowed, so the wander, the rain, the embers, the flicker and the flood ripple all freeze into their
own still image. Nothing disappears; it simply stops moving.

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

**In the Observatory.** The Warden panel leads the right column, above the Chronicle, which scrolls
in a box of its own. It used to be the last child of the Chronicle inside one long scrolling column,
which put it below sixty entries where nobody would find it. It appears only when you are signed in,
so a spectator never sees one. It lists each charter with its remaining budget and the impact used
this chapter, and its form creates or edits one. "Pick region" turns clicks on the globe into region
cells — while it is on, a click toggles a cell instead of opening it, and the counter stops at 512 so
the API is never asked for a region it would refuse. When the charter read is refused the panel says
so in one line rather than rendering nothing, which is what it did before.

**Adopting a world.** `POST /api/worlds/{worldId}/owner` is how a world created before ownership
existed becomes configurable again. It is authenticated like any mutation, and:

- It is refused with the constant-shape `404` unless `Cosmorph:AllowAdoptingUnownedWorlds` is set, so
  an environment that never enables adoption discloses nothing about which worlds are unowned. The
  flag reaches the container as `Cosmorph__AllowAdoptingUnownedWorlds` and is off by default.
- **Ownership is never taken from anyone.** The only transition is none → you. A world owned by
  somebody else is answered exactly as an unknown world is, so a stranger learns nothing by asking.
- Already yours answers `200`, so adopting twice is the same as adopting once. The manifest is
  rewritten by the tick job on every advance, so a lost race is ordinary: the write is retried once
  against the concurrency token and then answered `409`.

**The change is pending until the next tick.** A charter write answers `202 Accepted`: it is queued
as a command and applied by the tick job, not by the API. The panel says so, and refetches once the
world version advances. Expect the new Warden to appear on the next tick, and its first action a tick
or more after that — it must find a cell it governs with enough materials, and it draws on its action
budget when it acts.
