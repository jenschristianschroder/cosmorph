import { describe, expect, it } from 'vitest'
import type { SpectatorSnapshot, WorldLife } from '../api/dto'
import {
  buildDetailInstances,
  buildDetailTextureData,
  createDetailBuffers,
  detailCapacity,
  fillDetailInstances,
  MAX_CRITTERS_PER_SPECIES,
  MAX_ELEVATION,
  packClimate,
  packStress,
  unpackClimate,
  unpackStress,
  type DetailBuffers,
} from './detail'
import {
  DETAIL_BUILDING,
  DETAIL_CONIFER,
  DETAIL_HERBIVORE,
  DETAIL_PREDATOR,
  DETAIL_PRODUCER,
} from './palette'
import { uvToCell } from './projection'

const WIDTH = 4
const HEIGHT = 3
const COUNT = WIDTH * HEIGHT
const SEA_LEVEL = 520

/** Cell 0 is deep ocean, cell 1 is thriving land and the rest are bare, unremarkable land. */
function life(overrides: Partial<WorldLife> = {}): WorldLife {
  const flat = (value: number, first: number, second: number): number[] =>
    Array.from({ length: COUNT }, (_, i) => (i === 0 ? first : i === 1 ? second : value))

  return {
    schema: 'spectator-life/1',
    worldId: 'demo-world',
    tick: 9,
    version: 4,
    gridWidth: WIDTH,
    gridHeight: HEIGHT,
    elevation: flat(600, 200, 780),
    biomassPermille: flat(120, 0, 940),
    timber: flat(0, 0, 900),
    stone: flat(0, 0, 500),
    fibre: flat(0, 0, 300),
    seaLevel: SEA_LEVEL,
    species: [
      {
        species: 'verdant-moss',
        displayName: 'Verdant Moss',
        archetype: 'Producer',
        population: flat(0, 40, 3),
      },
      {
        species: 'cliff-grazer',
        displayName: 'Cliff Grazer',
        archetype: 'Herbivore',
        population: flat(0, 12, 400),
      },
    ],
    ...overrides,
  }
}

function snapshot(overrides: Partial<SpectatorSnapshot> = {}): SpectatorSnapshot {
  const column = (value: number, first: number, second: number): number[] =>
    Array.from({ length: COUNT }, (_, i) => (i === 0 ? first : i === 1 ? second : value))

  return {
    schema: 'spectator-snapshot/2',
    worldId: 'demo-world',
    tick: 9,
    version: 4,
    day: 9,
    season: 1,
    chapter: 1,
    healthPermille: 600,
    gridWidth: WIDTH,
    gridHeight: HEIGHT,
    lastEventSequence: 3,
    species: [],
    cells: {
      // Cell 0 is Ocean, cell 1 is Grassland, the rest are Temperate forest.
      biome: column(5, 0, 4),
      vitalityPermille: column(500, 400, 900),
      dominantStress: column(0, 0, 3),
      stressPermille: column(0, 0, 800),
      temperatureDeciC: column(100, 40, 210),
      moisturePermille: column(400, 900, 350),
      populationPressurePermille: column(200, 100, 700),
      constructionKind: column(0, 0, 1),
    },
    ...overrides,
  }
}

describe('packing climate into one byte', () => {
  it('round trips the band it names, so unpacking and packing again is the same byte', () => {
    for (let temperature = -450; temperature <= 450; temperature += 7) {
      for (let moisture = 0; moisture <= 1000; moisture += 37) {
        const packed = packClimate(temperature, moisture)
        expect(packed).toBeGreaterThanOrEqual(0)
        expect(packed).toBeLessThanOrEqual(255)

        const unpacked = unpackClimate(packed)
        expect(packClimate(unpacked.temperatureDeciC, unpacked.moisturePermille)).toBe(packed)
      }
    }
  })

  it('keeps cold apart from hot and dry apart from wet', () => {
    expect(unpackClimate(packClimate(-400, 0)).temperatureDeciC).toBeLessThan(-300)
    expect(unpackClimate(packClimate(400, 0)).temperatureDeciC).toBeGreaterThan(300)
    expect(unpackClimate(packClimate(0, 950)).moisturePermille).toBeGreaterThan(850)
    expect(unpackClimate(packClimate(0, 20)).moisturePermille).toBeLessThan(150)
  })

  it('clamps anything outside the range instead of wrapping it into a byte', () => {
    expect(packClimate(9000, 9000)).toBe(packClimate(450, 1000))
    expect(packClimate(-9000, -9000)).toBe(packClimate(-450, 0))
    expect(packClimate(Number.NaN, Number.NaN)).toBe(packClimate(0, 0))
  })
})

describe('packing the cause of stress with how hard it bites', () => {
  it('carries both, and says nothing at all when nothing is wrong', () => {
    expect(packStress(0, 900)).toBe(0)

    for (let kind = 1; kind <= 4; kind++) {
      for (const permille of [0, 250, 500, 1000]) {
        const packed = packStress(kind, permille)
        expect(packed).toBeLessThanOrEqual(255)

        const unpacked = unpackStress(packed)
        expect(unpacked.kind).toBe(kind)
        expect(unpacked.permille).toBeCloseTo(permille, -2)
      }
    }
  })

  it('never lets one cause be read as another, however hard it bites', () => {
    expect(unpackStress(packStress(1, 1000)).kind).toBe(1)
    expect(unpackStress(packStress(2, 0)).kind).toBe(2)
    expect(packStress(9, 1000)).toBe(packStress(4, 1000))
  })
})

describe('the close-up texture', () => {
  it('carries elevation, biomass, climate and stress, one texel per cell', () => {
    const data = buildDetailTextureData(life(), snapshot())

    expect(data.length).toBe(COUNT * 4)

    // Cell 0 is under water and cell 1 is high ground, so the sea level lands between the two.
    const seaLevelByte = Math.round((SEA_LEVEL * 255) / MAX_ELEVATION)
    expect(data[0]).toBeLessThan(seaLevelByte)
    expect(data[4]).toBeGreaterThan(seaLevelByte)

    // Nothing grows in the ocean; cell 1 is nearly at its carrying capacity.
    expect(data[1]).toBe(0)
    expect(data[5]).toBeGreaterThan(230)

    expect(unpackClimate(data[6]!).temperatureDeciC).toBeCloseTo(210, -2)
    expect(unpackStress(data[3]!).kind).toBe(0)
    expect(unpackStress(data[7]!).kind).toBe(3)
    expect(unpackStress(data[7]!).permille).toBeGreaterThan(700)
  })

  it('ignores a snapshot of a different grid rather than sampling it crookedly', () => {
    const mismatched = snapshot({ gridWidth: 8, gridHeight: 8 })
    const data = buildDetailTextureData(life(), mismatched)
    const alone = buildDetailTextureData(life(), null)

    expect(Array.from(data)).toEqual(Array.from(alone))
    // Elevation and biomass come from the life read, so they survive the disagreement.
    expect(data[4]).toBeGreaterThan(0)
  })
})

describe('placing critters and props', () => {
  it('stands nothing at all on the open ocean', () => {
    const buffers = buildDetailInstances(life(), snapshot(), false)

    expect(buffers.count).toBeGreaterThan(0)
    for (let i = 0; i < buffers.count; i++) {
      const { u, v } = pointToUv(buffers, i)
      expect(uvToCell(u, v, WIDTH, HEIGHT)).not.toBe(0)
    }
  })

  it('draws one critter per individual, up to the cap', () => {
    const buffers = buildDetailInstances(life(), snapshot(), false)
    const inCell = countByKind(buffers, 1)

    // Three moss is three cushions, one for each of them; four hundred grazers is still the cap.
    expect(inCell.get(DETAIL_PRODUCER)).toBe(3)
    expect(inCell.get(DETAIL_HERBIVORE)).toBe(MAX_CRITTERS_PER_SPECIES)
  })

  it('stands one prop per material the cell holds, and one for what is built on it', () => {
    const buffers = buildDetailInstances(life(), snapshot(), false)
    const inCell = countByKind(buffers, 1)

    expect(inCell.get(DETAIL_CONIFER)).toBe(1)
    expect(inCell.get(DETAIL_BUILDING)).toBe(1)

    // The bare cells hold no stock worth drawing and have nothing standing on them.
    expect(countByKind(buffers, 2).size).toBe(0)
  })

  it('puts every single thing inside the cell whose numbers put it there', () => {
    const buffers = buildDetailInstances(life(), snapshot(), false)

    expect(buffers.count).toBeGreaterThan(0)
    for (let i = 0; i < buffers.count; i++) {
      const { u, v } = pointToUv(buffers, i)
      expect(uvToCell(u, v, WIDTH, HEIGHT)).toBe(1)
    }
  })

  it('gives the same answer twice, so a poll does not teleport anything', () => {
    const first = buildDetailInstances(life(), snapshot(), false)

    // A tick later the grazers have bred and some timber has been cut. Both cells still hold the
    // same kinds of thing, so every one of them must still be standing exactly where it was.
    const base = life()
    const later = life({
      species: [
        base.species[0]!,
        { ...base.species[1]!, population: base.species[1]!.population.map((n) => n * 2) },
      ],
      timber: base.timber.map((stock) => Math.round(stock * 0.5)),
    })
    const second = buildDetailInstances(later, snapshot(), false)

    expect(second.count).toBe(first.count)
    expect(Array.from(second.centre)).toEqual(Array.from(first.centre))
    expect(Array.from(second.phase)).toEqual(Array.from(first.phase))
    expect(Array.from(second.kind)).toEqual(Array.from(first.kind))
    // The felled timber is a smaller tree in the same place, not a tree somewhere else.
    expect(Array.from(second.scale)).not.toEqual(Array.from(first.scale))
  })

  it('never writes past the capacity it was given', () => {
    const buffers = createDetailBuffers(3)
    fillDetailInstances(buffers, life(), snapshot(), false)

    expect(buffers.count).toBe(3)
    expect(buffers.capacity).toBe(3)
    expect(buffers.centre.length).toBe(9)
  })

  it('draws a species it has never heard of as the shape of its archetype', () => {
    const unknown = life({
      species: [
        {
          species: 'glass-lurker',
          displayName: 'Glass Lurker',
          archetype: 'Predator',
          population: Array.from({ length: COUNT }, (_, i) => (i === 1 ? 2 : 0)),
        },
      ],
    })
    const buffers = buildDetailInstances(unknown, snapshot(), false)

    expect(countByKind(buffers, 1).get(DETAIL_PREDATOR)).toBe(2)
  })

  it('falls back to the coastline the life read carries when there is no snapshot', () => {
    const buffers = buildDetailInstances(life(), null, false)

    // Cell 0 is below sea level, so it is still left empty even with no biome to consult.
    for (let i = 0; i < buffers.count; i++) {
      const { u, v } = pointToUv(buffers, i)
      expect(uvToCell(u, v, WIDTH, HEIGHT)).not.toBe(0)
    }
    expect(buffers.count).toBeGreaterThan(0)
  })

  it('tints the same thing differently for a colour-blind viewer', () => {
    const plain = buildDetailInstances(life(), snapshot(), false)
    const accessible = buildDetailInstances(life(), snapshot(), true)

    expect(accessible.count).toBe(plain.count)
    expect(Array.from(accessible.tint)).not.toEqual(Array.from(plain.tint))
  })

  it('sizes the buffers for the grid, and never beyond what one draw can hold', () => {
    expect(detailCapacity(COUNT)).toBe(COUNT * 16)
    expect(detailCapacity(64 * 32)).toBe(32_768)
    expect(detailCapacity(1_000_000)).toBe(32_768)
    expect(detailCapacity(-5)).toBe(0)
  })
})

/** How many of each kind were placed in one cell. */
function countByKind(buffers: DetailBuffers, cell: number): Map<number, number> {
  const counts = new Map<number, number>()
  for (let i = 0; i < buffers.count; i++) {
    const { u, v } = pointToUv(buffers, i)
    if (uvToCell(u, v, WIDTH, HEIGHT) !== cell) {
      continue
    }
    const kind = buffers.kind[i]!
    counts.set(kind, (counts.get(kind) ?? 0) + 1)
  }
  return counts
}

/**
 * The inverse of the projection, matching `projection.test.ts`. Reading a placed thing back as a uv
 * is the only honest way to ask which cell it is standing on.
 */
function pointToUv(buffers: DetailBuffers, index: number): { u: number; v: number } {
  const x = buffers.centre[index * 3]!
  const y = buffers.centre[index * 3 + 1]!
  const z = buffers.centre[index * 3 + 2]!
  const theta = Math.acos(Math.min(1, Math.max(-1, y)))
  const phi = Math.atan2(z, -x)
  return { u: (((phi / (Math.PI * 2)) % 1) + 1) % 1, v: 1 - theta / Math.PI }
}
