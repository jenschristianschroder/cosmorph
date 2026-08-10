import { describe, expect, it } from 'vitest'
import { buildTextureData, cellToLatLon } from './texture'
import { parseSnapshot, type SpectatorSnapshot } from '../api/dto'

function snapshot(width: number, height: number, overrides: Partial<SpectatorSnapshot> = {}) {
  const count = width * height
  const fill = (value: number): number[] => Array.from({ length: count }, () => value)
  return parseSnapshot({
    schema: 'spectator-snapshot/1',
    worldId: 'test-world',
    tick: 10,
    version: 3,
    day: 10,
    season: 1,
    chapter: 1,
    healthPermille: 500,
    gridWidth: width,
    gridHeight: height,
    lastEventSequence: 4,
    species: [],
    cells: {
      biome: fill(4),
      vitalityPermille: fill(700),
      dominantStress: fill(0),
      stressPermille: fill(0),
      temperatureDeciC: fill(120),
      moisturePermille: fill(400),
      populationPressurePermille: fill(300),
    },
    ...overrides,
  })
}

describe('texture building', () => {
  it('produces four bytes per cell', () => {
    const data = buildTextureData(snapshot(8, 4), null, 'condition', false)
    expect(data.length).toBe(8 * 4 * 4)
  })

  it('writes the stress pattern into the alpha channel', () => {
    const stressed = snapshot(2, 2)
    const withStress = parseSnapshot({
      ...JSON.parse(JSON.stringify(stressed)),
      cells: {
        ...JSON.parse(JSON.stringify(stressed.cells)),
        dominantStress: [3, 0, 0, 0],
        stressPermille: [800, 0, 0, 0],
      },
    })
    const data = buildTextureData(withStress, null, 'condition', false)
    expect(data[3]).toBe(180)
    expect(data[7]).toBe(0)
  })

  it('ignores a previous snapshot of a different size', () => {
    const current = snapshot(4, 2)
    const previous = snapshot(8, 4)
    expect(() => buildTextureData(current, previous, 'recentChange', false)).not.toThrow()
  })

  it('maps cell indices to latitude and longitude inside the sphere range', () => {
    const first = cellToLatLon(0, 64, 32)
    expect(first.latitude).toBeLessThanOrEqual(90)
    expect(first.latitude).toBeGreaterThan(80)
    expect(first.longitude).toBeGreaterThanOrEqual(-180)

    const last = cellToLatLon(64 * 32 - 1, 64, 32)
    expect(last.latitude).toBeLessThan(-80)
    expect(last.longitude).toBeLessThanOrEqual(180)
  })

  it('clamps an out-of-range cell index', () => {
    expect(cellToLatLon(999999, 8, 4)).toEqual(cellToLatLon(31, 8, 4))
    expect(cellToLatLon(-5, 8, 4)).toEqual(cellToLatLon(0, 8, 4))
  })
})
