import { useCallback, useEffect, useMemo, useState } from 'react'
import { Globe } from './globe/Globe'
import { useWorldFeed, usePrefersReducedMotion } from './api/useWorldFeed'
import {
  BIOME_NAMES,
  legendFor,
  overlayExplanation,
  STRESS_NAMES,
  type OverlayMode,
} from './render/palette'
import { cellToLatLon } from './render/texture'
import { CellInspector } from './inspect/CellInspector'
import { useNeighbourhood } from './inspect/useNeighbourhood'
import { cellLabels } from './inspect/labels'
import { WardenPanel } from './wardens/WardenPanel'
import { MAX_REGION_CELLS } from './wardens/charters'
import { SignInPanel } from './auth/SignInPanel'
import { useAuth } from './auth/useAuth'
import type { SpectatorEvent } from './api/dto'

const OVERLAYS: readonly { readonly id: OverlayMode; readonly label: string }[] = [
  { id: 'condition', label: 'Condition' },
  { id: 'biome', label: 'Biome' },
  { id: 'vitality', label: 'Vitality' },
  { id: 'climate', label: 'Climate' },
  { id: 'population', label: 'Population pressure' },
  { id: 'resources', label: 'Materials' },
  { id: 'constructions', label: 'Constructions' },
  { id: 'recentChange', label: 'Recent change' },
]

export function App(): React.ReactElement {
  const [worldId, setWorldId] = useState<string | null>(null)
  const [overlay, setOverlay] = useState<OverlayMode>('condition')
  const [colorBlindMode, setColorBlindMode] = useState(false)
  const [tourPlaying, setTourPlaying] = useState(true)
  const [focus, setFocus] = useState<{ latitude: number; longitude: number } | null>(null)
  const [selectedEvent, setSelectedEvent] = useState<SpectatorEvent | null>(null)
  const [selectedCell, setSelectedCell] = useState<number | null>(null)
  const [neighbourhoodRadius, setNeighbourhoodRadius] = useState(1)

  // The region being drawn for a Warden charter. It lives here rather than in the panel because the
  // globe is what paints and picks it.
  const [region, setRegion] = useState<readonly number[]>([])
  const [picking, setPicking] = useState(false)

  // Bumped when this browser creates a world, which is the only moment the list can have changed
  // for us before the next poll would notice.
  const [worldsVersion, setWorldsVersion] = useState(0)

  const auth = useAuth()
  const reducedMotion = usePrefersReducedMotion()
  const feed = useWorldFeed(worldId, 5000, worldsVersion)

  const onWorldCreated = useCallback((created: string) => {
    setWorldId(created)
    setWorldsVersion((version) => version + 1)
  }, [])

  useEffect(() => {
    if (!worldId && feed.worlds && feed.worlds.worlds.length > 0) {
      setWorldId(feed.worlds.worlds[0]?.worldId ?? null)
    }
  }, [feed.worlds, worldId])

  // A cell index means nothing in another world, so a change of world clears what was selected.
  useEffect(() => {
    setSelectedCell(null)
    setSelectedEvent(null)
    setRegion([])
    setPicking(false)
  }, [worldId])

  const legend = useMemo(() => legendFor(overlay, colorBlindMode), [overlay, colorBlindMode])

  const onSelectEvent = useCallback(
    (event: SpectatorEvent) => {
      setSelectedEvent(event)
      const snapshot = feed.snapshot
      if (event.cellIndex !== undefined && snapshot) {
        setSelectedCell(event.cellIndex)
        setFocus(cellToLatLon(event.cellIndex, snapshot.gridWidth, snapshot.gridHeight))
      }
    },
    [feed.snapshot],
  )

  /**
   * Selecting a place also aims the camera at it, whether it was reached by clicking the planet, by
   * the Chronicle, or by typing a cell number. Passing a fresh object each time is deliberate: the
   * globe re-aims on identity, so picking the same cell twice still recentres it.
   */
  const onSelectCell = useCallback(
    (cellIndex: number | null) => {
      setSelectedCell(cellIndex)
      const snapshot = feed.snapshot
      if (cellIndex !== null && snapshot) {
        setFocus(cellToLatLon(cellIndex, snapshot.gridWidth, snapshot.gridHeight))
      }
    },
    [feed.snapshot],
  )

  /**
   * What a click on the planet does. While a charter region is being drawn a click toggles that cell
   * instead of opening it, so the same gesture never means two things at once.
   */
  const onGlobePick = useCallback(
    (cellIndex: number) => {
      if (!picking) {
        onSelectCell(cellIndex)
        return
      }

      setRegion((current) => {
        if (current.includes(cellIndex)) {
          return current.filter((cell) => cell !== cellIndex)
        }
        // The API refuses a larger region, so the cap is held here rather than discovered on save.
        return current.length >= MAX_REGION_CELLS ? current : [...current, cellIndex]
      })
    },
    [picking, onSelectCell],
  )

  const highlight = useMemo(() => new Set(region), [region])

  const summary = feed.summary
  const snapshot = feed.snapshot
  const isFakeWorldmind = summary?.worldmindMode.toLowerCase().includes('fake') ?? false

  // The one read behind both the panel and the text floating over the planet, refreshed on the same
  // version the snapshot poll advances so the two can never disagree about what lives where.
  const neighbourhood = useNeighbourhood(
    worldId,
    selectedCell,
    neighbourhoodRadius,
    snapshot?.version ?? null,
  )
  const labels = useMemo(() => cellLabels(neighbourhood.block), [neighbourhood.block])

  return (
    <div className="app">
      <Globe
        snapshot={snapshot}
        previousSnapshot={feed.previousSnapshot}
        overlay={overlay}
        colorBlindMode={colorBlindMode}
        reducedMotion={reducedMotion}
        autoRotate={tourPlaying && !reducedMotion}
        focus={focus}
        onSelectCell={onGlobePick}
        highlight={highlight}
        selectedCell={selectedCell}
        labels={labels}
      />

      <header className="panel panel-top">
        <h1>Cosmorph Observatory</h1>
        <label>
          World{' '}
          <select
            value={worldId ?? ''}
            onChange={(e) => setWorldId(e.target.value || null)}
            aria-label="Select a public world"
          >
            {(feed.worlds?.worlds ?? []).map((world) => (
              <option key={world.worldId} value={world.worldId}>
                {world.name}
              </option>
            ))}
          </select>
        </label>
        {isFakeWorldmind && (
          <p className="badge badge-warning" role="status">
            Local fake-AI mode — the Worldmind is a deterministic stub, not a model.
          </p>
        )}
        <p className={`badge badge-${feed.connection}`} role="status">
          {feed.connection === 'live' && 'Live'}
          {feed.connection === 'stale' && 'No change since the last poll'}
          {feed.connection === 'loading' && 'Loading…'}
          {feed.connection === 'disconnected' && 'Disconnected — retrying'}
          {feed.connection === 'corrupt' && 'Unreadable data — retrying'}
        </p>
        <SignInPanel auth={auth} onWorldCreated={onWorldCreated} />
      </header>

      <section className="panel panel-left" aria-label="World state">
        {summary ? (
          <dl>
            <dt>World</dt>
            <dd>{summary.name}</dd>
            <dt>Age</dt>
            <dd>{summary.day.toLocaleString()} days</dd>
            <dt>Season phase</dt>
            <dd>
              {summary.seasonPhase} (season {summary.season})
            </dd>
            <dt>Chapter</dt>
            <dd>{summary.chapter}</dd>
            <dt>Health</dt>
            <dd>{(summary.healthPermille / 10).toFixed(1)}%</dd>
            <dt>Last update</dt>
            <dd>{new Date(summary.lastUpdatedUtc).toLocaleString()}</dd>
            <dt>Simulation</dt>
            <dd>
              {summary.simulationVersion} · {summary.contentVersion}
            </dd>
          </dl>
        ) : (
          <p>Waiting for a world…</p>
        )}

        <fieldset>
          <legend>Overlay</legend>
          {OVERLAYS.map((item) => (
            <label key={item.id}>
              <input
                type="radio"
                name="overlay"
                value={item.id}
                checked={overlay === item.id}
                onChange={() => setOverlay(item.id)}
              />
              {item.label}
            </label>
          ))}
        </fieldset>

        <label>
          <input
            type="checkbox"
            checked={colorBlindMode}
            onChange={(e) => setColorBlindMode(e.target.checked)}
          />
          Colour-blind palette
        </label>

        <button type="button" onClick={() => setTourPlaying((playing) => !playing)}>
          {tourPlaying ? 'Pause tour' : 'Play tour'}
        </button>

        <p className="explanation">{overlayExplanation(overlay)}</p>

        <CellInspector
          cellIndex={selectedCell}
          gridWidth={snapshot?.gridWidth ?? 0}
          gridHeight={snapshot?.gridHeight ?? 0}
          block={neighbourhood.block}
          center={neighbourhood.center}
          loading={neighbourhood.loading}
          failed={neighbourhood.failed}
          radius={neighbourhoodRadius}
          onRadiusChange={setNeighbourhoodRadius}
          colorBlindMode={colorBlindMode}
          events={feed.events}
          onSelectCell={onSelectCell}
        />
      </section>

      <div className="panel panel-right">
        <WardenPanel
          auth={auth}
          worldId={worldId}
          species={snapshot?.species ?? []}
          worldVersion={snapshot?.version ?? null}
          region={region}
          picking={picking}
          onPickingChange={setPicking}
          onRegionChange={setRegion}
        />

        <section className="chronicle-scroll" aria-label="Chronicle">
          <h2>Chronicle</h2>
          {feed.events.length === 0 && <p>No events recorded yet.</p>}
          <ul className="chronicle">
            {feed.events.map((event) => (
              <li key={event.sequence}>
                <button type="button" onClick={() => onSelectEvent(event)}>
                  <span className="chronicle-type">{event.type}</span>
                  <span className="chronicle-tick">day {event.tick.toLocaleString()}</span>
                  {event.species && <span className="chronicle-species">{event.species}</span>}
                  {event.narration && <span className="chronicle-narration">{event.narration}</span>}
                </button>
              </li>
            ))}
          </ul>
          {selectedEvent && (
            <p className="explanation">
              Selected: {selectedEvent.type} at day {selectedEvent.tick.toLocaleString()} with
              magnitude {selectedEvent.magnitude}.
            </p>
          )}
        </section>
      </div>

      <section className="panel panel-bottom" aria-label="Legend">
        <h2>Legend</h2>
        <ul className="legend">
          {legend.map((entry) => (
            <li key={`${entry.label}-${entry.pattern}`} title={entry.description}>
              <span
                className={`swatch pattern-${entry.pattern}`}
                style={{
                  backgroundColor: `rgb(${entry.color.r}, ${entry.color.g}, ${entry.color.b})`,
                }}
                aria-hidden="true"
              />
              {entry.label}
            </li>
          ))}
        </ul>
      </section>

      <section className="visually-hidden" aria-label="Accessible world summary">
        <h2>Accessible summary</h2>
        {summary && snapshot ? (
          <p>
            {summary.name} is {summary.day.toLocaleString()} days old, in chapter {summary.chapter},
            during {summary.seasonPhase}. Overall health is{' '}
            {(summary.healthPermille / 10).toFixed(1)} percent.{' '}
            {describeDominantBiome(snapshot.cells.biome)} {describeStress(snapshot.cells.dominantStress)}
          </p>
        ) : (
          <p>World state is not available yet.</p>
        )}
        <p>
          {selectedCell === null
            ? 'No place is selected. The Place panel takes a cell number.'
            : `Cell ${selectedCell} is selected; the Place panel describes it.`}
        </p>
        <ul>
          {snapshot?.species.map((species) => (
            <li key={species.species}>
              {species.displayName} ({species.archetype}): {species.population.toLocaleString()}
            </li>
          ))}
        </ul>
      </section>
    </div>
  )
}

export function describeDominantBiome(biomes: readonly number[]): string {
  const counts = new Map<number, number>()
  for (const biome of biomes) {
    counts.set(biome, (counts.get(biome) ?? 0) + 1)
  }
  let best = -1
  let bestCount = -1
  for (const [biome, count] of counts) {
    if (count > bestCount) {
      best = biome
      bestCount = count
    }
  }
  const name = BIOME_NAMES[best] ?? 'unknown terrain'
  return `The most common terrain is ${name.toLowerCase()}.`
}

export function describeStress(stresses: readonly number[]): string {
  const counts = new Map<number, number>()
  for (const stress of stresses) {
    if (stress > 0) {
      counts.set(stress, (counts.get(stress) ?? 0) + 1)
    }
  }
  if (counts.size === 0) {
    return 'No cell is under notable stress.'
  }
  let best = 0
  let bestCount = 0
  for (const [stress, count] of counts) {
    if (count > bestCount) {
      best = stress
      bestCount = count
    }
  }
  return `The most widespread stress is ${(STRESS_NAMES[best] ?? 'unknown').toLowerCase()}, affecting ${bestCount} cells.`
}
