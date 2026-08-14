import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest'
import { renderHook, waitFor, cleanup } from '@testing-library/react'
import { useWorldLife } from './useWorldLife'

const lifeBody = {
  schema: 'spectator-life/1',
  worldId: 'demo-world',
  tick: 5,
  version: 2,
  gridWidth: 1,
  gridHeight: 1,
  elevation: [600],
  biomassPermille: [500],
  timber: [400],
  stone: [300],
  fibre: [200],
  seaLevel: 520,
  species: [
    {
      species: 'verdant-moss',
      displayName: 'Verdant Moss',
      archetype: 'Producer',
      population: [7],
    },
  ],
}

function jsonResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: { get: () => null },
    json: () => Promise.resolve(body),
  } as unknown as Response
}

describe('useWorldLife', () => {
  let requests: string[]

  beforeEach(() => {
    requests = []
    vi.stubGlobal(
      'fetch',
      vi.fn((input: string) => {
        requests.push(input)
        return Promise.resolve(jsonResponse(lifeBody))
      }),
    )
  })

  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
  })

  /*
   * The whole point of the read is that a viewer watching the whole globe never pays for it. If this
   * ever starts fetching while zoomed out, every spectator is downloading a column per species for
   * every cell in the world, for nothing they can see.
   */
  it('asks for nothing at all while the camera is too far out', async () => {
    renderHook(() => useWorldLife('demo-world', false, 2))

    await Promise.resolve()
    expect(requests).toEqual([])
  })

  it('asks for nothing until a world is chosen', async () => {
    renderHook(() => useWorldLife(null, true, 2))

    await Promise.resolve()
    expect(requests).toEqual([])
  })

  it('reads the life of the world once the camera closes in', async () => {
    const { result } = renderHook(() => useWorldLife('demo-world', true, 2))

    await waitFor(() => expect(result.current.life).not.toBeNull())
    expect(requests).toEqual(['/api/worlds/demo-world/life'])
    expect(result.current.life?.species[0]?.population).toEqual([7])
    expect(result.current.failed).toBe(false)
  })

  it('reads it again when the tick moves the world on under it', async () => {
    const { result, rerender } = renderHook(
      ({ version }: { version: number }) => useWorldLife('demo-world', true, version),
      { initialProps: { version: 2 } },
    )

    await waitFor(() => expect(result.current.life).not.toBeNull())
    rerender({ version: 3 })
    await waitFor(() => expect(requests.length).toBe(2))
  })

  it('drops what it read when the camera pulls back out', async () => {
    const { result, rerender } = renderHook(
      ({ closeUp }: { closeUp: boolean }) => useWorldLife('demo-world', closeUp, 2),
      { initialProps: { closeUp: true } },
    )

    await waitFor(() => expect(result.current.life).not.toBeNull())
    rerender({ closeUp: false })

    expect(result.current.life).toBeNull()
    expect(result.current.loading).toBe(false)
  })

  it('reports a failure rather than drawing a world it could not read', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(jsonResponse({}, 503))),
    )

    const { result } = renderHook(() => useWorldLife('demo-world', true, 2))

    await waitFor(() => expect(result.current.failed).toBe(true))
    expect(result.current.life).toBeNull()
  })
})
