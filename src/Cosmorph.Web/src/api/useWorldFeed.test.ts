import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest'
import { renderHook, waitFor, cleanup } from '@testing-library/react'
import { useWorldFeed } from './useWorldFeed'

const snapshotBody = {
  schema: 'spectator-snapshot/1',
  worldId: 'demo-world',
  tick: 5,
  version: 2,
  day: 5,
  season: 1,
  chapter: 1,
  healthPermille: 600,
  gridWidth: 1,
  gridHeight: 1,
  lastEventSequence: 2,
  species: [],
  cells: {
    biome: [4],
    vitalityPermille: [500],
    dominantStress: [0],
    stressPermille: [0],
    temperatureDeciC: [100],
    moisturePermille: [400],
    populationPressurePermille: [200],
  },
}

const summaryBody = {
  worldId: 'demo-world',
  name: 'Demo',
  tick: 5,
  day: 5,
  season: 1,
  chapter: 1,
  seasonPhase: 'Thaw',
  healthPermille: 600,
  isPaused: false,
  lastUpdatedUtc: '2026-01-01T00:00:00Z',
  gridWidth: 1,
  gridHeight: 1,
  simulationVersion: 'sim/1.0.0',
  contentVersion: 'season-1.0.0',
  worldmindMode: 'FakeGameMaster',
}

function jsonResponse(body: unknown, status = 200, etag = '"v1"'): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: { get: (name: string) => (name === 'ETag' ? etag : null) },
    json: () => Promise.resolve(body),
  } as unknown as Response
}

describe('useWorldFeed', () => {
  let requests: string[]

  beforeEach(() => {
    requests = []
    vi.stubGlobal(
      'fetch',
      vi.fn((input: string) => {
        requests.push(input)
        if (input === '/api/worlds') {
          return Promise.resolve(
            jsonResponse({
              worldmindMode: 'FakeGameMaster',
              worlds: [
                {
                  worldId: 'demo-world',
                  name: 'Demo',
                  tick: 5,
                  chapter: 1,
                  lastAdvancedAtUtc: '2026-01-01T00:00:00Z',
                },
              ],
            }),
          )
        }
        if (input.endsWith('/snapshot')) {
          return Promise.resolve(jsonResponse(snapshotBody))
        }
        if (input.includes('/events')) {
          return Promise.resolve(
            jsonResponse({
              cursor: 2,
              latestSequence: 2,
              events: [
                { sequence: 1, tick: 4, type: 'Drought', chapter: 1, magnitude: 5 },
                { sequence: 2, tick: 5, type: 'Recovery', chapter: 1, magnitude: 3 },
              ],
            }),
          )
        }
        return Promise.resolve(jsonResponse(summaryBody))
      }),
    )
  })

  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
  })

  it('loads a snapshot and orders the Chronicle newest first', async () => {
    const { result } = renderHook(() => useWorldFeed('demo-world', 60_000))

    await waitFor(() => expect(result.current.snapshot).not.toBeNull())
    await waitFor(() => expect(result.current.events.length).toBe(2))

    expect(result.current.events[0]?.sequence).toBe(2)
    expect(result.current.connection).toBe('live')
    expect(requests.some((r) => r.includes('after=0&limit=50'))).toBe(true)
  })

  it('reports a disconnected feed when a request fails', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(jsonResponse({}, 503))),
    )

    const { result } = renderHook(() => useWorldFeed('demo-world', 60_000))
    await waitFor(() => expect(result.current.connection).toBe('disconnected'))
  })

  it('does not poll a world until one is selected', () => {
    renderHook(() => useWorldFeed(null, 60_000))
    expect(requests.every((r) => r === '/api/worlds')).toBe(true)
  })
})
