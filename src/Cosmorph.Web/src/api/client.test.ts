import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest'
import { createWorld } from './client'

const token = 'header.payload.signature'

function jsonResponse(status: number): Response {
  return { ok: status >= 200 && status < 300, status } as unknown as Response
}

describe('createWorld', () => {
  let fetchMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    fetchMock = vi.fn(() => Promise.resolve(jsonResponse(201)))
    vi.stubGlobal('fetch', fetchMock)
    vi.stubGlobal('crypto', {
      ...globalThis.crypto,
      randomUUID: () => '11111111-2222-3333-4444-555555555555',
    })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('carries the bearer token and an idempotency key', async () => {
    await createWorld({ worldId: 'quiet-meridian', name: 'Quiet Meridian', seed: 7, isPublic: true }, token)

    expect(fetchMock).toHaveBeenCalledTimes(1)
    const [path, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(path).toBe('/api/worlds')
    expect(init.method).toBe('POST')

    const headers = init.headers as Record<string, string>
    expect(headers['Authorization']).toBe(`Bearer ${token}`)
    expect(headers['Idempotency-Key']).toBe('11111111-2222-3333-4444-555555555555')
  })

  it('sends no actor in the body, because the API decides that from the token', async () => {
    await createWorld({ worldId: 'quiet-meridian', name: '  Quiet Meridian  ', seed: 7, isPublic: false }, token)

    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    const body = JSON.parse(init.body as string) as Record<string, unknown>
    expect(body).toEqual({
      worldId: 'quiet-meridian',
      name: 'Quiet Meridian',
      seed: 7,
      isPublic: false,
    })
  })

  it.each([
    ['UPPER-CASE', 'Invalid world identifier.'],
    ['ab', 'Invalid world identifier.'],
  ])('refuses %s before making a request', async (worldId, message) => {
    await expect(
      createWorld({ worldId, name: 'Name', seed: 1, isPublic: true }, token),
    ).rejects.toThrow(message)
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('refuses a seed that cannot survive a JSON round trip', async () => {
    await expect(
      createWorld({ worldId: 'quiet-meridian', name: 'Name', seed: 1.5, isPublic: true }, token),
    ).rejects.toThrow('whole number')
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('explains a rejected token rather than reporting a status code', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse(401))

    await expect(
      createWorld({ worldId: 'quiet-meridian', name: 'Name', seed: 1, isPublic: true }, token),
    ).rejects.toThrow('Your session has expired. Sign in again.')
  })

  it('explains a taken identifier', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse(409))

    await expect(
      createWorld({ worldId: 'quiet-meridian', name: 'Name', seed: 1, isPublic: true }, token),
    ).rejects.toThrow('already taken')
  })
})
