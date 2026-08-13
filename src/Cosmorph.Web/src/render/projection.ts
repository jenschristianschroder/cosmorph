/**
 * The single source of truth for mapping between grid cells, texture coordinates and the sphere.
 *
 * Two conventions have to agree here, and getting them wrong is invisible until you try to click
 * something. The grid stores row 0 at latitude +90, so the first row of texture data is the north
 * pole. Three.js `SphereGeometry` puts `uv.y = 1` at the north pole and `THREE.DataTexture` uploads
 * with `flipY = false`, which puts the first row of data at `v = 0`. The fragment shader therefore
 * samples `1 - uv.y`, and everything in this module uses that same flipped `v`.
 */

export interface LatLon {
  readonly latitude: number
  readonly longitude: number
}

export interface Uv {
  readonly u: number
  readonly v: number
}

/** A point on the unit sphere, in the mesh's own coordinates before any rotation is applied. */
export interface Point3 {
  readonly x: number
  readonly y: number
  readonly z: number
}

function cellCount(gridWidth: number, gridHeight: number): number {
  return Math.max(1, Math.trunc(gridWidth) * Math.trunc(gridHeight))
}

/** Converts a cell index into the latitude and longitude used to aim the camera. */
export function cellToLatLon(cellIndex: number, gridWidth: number, gridHeight: number): LatLon {
  const clamped = Math.min(Math.max(0, Math.trunc(cellIndex)), cellCount(gridWidth, gridHeight) - 1)
  const row = Math.floor(clamped / gridWidth)
  const column = clamped % gridWidth
  const latitude = 90 - ((row + 0.5) * 180) / gridHeight
  const longitude = ((column + 0.5) * 360) / gridWidth - 180
  return { latitude, longitude }
}

/** The centre of a cell in geometry uv space, the inverse of {@link uvToCell}. */
export function cellToUv(cellIndex: number, gridWidth: number, gridHeight: number): Uv {
  const clamped = Math.min(Math.max(0, Math.trunc(cellIndex)), cellCount(gridWidth, gridHeight) - 1)
  const row = Math.floor(clamped / gridWidth)
  const column = clamped % gridWidth
  return {
    u: (column + 0.5) / gridWidth,
    v: 1 - (row + 0.5) / gridHeight,
  }
}

/**
 * The cell under a point on the sphere. Longitude wraps, so a `u` of exactly 1 lands back in the
 * first column rather than off the end of the row.
 */
export function uvToCell(u: number, v: number, gridWidth: number, gridHeight: number): number {
  const width = Math.max(1, Math.trunc(gridWidth))
  const height = Math.max(1, Math.trunc(gridHeight))
  if (!Number.isFinite(u) || !Number.isFinite(v)) {
    return 0
  }

  const column = ((Math.floor(u * width) % width) + width) % width
  const row = Math.min(height - 1, Math.max(0, Math.floor((1 - v) * height)))
  return row * width + column
}

/**
 * The point on the unit sphere carrying a uv, matching how `THREE.SphereGeometry` places its
 * vertices: the polar angle runs from the north pole down, and `u = 0` sits at −X. Anything that
 * needs to know where a cell *is* on screen goes through here, so the label layer and the raycaster
 * can never drift apart.
 */
export function uvToPoint(u: number, v: number): Point3 {
  const theta = (1 - v) * Math.PI
  const phi = u * 2 * Math.PI
  const sinTheta = Math.sin(theta)
  return {
    x: -Math.cos(phi) * sinTheta,
    y: Math.cos(theta),
    z: Math.sin(phi) * sinTheta,
  }
}

/** The point on the unit sphere at the centre of a cell. */
export function cellToPoint(cellIndex: number, gridWidth: number, gridHeight: number): Point3 {
  const uv = cellToUv(cellIndex, gridWidth, gridHeight)
  return uvToPoint(uv.u, uv.v)
}

/**
 * The mesh rotation about Y that brings a longitude to face the camera. `u = 0` sits at −X on the
 * sphere while the camera looks down +Z, hence the extra quarter turn.
 */
export function rotationForLongitude(longitudeDegrees: number): number {
  return (-longitudeDegrees * Math.PI) / 180 - Math.PI / 2
}

/** The mesh rotation about X that brings a latitude to face the camera, bounded to stay readable. */
export function tiltForLatitude(latitudeDegrees: number): number {
  const radians = (latitudeDegrees * Math.PI) / 180
  return Math.min(1, Math.max(-1, radians))
}
