import { describe, expect, it } from 'vitest'
import {
  cellToLatLon,
  cellToPoint,
  cellToUv,
  rotationForLongitude,
  tiltForLatitude,
  uvToCell,
  uvToPoint,
} from './projection'

const WIDTH = 4
const HEIGHT = 3

describe('grid and sphere mapping', () => {
  it('round trips every cell of a grid, poles included', () => {
    for (let cellIndex = 0; cellIndex < WIDTH * HEIGHT; cellIndex++) {
      const { u, v } = cellToUv(cellIndex, WIDTH, HEIGHT)
      expect(uvToCell(u, v, WIDTH, HEIGHT)).toBe(cellIndex)
    }
  })

  it('puts the first row at the north pole, where the shader samples it', () => {
    // Row 0 is latitude +90, and the flipped v the shader uses puts it at v near 1.
    expect(cellToUv(0, WIDTH, HEIGHT).v).toBeGreaterThan(0.5)
    expect(cellToLatLon(0, WIDTH, HEIGHT).latitude).toBeGreaterThan(0)

    const lastRow = WIDTH * (HEIGHT - 1)
    expect(cellToUv(lastRow, WIDTH, HEIGHT).v).toBeLessThan(0.5)
    expect(cellToLatLon(lastRow, WIDTH, HEIGHT).latitude).toBeLessThan(0)
  })

  it('wraps longitude at the seam rather than running off the end of a row', () => {
    // u = 1 is the same meridian as u = 0, so it belongs to the first column, not a fifth one.
    expect(uvToCell(1, 0.9, WIDTH, HEIGHT)).toBe(0)
    expect(uvToCell(0, 0.9, WIDTH, HEIGHT)).toBe(0)

    // A shade before the seam is the last column, and a shade past it is the first.
    expect(uvToCell(0.999, 0.9, WIDTH, HEIGHT)).toBe(WIDTH - 1)
    expect(uvToCell(1.001, 0.9, WIDTH, HEIGHT)).toBe(0)
    expect(uvToCell(-0.001, 0.9, WIDTH, HEIGHT)).toBe(WIDTH - 1)
  })

  it('clamps a point at either pole into the grid', () => {
    expect(uvToCell(0.1, 1, WIDTH, HEIGHT)).toBe(0)
    expect(uvToCell(0.1, 0, WIDTH, HEIGHT)).toBe(WIDTH * (HEIGHT - 1))
  })

  it('never leaves the grid, whatever it is handed', () => {
    for (const [u, v] of [
      [Number.NaN, 0.5],
      [0.5, Number.NaN],
      [1e9, -1e9],
      [-1e9, 1e9],
    ]) {
      const cell = uvToCell(u as number, v as number, WIDTH, HEIGHT)
      expect(cell).toBeGreaterThanOrEqual(0)
      expect(cell).toBeLessThan(WIDTH * HEIGHT)
    }
  })

  it('clamps a cell index outside the grid instead of computing a place that is not there', () => {
    expect(cellToUv(9999, WIDTH, HEIGHT)).toEqual(cellToUv(WIDTH * HEIGHT - 1, WIDTH, HEIGHT))
    expect(cellToLatLon(-5, WIDTH, HEIGHT)).toEqual(cellToLatLon(0, WIDTH, HEIGHT))
  })
})

describe('aiming the camera', () => {
  // u = 0 sits at −X while the camera looks down +Z, so a longitude of 0 needs a quarter turn.
  it.each([
    [0, -Math.PI / 2],
    [90, -Math.PI],
    [-90, 0],
    [180, -Math.PI * 1.5],
  ])('rotates to longitude %i', (longitude, expected) => {
    expect(rotationForLongitude(longitude)).toBeCloseTo(expected, 10)
  })

  it('turns a full circle over the whole range of longitudes', () => {
    expect(rotationForLongitude(-180) - rotationForLongitude(180)).toBeCloseTo(Math.PI * 2, 10)
  })

  it('tilts to a latitude, bounded so the poles stay readable', () => {
    expect(tiltForLatitude(0)).toBe(0)
    expect(tiltForLatitude(45)).toBeCloseTo(Math.PI / 4, 10)
    expect(tiltForLatitude(90)).toBe(1)
    expect(tiltForLatitude(-90)).toBe(-1)
  })
})

/**
 * The inverse of {@link uvToPoint}, written out here rather than shipped: nothing in the application
 * needs it, but a projection you cannot undo is a projection you cannot check.
 */
function pointToUv({ x, y, z }: { x: number; y: number; z: number }): { u: number; v: number } {
  const theta = Math.acos(Math.min(1, Math.max(-1, y)))
  const phi = Math.atan2(z, -x)
  return {
    u: ((phi / (Math.PI * 2)) % 1 + 1) % 1,
    v: 1 - theta / Math.PI,
  }
}

describe('placing a cell on the sphere', () => {
  it('puts v = 1 at the north pole and v = 0 at the south, whatever the longitude', () => {
    for (const u of [0, 0.25, 0.5, 0.75, 1]) {
      const north = uvToPoint(u, 1)
      const south = uvToPoint(u, 0)

      expect(north.y).toBeCloseTo(1, 10)
      expect(north.x).toBeCloseTo(0, 10)
      expect(north.z).toBeCloseTo(0, 10)
      expect(south.y).toBeCloseTo(-1, 10)
      expect(south.x).toBeCloseTo(0, 10)
      expect(south.z).toBeCloseTo(0, 10)
    }
  })

  it('starts the seam at -X, which is what the camera rotation assumes', () => {
    // u = 0 on the equator sits at −X, and a quarter turn later at +Z. If this ever flips, labels
    // land on the far side of the planet from the cells they name.
    expect(uvToPoint(0, 0.5).x).toBeCloseTo(-1, 10)
    expect(uvToPoint(0, 0.5).y).toBeCloseTo(0, 10)
    expect(uvToPoint(0, 0.5).z).toBeCloseTo(0, 10)
    expect(uvToPoint(0.25, 0.5).z).toBeCloseTo(1, 10)
    expect(uvToPoint(0.5, 0.5).x).toBeCloseTo(1, 10)
  })

  it('joins the seam rather than tearing it', () => {
    const before = uvToPoint(1, 0.4)
    const after = uvToPoint(0, 0.4)

    expect(before.x).toBeCloseTo(after.x, 10)
    expect(before.y).toBeCloseTo(after.y, 10)
    expect(before.z).toBeCloseTo(after.z, 10)
  })

  it('never leaves the unit sphere, so a label is always on the surface', () => {
    for (let cellIndex = 0; cellIndex < 64 * 32; cellIndex += 37) {
      const { x, y, z } = cellToPoint(cellIndex, 64, 32)
      expect(Math.sqrt(x * x + y * y + z * z)).toBeCloseTo(1, 10)
    }
  })

  it('agrees with the cell the raycaster would pick, for every cell of a real grid', () => {
    // Project a cell centre onto the sphere, read the point back as a uv and ask which cell it is.
    // This is exactly the round trip a click makes, so a disagreement here is a label over the
    // wrong place.
    for (let cellIndex = 0; cellIndex < 64 * 32; cellIndex++) {
      const { u, v } = pointToUv(cellToPoint(cellIndex, 64, 32))
      expect(uvToCell(u, v, 64, 32)).toBe(cellIndex)
    }
  })

  it('places a cell exactly where its own uv does', () => {
    const uv = cellToUv(700, 64, 32)

    expect(cellToPoint(700, 64, 32)).toEqual(uvToPoint(uv.u, uv.v))
  })
})
