import { useEffect, useState } from 'react'
import { fetchCellDetail } from '../api/client'
import type { CellDetail } from '../api/dto'

export interface CellDetailState {
  readonly detail: CellDetail | null
  readonly loading: boolean
  readonly failed: boolean
}

/**
 * Reads the selected place on demand. It refetches when the snapshot version advances, so an open
 * inspector keeps pace with the world without adding a second poll of its own, and aborts in flight
 * when the selection changes so a slow response cannot overwrite a newer one.
 */
export function useCellDetail(
  worldId: string | null,
  cellIndex: number | null,
  version: number | null,
): CellDetailState {
  const [detail, setDetail] = useState<CellDetail | null>(null)
  const [loading, setLoading] = useState(false)
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    if (!worldId || cellIndex === null) {
      setDetail(null)
      setLoading(false)
      setFailed(false)
      return
    }

    const controller = new AbortController()
    setLoading(true)
    setFailed(false)

    fetchCellDetail(worldId, cellIndex, controller.signal)
      .then((value) => {
        if (!controller.signal.aborted) {
          setDetail(value)
          setLoading(false)
        }
      })
      .catch(() => {
        if (!controller.signal.aborted) {
          setDetail(null)
          setLoading(false)
          setFailed(true)
        }
      })

    return () => controller.abort()
  }, [worldId, cellIndex, version])

  return { detail, loading, failed }
}
