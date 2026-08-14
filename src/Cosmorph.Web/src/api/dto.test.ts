import { describe, expect, it } from 'vitest'
import {
  parseCellDetail,
  parseEventPage,
  parseNeighbourhood,
  parseSnapshot,
  parseWorldLife,
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

const validNeighbourhood = {
  schema: 'spectator-neighbourhood/1',
  worldId: 'demo-world',
  tick: 5,
  version: 2,
  centerCellIndex: 4,
  radius: 1,
  rows: 3,
  columns: 3,
  gridWidth: 64,
  gridHeight: 32,
  cells: Array.from({ length: 9 }, (_, index) => ({ ...validCellDetail, cellIndex: index })),
}

describe('neighbourhood validation', () => {
  it('accepts a well-formed block and keeps it in the order it arrived', () => {
    const block = parseNeighbourhood(validNeighbourhood)

    expect(block.rows).toBe(3)
    expect(block.columns).toBe(3)
    expect(block.cells).toHaveLength(9)
    expect(block.cells.map((cell) => cell.cellIndex)).toEqual([0, 1, 2, 3, 4, 5, 6, 7, 8])
    expect(block.cells[4]?.cellIndex).toBe(block.centerCellIndex)
  })

  it('reads the centre alone, which is what radius 0 gives', () => {
    const block = parseNeighbourhood({
      ...validNeighbourhood,
      radius: 0,
      rows: 1,
      columns: 1,
      cells: [validCellDetail],
    })

    expect(block.cells).toHaveLength(1)
  })

  it('rejects an unknown schema version', () => {
    expect(() =>
      parseNeighbourhood({ ...validNeighbourhood, schema: 'spectator-neighbourhood/9' }),
    ).toThrow()
  })

  it('rejects a layout that does not account for its cells', () => {
    expect(() => parseNeighbourhood({ ...validNeighbourhood, rows: 2 })).toThrow()
    expect(() => parseNeighbourhood({ ...validNeighbourhood, rows: 0, columns: 0 })).toThrow()
  })

  /*
   * The API offers at most 25 places. A larger block is refused rather than truncated: the browser
   * cannot lay out what it does not have, and a hostile payload should not be able to flood the DOM
   * one card at a time.
   */
  it('rejects a block larger than the API offers', () => {
    expect(() =>
      parseNeighbourhood({
        ...validNeighbourhood,
        rows: 7,
        columns: 7,
        cells: Array.from({ length: 49 }, () => validCellDetail),
      }),
    ).toThrow()
  })

  it('rejects a block holding something that is not a place', () => {
    const cells = [...validNeighbourhood.cells]
    cells[3] = { ...validCellDetail, biome: 42 } as unknown as typeof validCellDetail

    expect(() => parseNeighbourhood({ ...validNeighbourhood, cells })).toThrow()
    expect(() =>
      parseNeighbourhood({ ...validNeighbourhood, cells: Array.from({ length: 9 }, () => 'a place') }),
    ).toThrow()
  })

  it('rejects a payload carrying no cells at all', () => {
    expect(() => parseNeighbourhood({ ...validNeighbourhood, cells: undefined })).toThrow()
    expect(() => parseNeighbourhood(null)).toThrow()
  })
})

const validLife = {
  schema: 'spectator-life/1',
  worldId: 'demo-world',
  tick: 5,
  version: 2,
  gridWidth: 2,
  gridHeight: 2,
  elevation: [200, 600, 700, 900],
  biomassPermille: [0, 400, 800, 950],
  timber: [0, 100, 200, 300],
  stone: [0, 10, 20, 30],
  fibre: [0, 1, 2, 3],
  seaLevel: 520,
  species: [
    {
      species: 'verdant-moss',
      displayName: 'Verdant Moss',
      archetype: 'Producer',
      population: [0, 3, 7, 11],
    },
  ],
}

describe('the world life read', () => {
  it('accepts a well-formed payload', () => {
    const life = parseWorldLife(validLife)

    expect(life.gridWidth).toBe(2)
    expect(life.seaLevel).toBe(520)
    expect(life.species[0]?.population).toEqual([0, 3, 7, 11])
  })

  it('rejects a schema it does not know how to draw', () => {
    expect(() => parseWorldLife({ ...validLife, schema: 'spectator-life/9' })).toThrow()
    expect(() => parseWorldLife(null)).toThrow()
    expect(() => parseWorldLife({ ...validLife, species: undefined })).toThrow()
  })

  /*
   * A short column would otherwise read as a world where nothing lives past a certain cell, which
   * is worse than refusing the payload: the planet would look calmly, plausibly empty.
   */
  it('rejects a column that does not cover every cell', () => {
    expect(() => parseWorldLife({ ...validLife, elevation: [1, 2, 3] })).toThrow()
    expect(() => parseWorldLife({ ...validLife, timber: [1, 2, 3, 4, 5] })).toThrow()
    expect(() =>
      parseWorldLife({
        ...validLife,
        species: [{ ...validLife.species[0], population: [1, 2] }],
      }),
    ).toThrow()
  })

  it('rejects a grid that is not a grid', () => {
    expect(() => parseWorldLife({ ...validLife, gridWidth: 0 })).toThrow()
    expect(() => parseWorldLife({ ...validLife, gridHeight: -2 })).toThrow()
    expect(() => parseWorldLife({ ...validLife, gridWidth: 5000, gridHeight: 5000 })).toThrow()
  })

  it('rejects more species than the close-up has silhouettes for', () => {
    expect(() =>
      parseWorldLife({
        ...validLife,
        species: Array.from({ length: 17 }, () => validLife.species[0]),
      }),
    ).toThrow()
  })

  it('rejects a column holding something that is not a number', () => {
    expect(() => parseWorldLife({ ...validLife, stone: ['a', 'b', 'c', 'd'] })).toThrow()
    expect(() => parseWorldLife({ ...validLife, seaLevel: 'shallow' })).toThrow()
    expect(() => parseWorldLife({ ...validLife, species: ['verdant-moss'] })).toThrow()
  })
})
