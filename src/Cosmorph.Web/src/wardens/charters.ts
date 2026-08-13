import { describeFailure } from '../api/client'

/**
 * Owner-scoped Warden configuration. Every call here carries the bearer token the browser obtained
 * from Microsoft Entra ID and nothing else: the API resolves the actor from the token, so no
 * identifier in a body or query would be trusted even if one were sent.
 */

/** Mirrors the bounds on `WardenCharter`, so the panel refuses what the API would refuse. */
export const MAX_GOALS = 3
export const MAX_TABOOS = 4
export const MAX_WEIGHT = 100
export const MAX_BUDGET = 100
export const MAX_IMPACT_CEILING = 100
export const MAX_DISPLAY_NAME_LENGTH = 40
export const MAX_REGION_CELLS = 512

export const CHARTER_LIST_SCHEMA = 'warden-charters/1'

export const GOALS = ['Preserve', 'Expand', 'Adapt', 'Cooperate', 'Hunt'] as const
export const TABOOS = ['NeverHunt', 'NeverMigrate', 'NeverBurn', 'NeverCompete'] as const

export type WardenGoal = (typeof GOALS)[number]
export type WardenTaboo = (typeof TABOOS)[number]

const wardenIdPattern = /^[a-z0-9][a-z0-9-]{1,31}$/

export function isValidWardenId(wardenId: string): boolean {
  return wardenIdPattern.test(wardenId)
}

export interface Charter {
  readonly wardenId: string
  readonly displayName: string
  readonly controlledSpecies: string
  readonly controlledRegion: readonly number[]
  readonly goals: readonly string[]
  readonly goalWeights: readonly number[]
  readonly taboos: readonly string[]
  readonly actionBudget: number
  readonly budgetRenewalPerChapter: number
  readonly impactCeilingPerChapter: number
  readonly impactUsedThisChapter: number
}

export interface CharterList {
  readonly schema: string
  readonly worldId: string
  readonly version: number
  readonly gridWidth: number
  readonly gridHeight: number
  readonly wardens: readonly Charter[]
}

/** What the panel sends. Impact used is server state and is deliberately not writable. */
export interface CharterDraft {
  readonly displayName: string
  readonly controlledSpecies: string
  readonly controlledRegion: readonly number[]
  readonly goals: readonly WardenGoal[]
  readonly goalWeights: readonly number[]
  readonly taboos: readonly WardenTaboo[]
  readonly actionBudget: number
  readonly budgetRenewalPerChapter: number
  readonly impactCeilingPerChapter: number
}

export async function fetchCharters(
  worldId: string,
  accessToken: string,
  signal: AbortSignal,
): Promise<CharterList> {
  const response = await fetch(`/api/worlds/${worldId}/wardens`, {
    signal,
    headers: { Accept: 'application/json', Authorization: `Bearer ${accessToken}` },
  })

  if (!response.ok) {
    throw new Error(describeFailure(response.status, 'The Wardens could not be read'))
  }

  return parseCharterList((await response.json()) as unknown)
}

/**
 * Writes a charter. The API answers `202`: the command is applied by the tick job, so the change is
 * pending until the world advances. Writing to an unused warden id creates that Warden.
 */
export async function putCharter(
  worldId: string,
  wardenId: string,
  draft: CharterDraft,
  accessToken: string,
): Promise<void> {
  if (!isValidWardenId(wardenId)) {
    throw new Error('A Warden identifier is lower-case letters, digits and hyphens, 2 to 32 long.')
  }

  const response = await fetch(`/api/worlds/${worldId}/wardens/${wardenId}/charter`, {
    method: 'PUT',
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json',
      Authorization: `Bearer ${accessToken}`,

      // A retried save must not queue the charter twice.
      'Idempotency-Key': crypto.randomUUID(),
    },
    body: JSON.stringify({
      displayName: draft.displayName,
      controlledSpecies: draft.controlledSpecies,
      controlledRegion: [...draft.controlledRegion],
      goals: [...draft.goals],
      goalWeights: [...draft.goalWeights],
      taboos: [...draft.taboos],
      actionBudget: draft.actionBudget,
      budgetRenewalPerChapter: draft.budgetRenewalPerChapter,
      impactCeilingPerChapter: draft.impactCeilingPerChapter,
    }),
  })

  if (!response.ok) {
    throw new Error(describeFailure(response.status, 'The charter could not be saved'))
  }
}

/** Local check of the same bounds the API enforces, so a bad charter never becomes a round trip. */
export function describeDraftProblem(draft: CharterDraft): string | null {
  if (draft.displayName.trim().length === 0 || draft.displayName.length > MAX_DISPLAY_NAME_LENGTH) {
    return `A name of one to ${MAX_DISPLAY_NAME_LENGTH} characters is required.`
  }
  if (draft.controlledSpecies.length === 0) {
    return 'Choose the species this Warden looks after.'
  }
  if (draft.goals.length === 0 || draft.goals.length > MAX_GOALS) {
    return `Choose one to ${MAX_GOALS} goals.`
  }
  if (new Set(draft.goals).size !== draft.goals.length) {
    return 'Each goal may be chosen only once.'
  }
  if (draft.goalWeights.length !== draft.goals.length) {
    return 'Every goal needs a weight.'
  }
  if (draft.goalWeights.some((weight) => weight < 1 || weight > MAX_WEIGHT)) {
    return `Goal weights run from 1 to ${MAX_WEIGHT}.`
  }
  if (draft.taboos.length > MAX_TABOOS) {
    return 'Too many taboos.'
  }
  if (draft.controlledRegion.length === 0) {
    return 'Pick at least one cell for this Warden to govern.'
  }
  if (draft.controlledRegion.length > MAX_REGION_CELLS) {
    return `A region holds at most ${MAX_REGION_CELLS} cells.`
  }
  if (
    outOfRange(draft.actionBudget, MAX_BUDGET) ||
    outOfRange(draft.budgetRenewalPerChapter, MAX_BUDGET) ||
    outOfRange(draft.impactCeilingPerChapter, MAX_IMPACT_CEILING)
  ) {
    return 'Budgets and the impact ceiling run from 0 to 100.'
  }
  return null
}

function outOfRange(value: number, max: number): boolean {
  return !Number.isInteger(value) || value < 0 || value > max
}

export function parseCharterList(value: unknown): CharterList {
  if (!isRecord(value) || value.schema !== CHARTER_LIST_SCHEMA || !Array.isArray(value.wardens)) {
    throw new Error('Unsupported charter schema.')
  }

  return {
    schema: CHARTER_LIST_SCHEMA,
    worldId: text(value.worldId, 40),
    version: count(value.version),
    gridWidth: count(value.gridWidth),
    gridHeight: count(value.gridHeight),
    wardens: value.wardens.slice(0, 64).map((entry): Charter => {
      if (!isRecord(entry)) {
        throw new Error('Invalid charter.')
      }
      return {
        wardenId: text(entry.wardenId, 32),
        displayName: text(entry.displayName, MAX_DISPLAY_NAME_LENGTH),
        controlledSpecies: text(entry.controlledSpecies, 40),
        controlledRegion: numbers(entry.controlledRegion, MAX_REGION_CELLS),
        goals: strings(entry.goals, MAX_GOALS),
        goalWeights: numbers(entry.goalWeights, MAX_GOALS),
        taboos: strings(entry.taboos, MAX_TABOOS),
        actionBudget: count(entry.actionBudget),
        budgetRenewalPerChapter: count(entry.budgetRenewalPerChapter),
        impactCeilingPerChapter: count(entry.impactCeilingPerChapter),
        impactUsedThisChapter: count(entry.impactUsedThisChapter),
      }
    }),
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function text(value: unknown, maxLength: number): string {
  if (typeof value !== 'string' || value.length > maxLength) {
    throw new Error('Invalid charter field.')
  }
  return value
}

function count(value: unknown): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    throw new Error('Invalid charter number.')
  }
  return value
}

function numbers(value: unknown, maxLength: number): number[] {
  if (!Array.isArray(value) || value.length > maxLength) {
    throw new Error('Invalid charter array.')
  }
  return value.map(count)
}

function strings(value: unknown, maxLength: number): string[] {
  if (!Array.isArray(value) || value.length > maxLength) {
    throw new Error('Invalid charter array.')
  }
  return value.map((entry) => text(entry, 30))
}
