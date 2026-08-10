import { describe, expect, it } from 'vitest'
import {
  applyVitality,
  biomeColor,
  BIOME_NAMES,
  cellAppearance,
  legendFor,
  overlayExplanation,
  stressPattern,
  type CellAppearanceInput,
  type OverlayMode,
} from './palette'

const baseCell: CellAppearanceInput = {
  biome: 4,
  vitalityPermille: 800,
  dominantStress: 1,
  stressPermille: 600,
  temperatureDeciC: 150,
  moisturePermille: 500,
  populationPressurePermille: 400,
  changePermille: 200,
}

const overlays: readonly OverlayMode[] = [
  'condition',
  'biome',
  'vitality',
  'climate',
  'population',
  'recentChange',
]

describe('palette mapping', () => {
  it('gives every biome a distinct colour in both palettes', () => {
    for (const colorBlind of [false, true]) {
      const seen = new Set<string>()
      for (let biome = 0; biome < BIOME_NAMES.length; biome++) {
        const color = biomeColor(biome, colorBlind)
        seen.add(`${color.r}-${color.g}-${color.b}`)
      }
      expect(seen.size).toBe(BIOME_NAMES.length)
    }
  })

  it('falls back to a neutral colour for an unknown biome', () => {
    expect(biomeColor(999, false)).toEqual({ r: 120, g: 120, b: 120 })
  })

  it('desaturates toward grey as vitality falls', () => {
    const color = biomeColor(6, false)
    const healthy = applyVitality(color, 1000)
    const dying = applyVitality(color, 0)
    const spread = (c: { r: number; g: number; b: number }): number =>
      Math.max(c.r, c.g, c.b) - Math.min(c.r, c.g, c.b)
    expect(spread(dying)).toBeLessThan(spread(healthy))
  })

  it('encodes stress redundantly as a pattern, never colour alone', () => {
    expect(stressPattern(0, 900)).toBe(0)
    expect(stressPattern(2, 50)).toBe(0)
    expect(stressPattern(2, 500)).toBe(2)
    expect(stressPattern(99, 500)).toBe(4)
  })

  it('keeps the stress pattern visible in the default condition view', () => {
    expect(cellAppearance(baseCell, 'condition', false).pattern).toBe(1)
    expect(cellAppearance({ ...baseCell, dominantStress: 0 }, 'condition', false).pattern).toBe(0)
  })

  it('produces in-range colour channels for every overlay and extreme value', () => {
    const extremes: CellAppearanceInput[] = [
      baseCell,
      { ...baseCell, vitalityPermille: -5000, temperatureDeciC: -9000, changePermille: -1 },
      { ...baseCell, vitalityPermille: 99999, temperatureDeciC: 9000, changePermille: 99999 },
    ]
    for (const overlay of overlays) {
      for (const cell of extremes) {
        const appearance = cellAppearance(cell, overlay, false)
        for (const channel of [appearance.color.r, appearance.color.g, appearance.color.b]) {
          expect(channel).toBeGreaterThanOrEqual(0)
          expect(channel).toBeLessThanOrEqual(255)
        }
        expect(appearance.pattern).toBeGreaterThanOrEqual(0)
        expect(appearance.pattern).toBeLessThanOrEqual(4)
      }
    }
  })

  it('provides a legend and an explanation for every overlay', () => {
    for (const overlay of overlays) {
      expect(legendFor(overlay, false).length).toBeGreaterThan(0)
      expect(overlayExplanation(overlay).length).toBeGreaterThan(10)
    }
  })

  it('keeps the condition legend consistent with the rendered colours', () => {
    const legend = legendFor('condition', true)
    const grassland = legend.find((entry) => entry.label === 'Grassland')
    expect(grassland?.color).toEqual(biomeColor(4, true))
    expect(legend.some((entry) => entry.pattern > 0)).toBe(true)
  })
})
