import { useEffect, useState } from 'react'
import { fetchWorldLife } from './client'
import type { WorldLife } from './dto'

export interface WorldLifeState {
  readonly life: WorldLife | null
  readonly loading: boolean
  readonly failed: boolean
}

/**
 * Reads what is alive on every cell, but only while the camera is close enough to draw it. A viewer
 * watching the whole globe never asks for it at all; one who zooms in gets it once and then again
 * whenever the snapshot version advances, so the critters keep pace with the tick without a poll of
 * their own. Zooming back out drops the data, so nothing is ever drawn from a world that has moved on.
 */
export function useWorldLife(
  worldId: string | null,
  closeUp: boolean,
  version: number | null,
): WorldLifeState {
  const [life, setLife] = useState<WorldLife | null>(null)
  const [loading, setLoading] = useState(false)
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    if (!worldId || !closeUp) {
      setLife(null)
      setLoading(false)
      setFailed(false)
      return
    }

    const controller = new AbortController()
    setLoading(true)
    setFailed(false)

    fetchWorldLife(worldId, controller.signal)
      .then((value) => {
        if (!controller.signal.aborted) {
          setLife(value)
          setLoading(false)
        }
      })
      .catch(() => {
        if (!controller.signal.aborted) {
          setLife(null)
          setLoading(false)
          setFailed(true)
        }
      })

    return () => controller.abort()
  }, [worldId, closeUp, version])

  return { life, loading, failed }
}
