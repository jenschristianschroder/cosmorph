import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest'
import {
  adoptWorld,
  describeDraftProblem,
  fetchCharters,
  parseCharterList,
  putCharter,
  type CharterDraft,
} from './charters'

const token = 'header.payload.signature'

function response(status: number, body?: unknown): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: () => Promise.resolve(body),
  } as unknown as Response
}

const draft: CharterDraft = {
  displayName: 'Moss Keeper',
  controlledSpecies: 'verdant-moss',
  controlledRegion: [0, 1, 2],
  goals: ['Preserve'],
  goalWeights: [100],
  taboos: ['NeverBurn'],
  actionBudget: 10,
  budgetRenewalPerChapter: 5,
  impactCeilingPerChapter: 20,
}

const warden = {
  wardenId: 'moss-keeper',
  displayName: 'Moss Keeper',
  controlledSpecies: 'verdant-moss',
  controlledRegion: [0, 1],
  goals: ['Preserve'],
  goalWeights: [100],
  taboos: ['NeverBurn'],
  actionBudget: 10,
  budgetRenewalPerChapter: 5,
  impactCeilingPerChapter: 20,
  impactUsedThisChapter: 3,
}

const charterList = {
  schema: 'warden-charters/1',
  worldId: 'demo-world',
  version: 12,
  gridWidth: 64,
  gridHeight: 32,
  wardens: [warden],
}

describe('warden charters over the wire', () => {
  let fetchMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    fetchMock = vi.fn(() => Promise.resolve(response(202)))
    vi.stubGlobal('fetch', fetchMock)
    vi.stubGlobal('crypto', {
      ...globalThis.crypto,
      randomUUID: () => '11111111-2222-3333-4444-555555555555',
    })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('reads charters with the bearer token', async () => {
    fetchMock.mockResolvedValueOnce(response(200, charterList))

    const list = await fetchCharters('demo-world', token, new AbortController().signal)

    const [path, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(path).toBe('/api/worlds/demo-world/wardens')
    expect((init.headers as Record<string, string>)['Authorization']).toBe(`Bearer ${token}`)
    expect(list.wardens[0]?.wardenId).toBe('moss-keeper')
  })

  it('reports a world it cannot read as absent rather than as a status code', async () => {
    fetchMock.mockResolvedValueOnce(response(404))

    await expect(fetchCharters('demo-world', token, new AbortController().signal)).rejects.toThrow(
      /could not be read/,
    )
  })

  it('saves a charter with the bearer token and an idempotency key', async () => {
    await putCharter('demo-world', 'moss-keeper', draft, token)

    const [path, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(path).toBe('/api/worlds/demo-world/wardens/moss-keeper/charter')
    expect(init.method).toBe('PUT')

    const headers = init.headers as Record<string, string>
    expect(headers['Authorization']).toBe(`Bearer ${token}`)
    expect(headers['Idempotency-Key']).toBe('11111111-2222-3333-4444-555555555555')
  })

  it('sends no actor and no impact tally, because both are the API to decide', async () => {
    await putCharter('demo-world', 'moss-keeper', draft, token)

    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    const body = JSON.parse(init.body as string) as Record<string, unknown>
    expect(Object.keys(body).sort()).toEqual([
      'actionBudget',
      'budgetRenewalPerChapter',
      'controlledRegion',
      'controlledSpecies',
      'displayName',
      'goalWeights',
      'goals',
      'impactCeilingPerChapter',
      'taboos',
    ])
  })

  it.each(['UPPER', 'a', 'has space', '../escape'])(
    'refuses the warden identifier %s before making a request',
    async (wardenId) => {
      await expect(putCharter('demo-world', wardenId, draft, token)).rejects.toThrow()
      expect(fetchMock).not.toHaveBeenCalled()
    },
  )

  it('explains an expired session rather than reporting a status code', async () => {
    fetchMock.mockResolvedValueOnce(response(401))

    await expect(putCharter('demo-world', 'moss-keeper', draft, token)).rejects.toThrow(
      'Your session has expired. Sign in again.',
    )
  })

  it('adopts a world with the bearer token, and asks for nothing else', async () => {
    fetchMock.mockResolvedValueOnce(response(200, { worldId: 'demo-world', adopted: true }))

    await adoptWorld('demo-world', token)

    const [path, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(path).toBe('/api/worlds/demo-world/owner')
    expect(init.method).toBe('POST')
    expect(init.body).toBeUndefined()
    expect((init.headers as Record<string, string>)['Authorization']).toBe(`Bearer ${token}`)
  })

  /*
   * The API answers a world owned by somebody else exactly as it answers one that does not exist,
   * so the browser must not claim to know which of the two it was.
   */
  it('says a world cannot be adopted rather than guessing why', async () => {
    fetchMock.mockResolvedValueOnce(response(404))

    await expect(adoptWorld('demo-world', token)).rejects.toThrow(/belongs to somebody else/)
  })

  it('explains an expired session when adopting, too', async () => {
    fetchMock.mockResolvedValueOnce(response(401))

    await expect(adoptWorld('demo-world', token)).rejects.toThrow(/session has expired/)
  })

  it('asks the reader to try again when the tick job wrote first', async () => {
    fetchMock.mockResolvedValueOnce(response(409))

    await expect(adoptWorld('demo-world', token)).rejects.toThrow(/Try again/)
  })
})

/** Each of these is a bound the API also enforces, caught here so a save is never wasted. */
const badDrafts: [string, Partial<CharterDraft>, RegExp][] = [
  ['no name', { displayName: '' }, /name/i],
  ['no species', { controlledSpecies: '' }, /species/i],
  ['no goal', { goals: [], goalWeights: [] }, /goals/i],
  ['a repeated goal', { goals: ['Preserve', 'Preserve'], goalWeights: [50, 50] }, /once/i],
  ['a weight below one', { goalWeights: [0] }, /weights/i],
  ['a weight for a goal that is not there', { goalWeights: [50, 50] }, /weight/i],
  ['no region', { controlledRegion: [] }, /at least one cell/i],
  ['too wide a region', { controlledRegion: Array.from({ length: 513 }, (_, i) => i) }, /at most 512/i],
  ['a budget past its ceiling', { actionBudget: 101 }, /0 to 100/i],
  ['a negative impact ceiling', { impactCeilingPerChapter: -1 }, /0 to 100/i],
]

describe('charter drafts', () => {
  it('accepts a charter the API would accept', () => {
    expect(describeDraftProblem(draft)).toBeNull()
  })

  it.each(badDrafts)('refuses %s', (_name, overrides, message) => {
    expect(describeDraftProblem({ ...draft, ...overrides }) ?? '').toMatch(message)
  })
})

describe('charter list parsing', () => {
  it('rejects a payload from a schema it does not know', () => {
    expect(() => parseCharterList({ ...charterList, schema: 'warden-charters/9' })).toThrow()
  })

  it('rejects a charter whose fields are not what they claim to be', () => {
    expect(() => parseCharterList({ ...charterList, wardens: [{ ...warden, actionBudget: 'lots' }] })).toThrow()
  })

  it('rejects a region larger than a charter may hold', () => {
    expect(() =>
      parseCharterList({
        ...charterList,
        wardens: [{ ...warden, controlledRegion: Array.from({ length: 513 }, (_, i) => i) }],
      }),
    ).toThrow()
  })
})
