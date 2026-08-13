import type { SpectatorSnapshot } from '../api/dto'
import { cellAppearance, type OverlayMode, type Rgb } from './palette'

/**
 * Builds the RGBA texture bytes for the globe. Red, green and blue carry the mapped colour and
 * alpha carries the redundant stress pattern index, so the shader can hatch without a second
 * texture upload.
 */
export function buildTextureData(
  snapshot: SpectatorSnapshot,
  previous: SpectatorSnapshot | null,
  overlay: OverlayMode,
  colorBlindMode: boolean,
  highlight?: ReadonlySet<number> | undefined,
): Uint8Array {
  const count = snapshot.gridWidth * snapshot.gridHeight
  const data = new Uint8Array(count * 4)
  const cells = snapshot.cells
  const comparable =
    previous !== null &&
    previous.gridWidth === snapshot.gridWidth &&
    previous.gridHeight === snapshot.gridHeight
      ? previous.cells
      : null

  for (let i = 0; i < count; i++) {
    const vitality = cells.vitalityPermille[i] ?? 0
    const previousVitality = comparable?.vitalityPermille[i] ?? vitality
    const appearance = cellAppearance(
      {
        biome: cells.biome[i] ?? 0,
        vitalityPermille: vitality,
        dominantStress: cells.dominantStress[i] ?? 0,
        stressPermille: cells.stressPermille[i] ?? 0,
        temperatureDeciC: cells.temperatureDeciC[i] ?? 0,
        moisturePermille: cells.moisturePermille[i] ?? 0,
        populationPressurePermille: cells.populationPressurePermille[i] ?? 0,
        resourceRichnessPermille: cells.resourceRichnessPermille?.[i] ?? 0,
        constructionKind: cells.constructionKind?.[i] ?? 0,
        changePermille: Math.min(1000, Math.abs(vitality - previousVitality) * 4),
      },
      overlay,
      colorBlindMode,
    )

    // A highlighted cell is tinted toward white so a picked region reads at a glance.
    const color = highlight?.has(i) ? tint(appearance.color) : appearance.color

    const offset = i * 4
    data[offset] = color.r
    data[offset + 1] = color.g
    data[offset + 2] = color.b
    data[offset + 3] = appearance.pattern * 60
  }

  return data
}

function tint(color: Rgb): Rgb {
  return {
    r: Math.round(color.r + (255 - color.r) * 0.5),
    g: Math.round(color.g + (255 - color.g) * 0.5),
    b: Math.round(color.b + (255 - color.b) * 0.5),
  }
}

/** Re-exported so existing callers keep their import; the mapping itself lives in projection.ts. */
export { cellToLatLon } from './projection'
