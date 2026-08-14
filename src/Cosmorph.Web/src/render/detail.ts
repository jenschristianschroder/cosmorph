import type { SpectatorSnapshot, WorldLife } from '../api/dto'
import { cellToUv, uvToPoint } from './projection'
import {
  clampPermille,
  critterColor,
  critterKind,
  detailKindColor,
  DETAIL_BOULDER,
  DETAIL_BUILDING,
  DETAIL_CONIFER,
  DETAIL_TUFT,
  type Rgb,
} from './palette'

/**
 * The close-up channels. One RGBA texel per cell carries everything the ground shader draws that the
 * ordinary state texture does not: the shape of the land, how much is growing on it, its climate and
 * what is eating it. Kept pure and away from WebGL so every byte can be tested.
 *
 * Four bytes is the whole budget, so climate and stress each pack two values into one. That is why
 * the packing lives here as a named pair of functions rather than as arithmetic buried in a loop.
 */

/** Elevation runs 0 to 1000 in the domain, so the byte is the same scale at 255 steps. */
export const MAX_ELEVATION = 1000

/** Temperature bands in the packed climate byte, spanning {@link MIN_TEMPERATURE_DECI_C} up. */
export const CLIMATE_TEMPERATURE_STEPS = 21

/** Moisture bands in the packed climate byte. 21 × 12 is 252 values, which fits a byte. */
export const CLIMATE_MOISTURE_STEPS = 12

export const MIN_TEMPERATURE_DECI_C = -450
export const MAX_TEMPERATURE_DECI_C = 450

/** Room for five causes of stress, each with 51 strengths, in one byte. */
export const STRESS_KIND_STRIDE = 51
export const MAX_STRESS_KIND = 4

/**
 * Packs a cell's climate into one byte. Bands rather than exact values: the shader only chooses
 * between frost, dust, sheen and rain, and a band is more than enough resolution to do that.
 */
export function packClimate(temperatureDeciC: number, moisturePermille: number): number {
  const temperature = Number.isFinite(temperatureDeciC) ? Math.trunc(temperatureDeciC) : 0
  const clamped = Math.min(MAX_TEMPERATURE_DECI_C, Math.max(MIN_TEMPERATURE_DECI_C, temperature))
  const span = MAX_TEMPERATURE_DECI_C - MIN_TEMPERATURE_DECI_C
  const temperatureStep = Math.round(
    ((clamped - MIN_TEMPERATURE_DECI_C) / span) * (CLIMATE_TEMPERATURE_STEPS - 1),
  )
  const moistureStep = Math.round(
    (clampPermille(moisturePermille) / 1000) * (CLIMATE_MOISTURE_STEPS - 1),
  )
  return temperatureStep * CLIMATE_MOISTURE_STEPS + moistureStep
}

export interface Climate {
  readonly temperatureDeciC: number
  readonly moisturePermille: number
}

/** The middle of each band the byte names. Round trips: packing the result returns the same byte. */
export function unpackClimate(packed: number): Climate {
  const max = (CLIMATE_TEMPERATURE_STEPS - 1) * CLIMATE_MOISTURE_STEPS + CLIMATE_MOISTURE_STEPS - 1
  const value = Math.min(max, Math.max(0, Number.isFinite(packed) ? Math.trunc(packed) : 0))
  const temperatureStep = Math.floor(value / CLIMATE_MOISTURE_STEPS)
  const moistureStep = value - temperatureStep * CLIMATE_MOISTURE_STEPS
  const span = MAX_TEMPERATURE_DECI_C - MIN_TEMPERATURE_DECI_C

  return {
    temperatureDeciC: Math.round(
      MIN_TEMPERATURE_DECI_C + (temperatureStep * span) / (CLIMATE_TEMPERATURE_STEPS - 1),
    ),
    moisturePermille: Math.round((moistureStep * 1000) / (CLIMATE_MOISTURE_STEPS - 1)),
  }
}

/**
 * Packs the cause of stress and how hard it is biting into one byte. Read from the world state
 * rather than from the hatch byte of the state texture, which is zero on every overlay but Condition
 * and Recent change — the causes of stress must be visible whichever overlay is chosen.
 */
export function packStress(dominantStress: number, stressPermille: number): number {
  const kind = Math.min(
    MAX_STRESS_KIND,
    Math.max(0, Number.isFinite(dominantStress) ? Math.trunc(dominantStress) : 0),
  )
  if (kind === 0) {
    return 0
  }
  return kind * STRESS_KIND_STRIDE + Math.round((clampPermille(stressPermille) / 1000) * 50)
}

export interface Stress {
  readonly kind: number
  readonly permille: number
}

export function unpackStress(packed: number): Stress {
  const max = MAX_STRESS_KIND * STRESS_KIND_STRIDE + 50
  const value = Math.min(max, Math.max(0, Number.isFinite(packed) ? Math.trunc(packed) : 0))
  const kind = Math.floor(value / STRESS_KIND_STRIDE)
  return { kind, permille: Math.round(((value - kind * STRESS_KIND_STRIDE) / 50) * 1000) }
}

/**
 * Builds the close-up texture bytes: elevation, biomass, packed climate and packed stress, one texel
 * per cell in the same row-major order as the state texture. Climate and stress come from the
 * snapshot, which already carries them; a snapshot of a different grid is ignored rather than
 * sampled crookedly, which leaves those two channels neutral until the two reads agree again.
 */
export function buildDetailTextureData(
  life: WorldLife,
  snapshot: SpectatorSnapshot | null,
): Uint8Array {
  const count = life.gridWidth * life.gridHeight
  const data = new Uint8Array(count * 4)
  const cells =
    snapshot !== null &&
    snapshot.gridWidth === life.gridWidth &&
    snapshot.gridHeight === life.gridHeight
      ? snapshot.cells
      : null

  for (let i = 0; i < count; i++) {
    const elevation = Math.min(MAX_ELEVATION, Math.max(0, life.elevation[i] ?? 0))
    const offset = i * 4
    data[offset] = Math.round((elevation * 255) / MAX_ELEVATION)
    data[offset + 1] = Math.round((clampPermille(life.biomassPermille[i] ?? 0) * 255) / 1000)
    data[offset + 2] = packClimate(cells?.temperatureDeciC[i] ?? 0, cells?.moisturePermille[i] ?? 0)
    data[offset + 3] = packStress(cells?.dominantStress[i] ?? 0, cells?.stressPermille[i] ?? 0)
  }

  return data
}

/**
 * One drawable thing standing on the planet. The layer keeps these as flat arrays because they go
 * straight into instanced attributes: one draw call for the whole world's life.
 */
export interface DetailBuffers {
  readonly capacity: number
  /** Three floats per instance: a point on the unit sphere, before any rotation. */
  readonly centre: Float32Array
  readonly kind: Float32Array
  readonly scale: Float32Array
  /** Where in its own wander each thing is, so a field of critters does not move as one. */
  readonly phase: Float32Array
  /** Three floats per instance, each 0 to 1. */
  readonly tint: Float32Array
  count: number
}

/** At most four of any one species per cell, three materials, and whatever is built. */
export const MAX_CRITTERS_PER_SPECIES = 4
const MAX_INSTANCES = 32_768
const PER_CELL_ESTIMATE = 16

/** A stock below this is too thin to stand anything on: the cell is worked out, not wooded. */
const PROP_THRESHOLD = 120

const CRITTER_SCALE = 0.017
const OCEAN = 0
const ICE = 1

export function detailCapacity(cellCount: number): number {
  return Math.max(0, Math.min(MAX_INSTANCES, Math.trunc(cellCount) * PER_CELL_ESTIMATE))
}

export function createDetailBuffers(capacity: number): DetailBuffers {
  const size = Math.max(0, Math.trunc(capacity))
  return {
    capacity: size,
    centre: new Float32Array(size * 3),
    kind: new Float32Array(size),
    scale: new Float32Array(size),
    phase: new Float32Array(size),
    tint: new Float32Array(size * 3),
    count: 0,
  }
}

/**
 * Places every critter and prop on the planet. Deterministic throughout: a thing keeps its spot for
 * as long as its cell holds it, so the next poll five seconds later refreshes the numbers without
 * teleporting anything across its cell. Nothing here touches WebGL or the clock.
 */
export function fillDetailInstances(
  buffers: DetailBuffers,
  life: WorldLife,
  snapshot: SpectatorSnapshot | null,
  colorBlindMode: boolean,
): void {
  const width = life.gridWidth
  const height = life.gridHeight
  const count = width * height
  const cells =
    snapshot !== null && snapshot.gridWidth === width && snapshot.gridHeight === height
      ? snapshot.cells
      : null

  buffers.count = 0
  for (let cell = 0; cell < count; cell++) {
    // Nothing stands on open water or on the ice. Without a snapshot to say which is which, the
    // coastline the life read carries is the next best answer.
    const biome = cells?.biome[cell]
    const land =
      biome === undefined
        ? (life.elevation[cell] ?? 0) >= life.seaLevel
        : biome !== OCEAN && biome !== ICE
    if (!land) {
      continue
    }

    // Slot numbers are what make the placement stable, so they are counted per cell and never reset
    // by anything that varies between polls.
    let slot = 0
    for (const column of life.species) {
      const population = column.population[cell] ?? 0
      const critters = Math.min(MAX_CRITTERS_PER_SPECIES, Math.trunc(population))
      if (critters <= 0) {
        slot += MAX_CRITTERS_PER_SPECIES
        continue
      }

      const kind = critterKind(column.species, column.archetype)
      const color = critterColor(column.species, column.archetype, colorBlindMode)
      for (let i = 0; i < critters; i++) {
        if (!place(buffers, cell, slot + i, width, height, kind, CRITTER_SCALE, color)) {
          return
        }
      }
      slot += MAX_CRITTERS_PER_SPECIES
    }

    const timber = life.timber[cell] ?? 0
    const stone = life.stone[cell] ?? 0
    const fibre = life.fibre[cell] ?? 0
    placeProp(buffers, cell, slot, width, height, DETAIL_CONIFER, timber, 0.03, colorBlindMode)
    placeProp(buffers, cell, slot + 1, width, height, DETAIL_BOULDER, stone, 0.022, colorBlindMode)
    placeProp(buffers, cell, slot + 2, width, height, DETAIL_TUFT, fibre, 0.018, colorBlindMode)

    const built = Math.trunc(cells?.constructionKind?.[cell] ?? 0)
    if (built > 0) {
      place(
        buffers,
        cell,
        slot + 3,
        width,
        height,
        DETAIL_BUILDING,
        0.034,
        detailKindColor(DETAIL_BUILDING, colorBlindMode),
      )
    }
  }
}

/** Convenience for callers that have no buffers yet, and the shape the tests exercise. */
export function buildDetailInstances(
  life: WorldLife,
  snapshot: SpectatorSnapshot | null,
  colorBlindMode: boolean,
): DetailBuffers {
  const buffers = createDetailBuffers(detailCapacity(life.gridWidth * life.gridHeight))
  fillDetailInstances(buffers, life, snapshot, colorBlindMode)
  return buffers
}

function placeProp(
  buffers: DetailBuffers,
  cell: number,
  slot: number,
  width: number,
  height: number,
  kind: number,
  stock: number,
  largest: number,
  colorBlindMode: boolean,
): void {
  const held = clampPermille(stock)
  if (held < PROP_THRESHOLD) {
    return
  }

  // A well-stocked cell grows a full-sized prop; a thin one gets a stunted version of the same
  // thing, so the stock is legible from the size rather than only from its presence.
  const scale = largest * (0.55 + 0.45 * (held / 1000))
  place(buffers, cell, slot, width, height, kind, scale, detailKindColor(kind, colorBlindMode))
}

/** Writes one instance. Returns false once the buffers are full, which is the signal to stop. */
function place(
  buffers: DetailBuffers,
  cell: number,
  slot: number,
  width: number,
  height: number,
  kind: number,
  scale: number,
  color: Rgb,
): boolean {
  const index = buffers.count
  if (index >= buffers.capacity) {
    return false
  }

  // Kept well inside the cell: a critter that wandered over the border would be standing on a place
  // that is not the one whose numbers put it there.
  const uv = cellToUv(cell, width, height)
  const u = uv.u + (hashUnit(cell, slot * 2) - 0.5) * (0.7 / width)
  const v = uv.v + (hashUnit(cell, slot * 2 + 1) - 0.5) * (0.7 / height)
  const point = uvToPoint(u, v)

  buffers.centre[index * 3] = point.x
  buffers.centre[index * 3 + 1] = point.y
  buffers.centre[index * 3 + 2] = point.z
  buffers.kind[index] = kind
  buffers.scale[index] = scale
  buffers.phase[index] = hashUnit(cell, slot + 977)
  buffers.tint[index * 3] = color.r / 255
  buffers.tint[index * 3 + 1] = color.g / 255
  buffers.tint[index * 3 + 2] = color.b / 255
  buffers.count = index + 1
  return true
}

/**
 * A value in [0, 1) from two integers. Deliberately not `Math.random`: the whole point is that the
 * same cell and slot give the same answer on every poll, in every browser, forever.
 */
export function hashUnit(a: number, b: number): number {
  let h = Math.imul(Math.trunc(a) ^ 0x9e3779b9, 0x85ebca6b)
  h = Math.imul(h ^ Math.trunc(b) ^ (h >>> 13), 0xc2b2ae35)
  h ^= h >>> 16
  return (h >>> 0) / 4_294_967_296
}
