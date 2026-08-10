import { useEffect, useMemo, useRef, useState } from 'react'
import {
  fetchEvents,
  fetchSnapshot,
  fetchSummary,
  fetchWorlds,
  type Conditional,
} from '../api/client'
import type { SpectatorEvent, SpectatorSnapshot, WorldList, WorldSummary } from '../api/dto'

const MAX_FEED_EVENTS = 60

export type ConnectionState = 'loading' | 'live' | 'stale' | 'disconnected' | 'corrupt'

export interface WorldFeed {
  readonly worlds: WorldList | null
  readonly summary: WorldSummary | null
  readonly snapshot: SpectatorSnapshot | null
  readonly previousSnapshot: SpectatorSnapshot | null
  readonly events: readonly SpectatorEvent[]
  readonly connection: ConnectionState
  readonly lastUpdated: number | null
}

/**
 * Snapshot plus cursor-based event polling with conditional requests. Polling slows down when the
 * tab is hidden, and a missed event range is recovered from the last committed sequence.
 */
export function useWorldFeed(worldId: string | null, intervalMs = 5000): WorldFeed {
  const [worlds, setWorlds] = useState<WorldList | null>(null)
  const [summary, setSummary] = useState<WorldSummary | null>(null)
  const [snapshot, setSnapshot] = useState<SpectatorSnapshot | null>(null)
  const [previousSnapshot, setPreviousSnapshot] = useState<SpectatorSnapshot | null>(null)
  const [events, setEvents] = useState<readonly SpectatorEvent[]>([])
  const [connection, setConnection] = useState<ConnectionState>('loading')
  const [lastUpdated, setLastUpdated] = useState<number | null>(null)

  const cursorRef = useRef(0)
  const summaryEtag = useRef<string | null>(null)
  const latestSnapshot = useRef<SpectatorSnapshot | null>(null)
  const snapshotEtag = useRef<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    fetchWorlds(controller.signal)
      .then(setWorlds)
      .catch(() => setConnection('disconnected'))
    return () => controller.abort()
  }, [])

  useEffect(() => {
    cursorRef.current = 0
    summaryEtag.current = null
    snapshotEtag.current = null
    setEvents([])
    setSnapshot(null)
    setPreviousSnapshot(null)
    latestSnapshot.current = null
    setConnection('loading')
  }, [worldId])

  useEffect(() => {
    if (!worldId) {
      return
    }

    let cancelled = false
    let timer: ReturnType<typeof setTimeout> | null = null
    const controller = new AbortController()

    const poll = async (): Promise<void> => {
      try {
        const summaryResult: Conditional<WorldSummary> = await fetchSummary(
          worldId,
          summaryEtag.current,
          controller.signal,
        )
        if (cancelled) {
          return
        }
        if (summaryResult.value) {
          summaryEtag.current = summaryResult.etag
          setSummary(summaryResult.value)
        }

        const snapshotResult = await fetchSnapshot(worldId, snapshotEtag.current, controller.signal)
        if (cancelled) {
          return
        }
        if (snapshotResult.value) {
          snapshotEtag.current = snapshotResult.etag
          setPreviousSnapshot(latestSnapshot.current)
          latestSnapshot.current = snapshotResult.value
          setSnapshot(snapshotResult.value)
        }

        const page = await fetchEvents(worldId, cursorRef.current, 50, controller.signal)
        if (cancelled) {
          return
        }
        if (page.events.length > 0) {
          cursorRef.current = page.cursor
          setEvents((current) => [...page.events].reverse().concat(current).slice(0, MAX_FEED_EVENTS))
        } else if (page.latestSequence < cursorRef.current) {
          // The world was rebuilt or the cursor is ahead of the Chronicle; restart from the top.
          cursorRef.current = 0
        }

        setConnection(snapshotResult.notModified ? 'stale' : 'live')
        setLastUpdated(Date.now())
      } catch (error) {
        if (cancelled || controller.signal.aborted) {
          return
        }
        setConnection(error instanceof SyntaxError ? 'corrupt' : 'disconnected')
      } finally {
        if (!cancelled) {
          const hidden = typeof document !== 'undefined' && document.visibilityState === 'hidden'
          timer = setTimeout(() => void poll(), hidden ? intervalMs * 6 : intervalMs)
        }
      }
    }

    void poll()

    return () => {
      cancelled = true
      controller.abort()
      if (timer) {
        clearTimeout(timer)
      }
    }
  }, [worldId, intervalMs])

  return useMemo(
    () => ({
      worlds,
      summary,
      snapshot,
      previousSnapshot,
      events,
      connection,
      lastUpdated,
    }),
    [worlds, summary, snapshot, previousSnapshot, events, connection, lastUpdated],
  )
}

/** Respects the operating-system reduced-motion preference. */
export function usePrefersReducedMotion(): boolean {
  const [reduced, setReduced] = useState(false)

  useEffect(() => {
    if (typeof globalThis.matchMedia !== 'function') {
      return
    }
    const query = globalThis.matchMedia('(prefers-reduced-motion: reduce)')
    setReduced(query.matches)
    const listener = (event: MediaQueryListEvent): void => setReduced(event.matches)
    query.addEventListener('change', listener)
    return () => query.removeEventListener('change', listener)
  }, [])

  return reduced
}
