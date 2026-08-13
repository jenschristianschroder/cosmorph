/**
 * Pure data-to-appearance mapping. The authoritative encoding stays numeric; palettes are a
 * presentation concern and are tested as pure functions.
 */

export const BIOME_NAMES = [
  'Ocean',
  'Ice',
  'Tundra',
  'Boreal forest',
  'Grassland',
  'Temperate forest',
  'Rainforest',
  'Desert',
  'Wetland',
  'Mountain',
] as const

export const STRESS_NAMES = ['None', 'Drought', 'Disease', 'Fire', 'Flood'] as const

export const CONSTRUCTION_NAMES = ['None', 'Shelter', 'Terrace', 'Windbreak'] as const

export type OverlayMode =
  | 'condition'
  | 'biome'
  | 'vitality'
  | 'climate'
  | 'population'
  | 'recentChange'
  | 'resources'
  | 'constructions'

export interface Rgb {
  readonly r: number
  readonly g: number
  readonly b: number
}

/** Cartoon storybook-atlas palette, indexed by the wire value of the biome. */
const BIOME_PALETTE: readonly Rgb[] = [
  { r: 58, g: 106, b: 168 }, // Ocean
  { r: 226, g: 238, b: 245 }, // Ice
  { r: 158, g: 176, b: 158 }, // Tundra
  { r: 52, g: 106, b: 84 }, // Boreal forest
  { r: 150, g: 188, b: 96 }, // Grassland
  { r: 84, g: 148, b: 78 }, // Temperate forest
  { r: 44, g: 128, b: 70 }, // Rainforest
  { r: 219, g: 190, b: 122 }, // Desert
  { r: 96, g: 156, b: 140 }, // Wetland
  { r: 148, g: 140, b: 132 }, // Mountain
]

/** Colour-blind-safe alternative with distinct luminance steps. */
const BIOME_PALETTE_ACCESSIBLE: readonly Rgb[] = [
  { r: 35, g: 76, b: 140 },
  { r: 245, g: 245, b: 245 },
  { r: 176, g: 176, b: 176 },
  { r: 64, g: 92, b: 130 },
  { r: 200, g: 190, b: 90 },
  { r: 110, g: 130, b: 90 },
  { r: 60, g: 100, b: 60 },
  { r: 235, g: 170, b: 90 },
  { r: 100, g: 160, b: 190 },
  { r: 120, g: 110, b: 105 },
]

const FALLBACK: Rgb = { r: 120, g: 120, b: 120 }

export function biomeColor(biome: number, colorBlindMode: boolean): Rgb {
  const palette = colorBlindMode ? BIOME_PALETTE_ACCESSIBLE : BIOME_PALETTE
  return palette[biome] ?? FALLBACK
}

/**
 * The palette index for a biome named rather than numbered. Cell reads carry the enum name
 * (`BorealForest`) while the legend carries a readable one (`Boreal forest`), so both are stripped
 * to letters before matching. A name this bundle has never heard of — which is what a newer API
 * would send to an older browser — returns −1, and {@link biomeColor} answers that with grey.
 */
export function biomeIndex(name: string): number {
  const key = lettersOnly(name)
  return BIOME_NAMES.findIndex((entry) => lettersOnly(entry) === key)
}

function lettersOnly(name: string): string {
  return name.replaceAll(/[^a-z]/gi, '').toLowerCase()
}

export function clampPermille(value: number): number {
  if (!Number.isFinite(value)) {
    return 0
  }
  return Math.min(1000, Math.max(0, Math.trunc(value)))
}

/** Desaturates a colour toward grey as vitality falls, so low vitality reads as a faded world. */
export function applyVitality(color: Rgb, vitalityPermille: number): Rgb {
  const vitality = clampPermille(vitalityPermille) / 1000
  const saturation = 0.35 + 0.65 * vitality
  const grey = 0.299 * color.r + 0.587 * color.g + 0.114 * color.b
  return {
    r: Math.round(grey + (color.r - grey) * saturation),
    g: Math.round(grey + (color.g - grey) * saturation),
    b: Math.round(grey + (color.b - grey) * saturation),
  }
}

/**
 * Redundant, non-colour encoding of the dominant stress. The pattern index selects a hatch in the
 * shader, so stress is never communicated by colour alone.
 */
export function stressPattern(dominantStress: number, stressPermille: number): number {
  const strength = clampPermille(stressPermille)
  if (dominantStress <= 0 || strength < 100) {
    return 0
  }
  return Math.min(4, Math.trunc(dominantStress))
}

export function temperatureColor(temperatureDeciC: number): Rgb {
  const clamped = Math.min(450, Math.max(-450, Math.trunc(temperatureDeciC)))
  const t = (clamped + 450) / 900
  return {
    r: Math.round(40 + 200 * t),
    g: Math.round(90 + 60 * Math.sin(Math.PI * t)),
    b: Math.round(230 - 190 * t),
  }
}

export function rampColor(permille: number, cold: Rgb, hot: Rgb): Rgb {
  const t = clampPermille(permille) / 1000
  return {
    r: Math.round(cold.r + (hot.r - cold.r) * t),
    g: Math.round(cold.g + (hot.g - cold.g) * t),
    b: Math.round(cold.b + (hot.b - cold.b) * t),
  }
}

/**
 * Colours for the kinds of construction, indexed by the wire value. Kind zero is never drawn from
 * this palette; a cell with nothing standing keeps its biome colour.
 */
const CONSTRUCTION_PALETTE: readonly Rgb[] = [
  { r: 40, g: 44, b: 52 },
  { r: 226, g: 160, b: 92 }, // Shelter
  { r: 150, g: 200, b: 120 }, // Terrace
  { r: 130, g: 170, b: 230 }, // Windbreak
]

export interface CellAppearanceInput {
  readonly biome: number
  readonly vitalityPermille: number
  readonly dominantStress: number
  readonly stressPermille: number
  readonly temperatureDeciC: number
  readonly moisturePermille: number
  readonly populationPressurePermille: number
  readonly resourceRichnessPermille: number
  readonly constructionKind: number
  readonly changePermille: number
}

export interface CellAppearance {
  readonly color: Rgb
  readonly pattern: number
}

/** Single source of truth for the globe texture, the legend and the explanation panel. */
export function cellAppearance(
  cell: CellAppearanceInput,
  overlay: OverlayMode,
  colorBlindMode: boolean,
): CellAppearance {
  const base = biomeColor(cell.biome, colorBlindMode)
  switch (overlay) {
    case 'biome':
      return { color: base, pattern: 0 }
    case 'vitality':
      return {
        color: rampColor(cell.vitalityPermille, { r: 120, g: 60, b: 60 }, { r: 90, g: 220, b: 120 }),
        pattern: 0,
      }
    case 'climate':
      return { color: temperatureColor(cell.temperatureDeciC), pattern: 0 }
    case 'population':
      return {
        color: rampColor(
          cell.populationPressurePermille,
          { r: 30, g: 40, b: 60 },
          { r: 250, g: 220, b: 80 },
        ),
        pattern: 0,
      }
    case 'resources':
      return {
        color: rampColor(
          cell.resourceRichnessPermille,
          { r: 38, g: 34, b: 30 },
          { r: 214, g: 176, b: 96 },
        ),
        pattern: 0,
      }
    case 'constructions': {
      const kind = Math.trunc(cell.constructionKind)
      if (kind <= 0) {
        // Nothing built here, so the cell keeps a muted version of its own terrain.
        return { color: applyVitality(base, 200), pattern: 0 }
      }
      return { color: CONSTRUCTION_PALETTE[kind] ?? FALLBACK, pattern: 0 }
    }
    case 'recentChange':
      return {
        color: rampColor(cell.changePermille, { r: 40, g: 44, b: 52 }, { r: 240, g: 120, b: 220 }),
        pattern: stressPattern(cell.dominantStress, cell.stressPermille),
      }
    case 'condition':
    default:
      return {
        color: applyVitality(base, cell.vitalityPermille),
        pattern: stressPattern(cell.dominantStress, cell.stressPermille),
      }
  }
}

export interface LegendEntry {
  readonly label: string
  readonly color: Rgb
  readonly pattern: number
  readonly description: string
}

export function legendFor(overlay: OverlayMode, colorBlindMode: boolean): readonly LegendEntry[] {
  if (overlay === 'biome' || overlay === 'condition') {
    const biomes = BIOME_NAMES.map((label, index) => ({
      label,
      color: biomeColor(index, colorBlindMode),
      pattern: 0,
      description: `Cells classified as ${label.toLowerCase()}.`,
    }))
    if (overlay === 'biome') {
      return biomes
    }
    return [
      ...biomes,
      ...STRESS_NAMES.slice(1).map((label, index) => ({
        label: `${label} stress`,
        color: { r: 230, g: 230, b: 230 },
        pattern: index + 1,
        description: `Hatch pattern marking cells whose dominant stress is ${label.toLowerCase()}.`,
      })),
    ]
  }

  if (overlay === 'constructions') {
    return [
      {
        label: 'Nothing built',
        color: applyVitality(biomeColor(4, colorBlindMode), 200),
        pattern: 0,
        description: 'Cells with no standing construction keep a muted terrain colour.',
      },
      ...CONSTRUCTION_NAMES.slice(1).map((label, index) => ({
        label,
        color: CONSTRUCTION_PALETTE[index + 1] ?? FALLBACK,
        pattern: 0,
        description: constructionDescription(label),
      })),
    ]
  }

  const ramp = (label: string, description: string, cold: Rgb, hot: Rgb): LegendEntry[] =>
    [0, 500, 1000].map((permille) => ({
      label: `${label} ${permille / 10}%`,
      color: rampColor(permille, cold, hot),
      pattern: 0,
      description,
    }))

  switch (overlay) {
    case 'vitality':
      return ramp(
        'Vitality',
        'Green cells sustain life; dark red cells are failing.',
        { r: 120, g: 60, b: 60 },
        { r: 90, g: 220, b: 120 },
      )
    case 'population':
      return ramp(
        'Pressure',
        'Yellow cells are near their carrying capacity.',
        { r: 30, g: 40, b: 60 },
        { r: 250, g: 220, b: 80 },
      )
    case 'resources':
      return ramp(
        'Materials',
        'Bright cells hold the most timber, stone and fibre.',
        { r: 38, g: 34, b: 30 },
        { r: 214, g: 176, b: 96 },
      )
    case 'recentChange':
      return ramp(
        'Change',
        'Bright cells changed most in the last visible ticks.',
        { r: 40, g: 44, b: 52 },
        { r: 240, g: 120, b: 220 },
      )
    case 'climate':
    default:
      return [-400, 0, 400].map((deciC) => ({
        label: `${deciC / 10} °C`,
        color: temperatureColor(deciC),
        pattern: 0,
        description: 'Blue is cold, red is hot.',
      }))
  }
}

/** What each kind of construction does, kept next to the palette so the legend explains itself. */
export function constructionDescription(kind: string): string {
  switch (kind) {
    case 'Shelter':
      return 'Shelters absorb part of the strain the climate puts on the species living there.'
    case 'Terrace':
      return 'Terraces raise how much life the cell can carry.'
    case 'Windbreak':
      return 'Windbreaks damp how quickly drought and flood build up.'
    default:
      return 'Nothing is standing on this cell.'
  }
}

export function overlayExplanation(overlay: OverlayMode): string {
  switch (overlay) {
    case 'biome':
      return 'Base hue shows the biome of each cell. No other state is encoded.'
    case 'vitality':
      return 'Colour shows ecological vitality only, from failing to thriving.'
    case 'climate':
      return 'Colour shows the current cell temperature derived from season and latitude.'
    case 'population':
      return 'Colour shows how close a cell is to its carrying capacity.'
    case 'recentChange':
      return 'Colour shows how much a cell changed recently; hatching still marks dominant stress.'
    case 'resources':
      return 'Colour shows the combined stock of timber, stone and fibre held by each cell.'
    case 'constructions':
      return 'Colour marks what a Warden has built on each cell. Muted cells hold nothing.'
    case 'condition':
    default:
      return 'Hue shows biome, saturation shows vitality, and hatching redundantly marks the dominant stress.'
  }
}
