import { useEffect, useState } from 'react'
import { fetchNeighbourhood } from '../api/client'
import type { CellDetail, Neighbourhood } from '../api/dto'

export interface NeighbourhoodState {
  readonly block: Neighbourhood | null
  /** The selected place itself, read out of the block rather than fetched a second time. */
  readonly center: CellDetail | null
  readonly loading: boolean
  readonly failed: boolean
}

/**
 * Reads the selected place and its neighbours on demand. It refetches when the snapshot version
 * advances, so an open inspector keeps pace with the world without adding a second poll of its own,
 * and aborts in flight when the selection changes so a slow response cannot overwrite a newer one.
 */
export function useNeighbourhood(
  worldId: string | null,
  cellIndex: number | null,
  radius: number,
  version: number | null,
): NeighbourhoodState {
  const [block, setBlock] = useState<Neighbourhood | null>(null)
  const [loading, setLoading] = useState(false)
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    if (!worldId || cellIndex === null) {
      setBlock(null)
      setLoading(false)
      setFailed(false)
      return
    }

    const controller = new AbortController()
    setLoading(true)
    setFailed(false)

    fetchNeighbourhood(worldId, cellIndex, radius, controller.signal)
      .then((value) => {
        if (!controller.signal.aborted) {
          setBlock(value)
          setLoading(false)
        }
      })
      .catch(() => {
        if (!controller.signal.aborted) {
          setBlock(null)
          setLoading(false)
          setFailed(true)
        }
      })

    return () => controller.abort()
  }, [worldId, cellIndex, radius, version])

  // A block from the previous selection is not this place, so the centre is matched by index.
  const center = block?.cells.find((cell) => cell.cellIndex === block.centerCellIndex) ?? null
  return { block, center, loading, failed }
}
