import type { CellDetail, Neighbourhood, SpeciesAtCell } from '../api/dto'
import type { CellLabel } from '../globe/GlobeScene'

/**
 * Turns a block of places into the text pinned over the planet. Kept pure and away from both React
 * and WebGL so the wording is testable: this is the only thing the globe says in words, and getting
 * it wrong is a lie about a live world rather than a cosmetic slip.
 */

/** Splits the PascalCase enum names the API sends, so `BorealForest` reads as `Boreal forest`. */
export function spaced(name: string): string {
  const words = name.replace(/([a-z])([A-Z])/g, '$1 $2')
  return words.charAt(0) + words.slice(1).toLowerCase()
}

/** Thousands as `12k`, so a population fits a label without wrapping the planet in text. */
export function compact(value: number): string {
  if (!Number.isFinite(value)) {
    return '0'
  }
  const rounded = Math.trunc(value)
  if (Math.abs(rounded) < 10_000) {
    return rounded.toLocaleString()
  }
  return `${Math.round(rounded / 1000).toLocaleString()}k`
}

/** The most populous species in a cell, which is what a one-line label has room to name. */
export function leadingSpecies(cell: CellDetail): SpeciesAtCell | null {
  let best: SpeciesAtCell | null = null
  for (const entry of cell.species) {
    if (!best || entry.population > best.population) {
      best = entry
    }
  }
  return best
}

/**
 * Three lines per place: what lives there, what it is made of, and what is happening to it. The
 * centre keeps its full species name; a neighbour is abbreviated so a 5 × 5 block stays legible.
 */
export function cellLabels(block: Neighbourhood | null): readonly CellLabel[] {
  if (!block) {
    return []
  }

  return block.cells.map((cell) => {
    const isCenter = cell.cellIndex === block.centerCellIndex
    const leading = leadingSpecies(cell)
    const total = cell.species.reduce((sum, entry) => sum + entry.population, 0)

    const life = leading
      ? `${leading.displayName} ${compact(leading.population)}${
          cell.species.length > 1 ? ` of ${compact(total)}` : ''
        }`
      : 'lifeless'

    const note = cell.construction
      ? `${cell.construction.kind} ${cell.construction.level}`
      : spaced(cell.biome)
    const stress = cell.dominantStress === 'None' ? '' : ` · ${spaced(cell.dominantStress)}`

    const lines = isCenter
      ? [life, `t${compact(cell.timber)} s${compact(cell.stone)} f${compact(cell.fibre)}`, note + stress]
      : [life, note + stress]

    return { cellIndex: cell.cellIndex, lines }
  })
}
