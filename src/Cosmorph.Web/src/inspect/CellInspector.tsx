import type { CellDetail, Neighbourhood, SpectatorEvent } from '../api/dto'
import { biomeColor, biomeIndex, constructionDescription } from '../render/palette'
import { compact, leadingSpecies, spaced } from './labels'

export interface CellInspectorProps {
  readonly cellIndex: number | null
  readonly gridWidth: number
  readonly gridHeight: number
  /** The selected place and the places around it, read by whoever owns the poll. */
  readonly block: Neighbourhood | null
  readonly center: CellDetail | null
  readonly loading: boolean
  readonly failed: boolean
  readonly radius: number
  readonly onRadiusChange: (radius: number) => void
  readonly colorBlindMode: boolean
  readonly events: readonly SpectatorEvent[]
  readonly onSelectCell: (cellIndex: number | null) => void
}

/**
 * Everything known about one place, and a readable block of the places around it. The globe canvas
 * is `aria-hidden`, so the cell number field and this grid are the keyboard and screen-reader route
 * to the same state that clicking the planet, and the labels floating over it, reach.
 */
export function CellInspector(props: CellInspectorProps): React.ReactElement {
  const detail = props.center
  const block = props.block
  const cellCount = Math.max(1, props.gridWidth * props.gridHeight)

  // The feed holds only the most recent entries, so these are labelled as recent rather than all.
  const recent = props.events.filter((event) => event.cellIndex === props.cellIndex).slice(0, 8)
  const eventCells = new Set(props.events.map((event) => event.cellIndex))

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
      {props.cellIndex !== null && props.loading && !detail && <p role="status">Reading the place…</p>}
      {props.failed && <p role="status">That place could not be read. It may be outside the world.</p>}

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
        </>
      )}

      {block && (
        <>
          <h3>Around here</h3>
          <label>
            Reach{' '}
            <select
              value={props.radius}
              onChange={(e) => props.onRadiusChange(Number.parseInt(e.target.value, 10) || 1)}
              aria-label="How far around the selected place to read"
            >
              <option value={1}>3 × 3</option>
              <option value={2}>5 × 5</option>
            </select>
          </label>
          <div
            className="neighbourhood"
            style={{ gridTemplateColumns: `repeat(${block.columns}, minmax(0, 1fr))` }}
          >
            {block.cells.map((cell) => (
              <NeighbourCard
                key={cell.cellIndex}
                cell={cell}
                isCenter={cell.cellIndex === block.centerCellIndex}
                hasEvent={eventCells.has(cell.cellIndex)}
                colorBlindMode={props.colorBlindMode}
                onSelect={props.onSelectCell}
              />
            ))}
          </div>
        </>
      )}

      {detail && (
        <>
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

interface NeighbourCardProps {
  readonly cell: CellDetail
  readonly isCenter: boolean
  readonly hasEvent: boolean
  readonly colorBlindMode: boolean
  readonly onSelect: (cellIndex: number) => void
}

/** One place in the block. The whole card is the button, so the hit target matches what you see. */
function NeighbourCard(props: NeighbourCardProps): React.ReactElement {
  const cell = props.cell
  const color = biomeColor(biomeIndex(cell.biome), props.colorBlindMode)
  const leading = leadingSpecies(cell)

  return (
    <button
      type="button"
      className={`neighbour${props.isCenter ? ' neighbour-center' : ''}`}
      aria-current={props.isCenter ? 'true' : undefined}
      onClick={() => props.onSelect(cell.cellIndex)}
      title={`${spaced(cell.biome)} at cell ${cell.cellIndex}`}
    >
      <span
        className="neighbour-swatch"
        style={{ backgroundColor: `rgb(${color.r}, ${color.g}, ${color.b})` }}
        aria-hidden="true"
      />
      <span className="neighbour-index">
        Cell {cell.cellIndex}
        {props.hasEvent && (
          <>
            {' '}
            <span aria-hidden="true">•</span>
            <span className="visually-hidden">, something happened here recently</span>
          </>
        )}
      </span>
      <span className="neighbour-life">
        {leading ? `${leading.displayName} ${compact(leading.population)}` : 'lifeless'}
      </span>
      <span className="neighbour-materials">
        timber {compact(cell.timber)} · stone {compact(cell.stone)} · fibre {compact(cell.fibre)}
      </span>
      <span className="neighbour-note">
        {cell.construction ? cell.construction.kind : spaced(cell.biome)}
        {cell.dominantStress === 'None' ? '' : ` · ${spaced(cell.dominantStress)}`}
      </span>
    </button>
  )
}

function pct(permille: number): string {
  return `${(permille / 10).toFixed(0)}%`
}

function formatLatitude(degrees: number): string {
  return `${Math.abs(degrees)}° ${degrees < 0 ? 'S' : 'N'}`
}

function formatLongitude(degrees: number): string {
  return `${Math.abs(degrees)}° ${degrees < 0 ? 'W' : 'E'}`
}
