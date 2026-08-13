import { describe, expect, it } from 'vitest'
import type { CellDetail, Neighbourhood } from '../api/dto'
import { cellLabels, compact, leadingSpecies, spaced } from './labels'

function place(cellIndex: number, overrides: Partial<CellDetail> = {}): CellDetail {
  return {
    schema: 'spectator-cell/1',
    worldId: 'demo-world',
    tick: 5,
    version: 2,
    cellIndex,
    latitudeDegrees: 45,
    longitudeDegrees: -120,
    biome: 'BorealForest',
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
    construction: null,
    species: [],
    ...overrides,
  }
}

function moss(population: number) {
  return {
    species: 'verdant-moss',
    displayName: 'Verdant Moss',
    archetype: 'Producer',
    population,
    healthPermille: 720,
    energy: 600,
    coldTolerance: 5,
    droughtTolerance: 9,
  }
}

function grazer(population: number) {
  return { ...moss(population), species: 'plains-grazer', displayName: 'Plains Grazer', archetype: 'Consumer' }
}

function block(cells: readonly CellDetail[], centerCellIndex: number): Neighbourhood {
  return {
    schema: 'spectator-neighbourhood/1',
    worldId: 'demo-world',
    tick: 5,
    version: 2,
    centerCellIndex,
    radius: 1,
    rows: 1,
    columns: cells.length,
    gridWidth: 64,
    gridHeight: 32,
    cells,
  }
}

describe('wording a place', () => {
  it('splits the names the API sends without shouting them', () => {
    expect(spaced('BorealForest')).toBe('Boreal forest')
    expect(spaced('None')).toBe('None')
    expect(spaced('Drought')).toBe('Drought')
  })

  it('shortens a population only once it would crowd the label', () => {
    expect(compact(0)).toBe('0')
    expect(compact(1800)).toBe((1800).toLocaleString())
    expect(compact(9999)).toBe((9999).toLocaleString())
    expect(compact(12_400)).toBe('12k')
    expect(compact(1_500_000)).toBe(`${(1500).toLocaleString()}k`)
  })

  it('says nothing rather than NaN when a number is missing', () => {
    expect(compact(Number.NaN)).toBe('0')
    expect(compact(Number.POSITIVE_INFINITY)).toBe('0')
  })

  it('names the most populous species, not the first one listed', () => {
    const cell = place(1, { species: [moss(10), grazer(900), moss(500)] })

    expect(leadingSpecies(cell)?.displayName).toBe('Plains Grazer')
    expect(leadingSpecies(place(1))).toBeNull()
  })
})

describe('labelling a block', () => {
  it('has nothing to say about a block that has not arrived', () => {
    expect(cellLabels(null)).toEqual([])
  })

  it('gives the centre its materials and a neighbour only what fits', () => {
    const labels = cellLabels(block([place(0, { species: [moss(1200)] }), place(1, { species: [moss(1200)] })], 1))

    expect(labels).toHaveLength(2)
    expect(labels[0]?.cellIndex).toBe(0)
    expect(labels[0]?.lines).toHaveLength(2)

    // The centre is the place being inspected, so it is the one worth three lines.
    expect(labels[1]?.lines).toHaveLength(3)
    expect(labels[1]?.lines[1]).toBe('t40 s12 f3')
  })

  it('names who lives there, and how many of them out of how many in all', () => {
    const [alone, shared, empty] = cellLabels(
      block(
        [
          place(0, { species: [moss(1200)] }),
          place(1, { species: [moss(1200), grazer(300)] }),
          place(2),
        ],
        9,
      ),
    )

    expect(alone?.lines[0]).toBe(`Verdant Moss ${(1200).toLocaleString()}`)
    expect(shared?.lines[0]).toBe(`Verdant Moss ${(1200).toLocaleString()} of ${(1500).toLocaleString()}`)
    expect(empty?.lines[0]).toBe('lifeless')
  })

  it('says what was built where something was built, and the biome where nothing was', () => {
    const [wild, built] = cellLabels(
      block(
        [place(0), place(1, { construction: { kind: 'Terrace', level: 2, conditionPermille: 800 } })],
        9,
      ),
    )

    expect(wild?.lines[1]).toBe('Boreal forest')
    expect(built?.lines[1]).toBe('Terrace 2')
  })

  it('adds what is happening to a place, and stays quiet when nothing is', () => {
    const [calm, burning] = cellLabels(
      block([place(0), place(1, { dominantStress: 'Fire' })], 9),
    )

    expect(calm?.lines[1]).toBe('Boreal forest')
    expect(burning?.lines[1]).toBe('Boreal forest · Fire')
  })
})
