import {
  parseEventPage,
  parseSnapshot,
  parseWorldList,
  parseWorldSummary,
  type EventPage,
  type SpectatorSnapshot,
  type WorldList,
  type WorldSummary,
} from './dto'

/** Same-origin spectator client. It holds no credentials and never talks to Azure directly. */

export interface Conditional<T> {
  readonly value: T | null
  readonly etag: string | null
  readonly notModified: boolean
}

const worldIdPattern = /^[a-z0-9][a-z0-9-]{2,39}$/

export function isValidWorldId(worldId: string): boolean {
  return worldIdPattern.test(worldId)
}

async function getJson(path: string, signal: AbortSignal): Promise<unknown> {
  const response = await fetch(path, { signal, headers: { Accept: 'application/json' } })
  if (!response.ok) {
    throw new Error(`Request failed with status ${response.status}.`)
  }
  return (await response.json()) as unknown
}

export async function fetchWorlds(signal: AbortSignal): Promise<WorldList> {
  return parseWorldList(await getJson('/api/worlds', signal))
}

async function getConditional<T>(
  path: string,
  etag: string | null,
  signal: AbortSignal,
  parse: (value: unknown) => T,
): Promise<Conditional<T>> {
  const headers: Record<string, string> = { Accept: 'application/json' }
  if (etag) {
    headers['If-None-Match'] = etag
  }

  const response = await fetch(path, { signal, headers })
  if (response.status === 304) {
    return { value: null, etag, notModified: true }
  }
  if (!response.ok) {
    throw new Error(`Request failed with status ${response.status}.`)
  }

  return {
    value: parse((await response.json()) as unknown),
    etag: response.headers.get('ETag'),
    notModified: false,
  }
}

export function fetchSummary(
  worldId: string,
  etag: string | null,
  signal: AbortSignal,
): Promise<Conditional<WorldSummary>> {
  assertWorldId(worldId)
  return getConditional(`/api/worlds/${worldId}`, etag, signal, parseWorldSummary)
}

export function fetchSnapshot(
  worldId: string,
  etag: string | null,
  signal: AbortSignal,
): Promise<Conditional<SpectatorSnapshot>> {
  assertWorldId(worldId)
  return getConditional(`/api/worlds/${worldId}/snapshot`, etag, signal, parseSnapshot)
}

export async function fetchEvents(
  worldId: string,
  after: number,
  limit: number,
  signal: AbortSignal,
): Promise<EventPage> {
  assertWorldId(worldId)
  const cursor = Math.max(0, Math.trunc(after))
  const bounded = Math.min(200, Math.max(1, Math.trunc(limit)))
  return parseEventPage(
    await getJson(`/api/worlds/${worldId}/events?after=${cursor}&limit=${bounded}`, signal),
  )
}

function assertWorldId(worldId: string): void {
  if (!isValidWorldId(worldId)) {
    throw new Error('Invalid world identifier.')
  }
}
