import type { SpectatorSnapshot } from '../api/dto'
import { cellAppearance, type OverlayMode } from './palette'

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
        changePermille: Math.min(1000, Math.abs(vitality - previousVitality) * 4),
      },
      overlay,
      colorBlindMode,
    )

    const offset = i * 4
    data[offset] = appearance.color.r
    data[offset + 1] = appearance.color.g
    data[offset + 2] = appearance.color.b
    data[offset + 3] = appearance.pattern * 60
  }

  return data
}

/** Converts a cell index into the latitude and longitude used to aim the camera. */
export function cellToLatLon(
  cellIndex: number,
  gridWidth: number,
  gridHeight: number,
): { latitude: number; longitude: number } {
  const clamped = Math.min(Math.max(0, Math.trunc(cellIndex)), gridWidth * gridHeight - 1)
  const row = Math.floor(clamped / gridWidth)
  const column = clamped % gridWidth
  const latitude = 90 - ((row + 0.5) * 180) / gridHeight
  const longitude = ((column + 0.5) * 360) / gridWidth - 180
  return { latitude, longitude }
}
