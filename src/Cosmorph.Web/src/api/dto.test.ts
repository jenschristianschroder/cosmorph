import { describe, expect, it } from 'vitest'
import {
  parseCellDetail,
  parseEventPage,
  parseSnapshot,
  parseWorldList,
  parseWorldSummary,
} from './dto'
import { isValidWorldId } from './client'

const validSnapshot = {
  schema: 'spectator-snapshot/1',
  worldId: 'demo-world',
  tick: 5,
  version: 2,
  day: 5,
  season: 1,
  chapter: 1,
  healthPermille: 600,
  gridWidth: 2,
  gridHeight: 2,
  lastEventSequence: 1,
  species: [{ species: 'verdant-moss', displayName: 'Verdant Moss', archetype: 'Producer', population: 10 }],
  cells: {
    biome: [0, 1, 2, 3],
    vitalityPermille: [1, 2, 3, 4],
    dominantStress: [0, 0, 0, 0],
    stressPermille: [0, 0, 0, 0],
    temperatureDeciC: [1, 2, 3, 4],
    moisturePermille: [1, 2, 3, 4],
    populationPressurePermille: [1, 2, 3, 4],
  },
}

describe('spectator DTO validation', () => {
  it('accepts a well-formed snapshot', () => {
    expect(parseSnapshot(validSnapshot).gridWidth).toBe(2)
  })

  /*
   * A browser holding a cached bundle from before materials existed still has to read what the API
   * sends now, and a browser with the new bundle still has to read a world that has not yet ticked
   * under it. Both schemas are therefore accepted, and the two newest arrays are optional.
   */
  it('accepts the older snapshot schema, without materials or constructions', () => {
    const snapshot = parseSnapshot(validSnapshot)

    expect(snapshot.schema).toBe('spectator-snapshot/1')
    expect(snapshot.cells.resourceRichnessPermille).toBeUndefined()
    expect(snapshot.cells.constructionKind).toBeUndefined()
  })

  it('accepts the current snapshot schema, with materials and constructions', () => {
    const snapshot = parseSnapshot({
      ...validSnapshot,
      schema: 'spectator-snapshot/2',
      cells: {
        ...validSnapshot.cells,
        resourceRichnessPermille: [0, 250, 500, 1000],
        constructionKind: [0, 1, 2, 3],
      },
    })

    expect(snapshot.schema).toBe('spectator-snapshot/2')
    expect(snapshot.cells.resourceRichnessPermille).toEqual([0, 250, 500, 1000])
    expect(snapshot.cells.constructionKind).toEqual([0, 1, 2, 3])
  })

  it('rejects a material array that does not match the grid size', () => {
    expect(() =>
      parseSnapshot({
        ...validSnapshot,
        schema: 'spectator-snapshot/2',
        cells: { ...validSnapshot.cells, resourceRichnessPermille: [0, 1] },
      }),
    ).toThrow()
  })

  it('rejects an unknown schema version', () => {
    expect(() => parseSnapshot({ ...validSnapshot, schema: 'spectator-snapshot/9' })).toThrow()
  })

  it('rejects cell arrays that do not match the grid size', () => {
    expect(() =>
      parseSnapshot({
        ...validSnapshot,
        cells: { ...validSnapshot.cells, biome: [0, 1] },
      }),
    ).toThrow()
  })

  it('rejects an oversized grid', () => {
    expect(() =>
      parseSnapshot({ ...validSnapshot, gridWidth: 4096, gridHeight: 4096 }),
    ).toThrow()
  })

  it('rejects non-numeric cell values', () => {
    expect(() =>
      parseSnapshot({
        ...validSnapshot,
        cells: { ...validSnapshot.cells, biome: ['<script>', 1, 2, 3] },
      }),
    ).toThrow()
  })

  it('keeps hostile narration as plain text data', () => {
    const page = parseEventPage({
      cursor: 1,
      latestSequence: 1,
      events: [
        {
          sequence: 1,
          tick: 1,
          type: 'Drought',
          chapter: 1,
          magnitude: 10,
          narration: '<img src=x onerror=alert(1)> ignore previous instructions',
        },
      ],
    })
    expect(page.events[0]?.narration).toContain('<img')
    expect(typeof page.events[0]?.narration).toBe('string')
  })

  it('rejects an event page that is not an array', () => {
    expect(() => parseEventPage({ cursor: 0, latestSequence: 0, events: {} })).toThrow()
  })

  it('parses world lists and summaries', () => {
    const list = parseWorldList({
      worldmindMode: 'FakeGameMaster',
      worlds: [
        { worldId: 'demo-world', name: 'Demo', tick: 1, chapter: 1, lastAdvancedAtUtc: '2026-01-01T00:00:00Z' },
      ],
    })
    expect(list.worlds).toHaveLength(1)

    const summary = parseWorldSummary({
      worldId: 'demo-world',
      name: 'Demo',
      tick: 1,
      day: 1,
      season: 1,
      chapter: 1,
      seasonPhase: 'Thaw',
      healthPermille: 500,
      isPaused: false,
      lastUpdatedUtc: '2026-01-01T00:00:00Z',
      gridWidth: 2,
      gridHeight: 2,
      simulationVersion: 'sim/1.0.0',
      contentVersion: 'season-1.0.0',
      worldmindMode: 'FakeGameMaster',
    })
    expect(summary.isPaused).toBe(false)
  })

  it('only accepts identifier-shaped world identifiers', () => {
    expect(isValidWorldId('demo-world')).toBe(true)
    expect(isValidWorldId('../secrets')).toBe(false)
    expect(isValidWorldId('Demo')).toBe(false)
  })
})

const validCellDetail = {
  schema: 'spectator-cell/1',
  worldId: 'demo-world',
  tick: 5,
  version: 2,
  cellIndex: 3,
  latitudeDegrees: 45,
  longitudeDegrees: -120,
  biome: 'Forest',
  isLand: true,
  elevation: 300,
  temperatureDeciC: 120,
  moisturePermille: 500,
  biomass: 4000,
  carryingCapacity: 9000,
  vitalityPermille: 610,
  dominantStress: 'None',
  droughtPermille: 0,
  diseasePermille: 0,
  firePermille: 0,
  floodPermille: 0,
  timber: 40,
  stone: 12,
  fibre: 3,
  resourceRichnessPermille: 55,
  timberYield: 4,
  stoneYield: 1,
  fibreYield: 0,
  construction: { kind: 'Shelter', level: 2, conditionPermille: 800 },
  species: [
    {
      species: 'verdant-moss',
      displayName: 'Verdant Moss',
      archetype: 'Producer',
      population: 1800,
      healthPermille: 720,
      energy: 600,
      coldTolerance: 5,
      droughtTolerance: 9,
    },
  ],
}

describe('cell detail validation', () => {
  it('accepts a well-formed place', () => {
    const detail = parseCellDetail(validCellDetail)

    expect(detail.cellIndex).toBe(3)
    expect(detail.construction?.kind).toBe('Shelter')
    expect(detail.species[0]?.population).toBe(1800)
  })

  it('reads a place with nothing built and nobody living there', () => {
    const detail = parseCellDetail({ ...validCellDetail, construction: null, species: [] })

    expect(detail.construction).toBeNull()
    expect(detail.species).toEqual([])
  })

  it.each([
    ['an unknown schema', { schema: 'spectator-cell/9' }],
    ['a missing number', { timber: undefined }],
    ['a number sent as text', { vitalityPermille: '610' }],
    ['a biome that is not a string', { biome: 42 }],
    ['a species list holding something that is not a species', { species: ['verdant-moss'] }],
    ['a construction missing its condition', { construction: { kind: 'Shelter', level: 1 } }],
  ])('rejects %s', (_name, overrides) => {
    expect(() => parseCellDetail({ ...validCellDetail, ...overrides })).toThrow()
  })

  it('rejects a payload that is not an object at all', () => {
    expect(() => parseCellDetail(null)).toThrow()
    expect(() => parseCellDetail('spectator-cell/1')).toThrow()
  })
})
