import type { SpectatorEvent } from '../api/dto'
import { constructionDescription } from '../render/palette'
import { useCellDetail } from './useCellDetail'

export interface CellInspectorProps {
  readonly worldId: string | null
  readonly cellIndex: number | null
  readonly gridWidth: number
  readonly gridHeight: number
  /** The snapshot version, so the open inspector refreshes as the world advances. */
  readonly version: number | null
  readonly events: readonly SpectatorEvent[]
  readonly onSelectCell: (cellIndex: number | null) => void
}

/**
 * Everything known about one place. The globe canvas is `aria-hidden`, so the cell number field here
 * is the keyboard and screen-reader route to the same state that clicking the planet reaches.
 */
export function CellInspector(props: CellInspectorProps): React.ReactElement {
  const { detail, loading, failed } = useCellDetail(props.worldId, props.cellIndex, props.version)
  const cellCount = Math.max(1, props.gridWidth * props.gridHeight)

  // The feed holds only the most recent entries, so these are labelled as recent rather than all.
  const recent = props.events.filter((event) => event.cellIndex === props.cellIndex).slice(0, 8)

  return (
    <section className="panel-section inspector" aria-label="Selected place">
      <h2>Place</h2>

      <label>
        Cell{' '}
        <input
          type="number"
          min={0}
          max={cellCount - 1}
          value={props.cellIndex ?? ''}
          onChange={(e) => {
            const parsed = Number.parseInt(e.target.value, 10)
            props.onSelectCell(
              Number.isFinite(parsed) && parsed >= 0 && parsed < cellCount ? parsed : null,
            )
          }}
        />
      </label>

      {props.cellIndex === null && <p>Click the planet, or type a cell number, to look at a place.</p>}
      {props.cellIndex !== null && loading && !detail && <p role="status">Reading the place…</p>}
      {failed && <p role="status">That place could not be read. It may be outside the world.</p>}

      {detail && (
        <>
          <dl>
            <dt>Position</dt>
            <dd>
              {formatLatitude(detail.latitudeDegrees)}, {formatLongitude(detail.longitudeDegrees)}
            </dd>
            <dt>Terrain</dt>
            <dd>
              {spaced(detail.biome)} · elevation {detail.elevation}
            </dd>
            <dt>Climate</dt>
            <dd>
              {(detail.temperatureDeciC / 10).toFixed(1)} °C ·{' '}
              {(detail.moisturePermille / 10).toFixed(0)}% moisture
            </dd>
            <dt>Vitality</dt>
            <dd>{(detail.vitalityPermille / 10).toFixed(1)}%</dd>
            <dt>Living space</dt>
            <dd>
              biomass {detail.biomass.toLocaleString()} of {detail.carryingCapacity.toLocaleString()}
            </dd>
            <dt>Stress</dt>
            <dd>
              {detail.dominantStress === 'None'
                ? 'None'
                : `${spaced(detail.dominantStress)} leads — drought ${pct(detail.droughtPermille)}, disease ${pct(detail.diseasePermille)}, fire ${pct(detail.firePermille)}, flood ${pct(detail.floodPermille)}`}
            </dd>
            <dt>Materials</dt>
            <dd>
              timber {detail.timber} (+{detail.timberYield}), stone {detail.stone} (+
              {detail.stoneYield}), fibre {detail.fibre} (+{detail.fibreYield})
            </dd>
            <dt>Construction</dt>
            <dd>
              {detail.construction ? (
                <>
                  {detail.construction.kind} level {detail.construction.level}, condition{' '}
                  {(detail.construction.conditionPermille / 10).toFixed(0)}%.{' '}
                  {constructionDescription(detail.construction.kind)}
                </>
              ) : (
                'Nothing has been built here.'
              )}
            </dd>
          </dl>

          <h3>Species here</h3>
          {detail.species.length === 0 ? (
            <p>Nothing lives here.</p>
          ) : (
            <ul className="cell-species">
              {detail.species.map((entry) => (
                <li key={entry.species}>
                  <span className="cell-species-name">
                    {entry.displayName} ({entry.archetype.toLowerCase()})
                  </span>
                  <span className="cell-species-count">{entry.population.toLocaleString()}</span>
                  <span className="cell-species-traits">
                    health {(entry.healthPermille / 10).toFixed(0)}% · energy {entry.energy} · cold{' '}
                    {entry.coldTolerance} · drought {entry.droughtTolerance}
                  </span>
                </li>
              ))}
            </ul>
          )}

          <h3>Recent events here</h3>
          {recent.length === 0 ? (
            <p>Nothing recent has happened here.</p>
          ) : (
            <ul className="cell-events">
              {recent.map((event) => (
                <li key={event.sequence}>
                  {event.type} — day {event.tick.toLocaleString()}
                  {event.narration ? `: ${event.narration}` : ''}
                </li>
              ))}
            </ul>
          )}

          <button type="button" onClick={() => props.onSelectCell(null)}>
            Clear selection
          </button>
        </>
      )}
    </section>
  )
}

function pct(permille: number): string {
  return `${(permille / 10).toFixed(0)}%`
}

/** Splits the PascalCase enum names the API sends, so `BorealForest` reads as `Boreal forest`. */
function spaced(name: string): string {
  const words = name.replace(/([a-z])([A-Z])/g, '$1 $2')
  return words.charAt(0) + words.slice(1).toLowerCase()
}

function formatLatitude(degrees: number): string {
  return `${Math.abs(degrees)}° ${degrees < 0 ? 'S' : 'N'}`
}

function formatLongitude(degrees: number): string {
  return `${Math.abs(degrees)}° ${degrees < 0 ? 'W' : 'E'}`
}
