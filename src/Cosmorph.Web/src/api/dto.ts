/** Versioned spectator DTOs. Everything crossing the network boundary is validated before use. */

export const SNAPSHOT_SCHEMA = 'spectator-snapshot/1'

export interface WorldListItem {
  readonly worldId: string
  readonly name: string
  readonly tick: number
  readonly chapter: number
  readonly lastAdvancedAtUtc: string
}

export interface WorldList {
  readonly worlds: readonly WorldListItem[]
  readonly worldmindMode: string
}

export interface WorldSummary {
  readonly worldId: string
  readonly name: string
  readonly tick: number
  readonly day: number
  readonly season: number
  readonly chapter: number
  readonly seasonPhase: string
  readonly healthPermille: number
  readonly isPaused: boolean
  readonly lastUpdatedUtc: string
  readonly gridWidth: number
  readonly gridHeight: number
  readonly simulationVersion: string
  readonly contentVersion: string
  readonly worldmindMode: string
}

export interface SpeciesTotal {
  readonly species: string
  readonly displayName: string
  readonly archetype: string
  readonly population: number
}

export interface SpectatorCells {
  readonly biome: readonly number[]
  readonly vitalityPermille: readonly number[]
  readonly dominantStress: readonly number[]
  readonly stressPermille: readonly number[]
  readonly temperatureDeciC: readonly number[]
  readonly moisturePermille: readonly number[]
  readonly populationPressurePermille: readonly number[]
}

export interface SpectatorSnapshot {
  readonly schema: string
  readonly worldId: string
  readonly tick: number
  readonly version: number
  readonly day: number
  readonly season: number
  readonly chapter: number
  readonly healthPermille: number
  readonly gridWidth: number
  readonly gridHeight: number
  readonly lastEventSequence: number
  readonly species: readonly SpeciesTotal[]
  readonly cells: SpectatorCells
}

export interface SpectatorEvent {
  readonly sequence: number
  readonly tick: number
  readonly type: string
  readonly chapter: number
  readonly cellIndex?: number
  readonly latitudeDegrees?: number
  readonly longitudeDegrees?: number
  readonly species?: string
  readonly magnitude: number
  readonly narration?: string
}

export interface EventPage {
  readonly events: readonly SpectatorEvent[]
  readonly cursor: number
  readonly latestSequence: number
}

const MAX_CELLS = 1 << 16
const MAX_EVENTS = 500

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function num(value: unknown, name: string): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    throw new Error(`Invalid number: ${name}`)
  }
  return value
}

function str(value: unknown, name: string, maxLength = 200): string {
  if (typeof value !== 'string' || value.length > maxLength) {
    throw new Error(`Invalid string: ${name}`)
  }
  return value
}

function intArray(value: unknown, name: string, expectedLength: number): number[] {
  if (!Array.isArray(value) || value.length !== expectedLength) {
    throw new Error(`Invalid array: ${name}`)
  }
  return value.map((entry, index) => num(entry, `${name}[${index}]`))
}

export function parseWorldList(value: unknown): WorldList {
  if (!isRecord(value) || !Array.isArray(value.worlds)) {
    throw new Error('Invalid world list.')
  }
  return {
    worldmindMode: str(value.worldmindMode, 'worldmindMode', 60),
    worlds: value.worlds.slice(0, 100).map((entry): WorldListItem => {
      if (!isRecord(entry)) {
        throw new Error('Invalid world list entry.')
      }
      return {
        worldId: str(entry.worldId, 'worldId', 40),
        name: str(entry.name, 'name', 60),
        tick: num(entry.tick, 'tick'),
        chapter: num(entry.chapter, 'chapter'),
        lastAdvancedAtUtc: str(entry.lastAdvancedAtUtc, 'lastAdvancedAtUtc', 40),
      }
    }),
  }
}

export function parseWorldSummary(value: unknown): WorldSummary {
  if (!isRecord(value)) {
    throw new Error('Invalid world summary.')
  }
  return {
    worldId: str(value.worldId, 'worldId', 40),
    name: str(value.name, 'name', 60),
    tick: num(value.tick, 'tick'),
    day: num(value.day, 'day'),
    season: num(value.season, 'season'),
    chapter: num(value.chapter, 'chapter'),
    seasonPhase: str(value.seasonPhase, 'seasonPhase', 30),
    healthPermille: num(value.healthPermille, 'healthPermille'),
    isPaused: value.isPaused === true,
    lastUpdatedUtc: str(value.lastUpdatedUtc, 'lastUpdatedUtc', 40),
    gridWidth: num(value.gridWidth, 'gridWidth'),
    gridHeight: num(value.gridHeight, 'gridHeight'),
    simulationVersion: str(value.simulationVersion, 'simulationVersion', 40),
    contentVersion: str(value.contentVersion, 'contentVersion', 40),
    worldmindMode: str(value.worldmindMode, 'worldmindMode', 60),
  }
}

export function parseSnapshot(value: unknown): SpectatorSnapshot {
  if (!isRecord(value) || value.schema !== SNAPSHOT_SCHEMA || !isRecord(value.cells)) {
    throw new Error('Unsupported snapshot schema.')
  }

  const gridWidth = num(value.gridWidth, 'gridWidth')
  const gridHeight = num(value.gridHeight, 'gridHeight')
  const count = gridWidth * gridHeight
  if (gridWidth <= 0 || gridHeight <= 0 || count > MAX_CELLS) {
    throw new Error('Snapshot grid is out of range.')
  }

  const cells = value.cells
  const species = Array.isArray(value.species) ? value.species.slice(0, 50) : []

  return {
    schema: SNAPSHOT_SCHEMA,
    worldId: str(value.worldId, 'worldId', 40),
    tick: num(value.tick, 'tick'),
    version: num(value.version, 'version'),
    day: num(value.day, 'day'),
    season: num(value.season, 'season'),
    chapter: num(value.chapter, 'chapter'),
    healthPermille: num(value.healthPermille, 'healthPermille'),
    gridWidth,
    gridHeight,
    lastEventSequence: num(value.lastEventSequence, 'lastEventSequence'),
    species: species.map((entry): SpeciesTotal => {
      if (!isRecord(entry)) {
        throw new Error('Invalid species total.')
      }
      return {
        species: str(entry.species, 'species', 40),
        displayName: str(entry.displayName, 'displayName', 60),
        archetype: str(entry.archetype, 'archetype', 20),
        population: num(entry.population, 'population'),
      }
    }),
    cells: {
      biome: intArray(cells.biome, 'biome', count),
      vitalityPermille: intArray(cells.vitalityPermille, 'vitalityPermille', count),
      dominantStress: intArray(cells.dominantStress, 'dominantStress', count),
      stressPermille: intArray(cells.stressPermille, 'stressPermille', count),
      temperatureDeciC: intArray(cells.temperatureDeciC, 'temperatureDeciC', count),
      moisturePermille: intArray(cells.moisturePermille, 'moisturePermille', count),
      populationPressurePermille: intArray(
        cells.populationPressurePermille,
        'populationPressurePermille',
        count,
      ),
    },
  }
}

export function parseEventPage(value: unknown): EventPage {
  if (!isRecord(value) || !Array.isArray(value.events)) {
    throw new Error('Invalid event page.')
  }

  return {
    cursor: num(value.cursor, 'cursor'),
    latestSequence: num(value.latestSequence, 'latestSequence'),
    events: value.events.slice(0, MAX_EVENTS).map((entry): SpectatorEvent => {
      if (!isRecord(entry)) {
        throw new Error('Invalid event.')
      }
      const event: { -readonly [K in keyof SpectatorEvent]: SpectatorEvent[K] } = {
        sequence: num(entry.sequence, 'sequence'),
        tick: num(entry.tick, 'tick'),
        type: str(entry.type, 'type', 40),
        chapter: num(entry.chapter, 'chapter'),
        magnitude: num(entry.magnitude, 'magnitude'),
      }
      if (typeof entry.cellIndex === 'number') {
        event.cellIndex = entry.cellIndex
      }
      if (typeof entry.latitudeDegrees === 'number') {
        event.latitudeDegrees = entry.latitudeDegrees
      }
      if (typeof entry.longitudeDegrees === 'number') {
        event.longitudeDegrees = entry.longitudeDegrees
      }
      if (typeof entry.species === 'string') {
        event.species = str(entry.species, 'species', 40)
      }
      if (typeof entry.narration === 'string') {
        event.narration = str(entry.narration, 'narration', 400)
      }
      return event
    }),
  }
}
