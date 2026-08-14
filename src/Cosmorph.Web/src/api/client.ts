import {
  parseEventPage,
  parseNeighbourhood,
  parseSnapshot,
  parseWorldLife,
  parseWorldList,
  parseWorldSummary,
  type EventPage,
  type Neighbourhood,
  type SpectatorSnapshot,
  type WorldLife,
  type WorldList,
  type WorldSummary,
} from './dto'

/**
 * Same-origin API client. Spectator reads carry no credentials; a mutation carries only the bearer
 * token the browser obtained from Microsoft Entra ID. Neither ever talks to Azure Storage or holds
 * an Azure credential.
 */

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

/**
 * Reads a place and the places around it. Anonymous like the rest of the spectator surface, and
 * fetched only when a cell is selected rather than on the snapshot poll, because per-cell species
 * would bloat every poll for every viewer. The block contains the centre, so this is the only read
 * an open inspector needs.
 */
export async function fetchNeighbourhood(
  worldId: string,
  cellIndex: number,
  radius: number,
  signal: AbortSignal,
): Promise<Neighbourhood> {
  assertWorldId(worldId)
  const index = Math.max(0, Math.trunc(cellIndex))
  const reach = Math.min(MAX_NEIGHBOURHOOD_RADIUS, Math.max(0, Math.trunc(radius)))
  return parseNeighbourhood(
    await getJson(`/api/worlds/${worldId}/cells/${index}/neighbourhood?radius=${reach}`, signal),
  )
}

/**
 * Reads what is alive on every cell of a world. Whole-planet rather than per-place, so it is fetched
 * only while the camera is close enough for individual creatures to be visible, and never on the
 * snapshot poll: a viewer looking at the whole globe pays nothing for it.
 */
export async function fetchWorldLife(worldId: string, signal: AbortSignal): Promise<WorldLife> {
  assertWorldId(worldId)
  return parseWorldLife(await getJson(`/api/worlds/${worldId}/life`, signal))
}

/** The widest block the API will answer. A larger radius is a 400, so it is bounded here first. */
export const MAX_NEIGHBOURHOOD_RADIUS = 2

/** The largest seed that survives a JSON round trip without losing precision. */
export const MAX_SEED = Number.MAX_SAFE_INTEGER

export interface NewWorld {
  readonly worldId: string
  readonly name: string
  readonly seed: number
  readonly isPublic: boolean
}

/**
 * Creates a world. The caller is whoever the token says they are; nothing in this body identifies
 * an actor, and the API would ignore it if it did.
 */
export async function createWorld(world: NewWorld, accessToken: string): Promise<void> {
  assertWorldId(world.worldId)
  if (world.name.trim().length === 0 || world.name.length > 60) {
    throw new Error('A world name of up to 60 characters is required.')
  }
  if (!Number.isSafeInteger(world.seed) || world.seed < 0) {
    throw new Error('The seed must be a whole number that is not negative.')
  }

  const response = await fetch('/api/worlds', {
    method: 'POST',
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json',
      Authorization: `Bearer ${accessToken}`,

      // A retry of the same click must not create a second world.
      'Idempotency-Key': crypto.randomUUID(),
    },
    body: JSON.stringify({
      worldId: world.worldId,
      name: world.name.trim(),
      seed: world.seed,
      isPublic: world.isPublic,
    }),
  })

  if (response.ok) {
    return
  }

  throw new Error(describeFailure(response.status, 'The world could not be created'))
}

/**
 * Turns a failed mutation into something a person can act on. Shared with the Warden panel, so both
 * call sites explain a rejection the same way. `subject` is a noun phrase naming what failed.
 */
export function describeFailure(status: number, subject: string): string {
  switch (status) {
    case 400:
      return `${subject}: check the values you entered.`
    case 401:
      return 'Your session has expired. Sign in again.'
    case 403:
      return `${subject}: your account is not permitted to do that.`
    case 404:
      return `${subject}: that world was not found, or it is not yours.`
    case 409:
      return `${subject}: that identifier is already taken.`
    case 429:
      return 'Too many requests. Try again in a minute.'
    default:
      return `${subject} (status ${status}).`
  }
}
