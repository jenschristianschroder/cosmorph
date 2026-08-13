import { useCallback, useEffect, useState } from 'react'
import type { SpeciesTotal } from '../api/dto'
import type { Auth } from '../auth/useAuth'
import {
  adoptWorld,
  describeDraftProblem,
  fetchCharters,
  GOALS,
  isValidWardenId,
  MAX_BUDGET,
  MAX_DISPLAY_NAME_LENGTH,
  MAX_GOALS,
  MAX_IMPACT_CEILING,
  MAX_REGION_CELLS,
  MAX_WEIGHT,
  putCharter,
  TABOOS,
  type Charter,
  type CharterDraft,
  type WardenGoal,
  type WardenTaboo,
} from './charters'

export interface WardenPanelProps {
  readonly auth: Auth
  readonly worldId: string | null
  /** Species present in the world, used to offer only species a Warden could actually look after. */
  readonly species: readonly SpeciesTotal[]
  /** The snapshot version, which is how the panel notices a saved charter has been applied. */
  readonly worldVersion: number | null
  readonly region: readonly number[]
  readonly picking: boolean
  readonly onPickingChange: (picking: boolean) => void
  readonly onRegionChange: (region: readonly number[]) => void
}

interface GoalRow {
  readonly goal: WardenGoal | ''
  readonly weight: number
}

const EMPTY_GOALS: readonly GoalRow[] = [
  { goal: 'Preserve', weight: 50 },
  { goal: '', weight: 50 },
  { goal: '', weight: 50 },
]

/**
 * Configures the Wardens of a world you own. Renders nothing to a signed-out spectator, because
 * there is nothing they could do with it. To somebody signed in the read is still owner-only and
 * answers 404 to anyone else, but silence there reads as a broken page rather than as somebody
 * else's world — so a refusal says so plainly, and offers to adopt a world nobody owns.
 *
 * A save is accepted, not applied — the command is carried out by the tick job — so the panel says
 * the charter is pending and clears that once the world has advanced past the version it saved at.
 */
export function WardenPanel(props: WardenPanelProps): React.ReactElement | null {
  const { auth, worldId, worldVersion, region, onRegionChange, onPickingChange } = props
  const { signedIn, token } = auth

  const [charters, setCharters] = useState<readonly Charter[] | null>(null)
  const [wardenId, setWardenId] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [controlledSpecies, setControlledSpecies] = useState('')
  const [goals, setGoals] = useState<readonly GoalRow[]>(EMPTY_GOALS)
  const [taboos, setTaboos] = useState<readonly WardenTaboo[]>([])
  const [actionBudget, setActionBudget] = useState(40)
  const [renewal, setRenewal] = useState(20)
  const [impactCeiling, setImpactCeiling] = useState(50)
  const [open, setOpen] = useState(false)
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<string | null>(null)

  // Bumped by a successful adoption, which is the one moment the charter read can start succeeding
  // without the world having ticked.
  const [ownershipVersion, setOwnershipVersion] = useState(0)

  // The world version at the moment of the last save. The command is pending until the world has
  // moved past it.
  const [savedAtVersion, setSavedAtVersion] = useState<number | null>(null)

  useEffect(() => {
    if (!worldId || !signedIn) {
      setCharters(null)
      return
    }

    const accessToken = token()
    if (accessToken === null) {
      setCharters(null)
      return
    }

    const controller = new AbortController()
    fetchCharters(worldId, accessToken, controller.signal)
      .then((list) => {
        if (!controller.signal.aborted) {
          setCharters(list.wardens)
        }
      })
      .catch(() => {
        if (!controller.signal.aborted) {
          // A non-owner is answered 404, which is the ordinary case for every world but your own.
          setCharters(null)
        }
      })

    return () => controller.abort()
  }, [worldId, signedIn, token, worldVersion, ownershipVersion])

  const onAdopt = useCallback(async (): Promise<void> => {
    if (!worldId) {
      return
    }
    setFailure(null)

    const accessToken = token()
    if (accessToken === null) {
      setFailure('Your session has expired. Sign in again to adopt this world.')
      return
    }

    setBusy(true)
    try {
      await adoptWorld(worldId, accessToken)
      setOwnershipVersion((version) => version + 1)
    } catch (error) {
      setFailure(error instanceof Error ? error.message : 'This world could not be adopted.')
    } finally {
      setBusy(false)
    }
  }, [token, worldId])

  const pending = savedAtVersion !== null && (worldVersion === null || worldVersion <= savedAtVersion)

  useEffect(() => {
    if (savedAtVersion !== null && worldVersion !== null && worldVersion > savedAtVersion) {
      setSavedAtVersion(null)
    }
  }, [savedAtVersion, worldVersion])

  const edit = useCallback(
    (charter: Charter) => {
      setWardenId(charter.wardenId)
      setDisplayName(charter.displayName)
      setControlledSpecies(charter.controlledSpecies)
      setGoals(
        EMPTY_GOALS.map((fallback, index) => ({
          goal: (charter.goals[index] as WardenGoal | undefined) ?? '',
          weight: charter.goalWeights[index] ?? fallback.weight,
        })),
      )
      setTaboos(charter.taboos.filter(isTaboo))
      setActionBudget(charter.actionBudget)
      setRenewal(charter.budgetRenewalPerChapter)
      setImpactCeiling(charter.impactCeilingPerChapter)
      onRegionChange([...charter.controlledRegion])
      setFailure(null)
      setOpen(true)
    },
    [onRegionChange],
  )

  const onSubmit = useCallback(
    async (event: React.FormEvent): Promise<void> => {
      event.preventDefault()
      setFailure(null)

      if (!worldId) {
        return
      }
      if (!isValidWardenId(wardenId)) {
        setFailure('A Warden identifier is lower-case letters, digits and hyphens, 2 to 32 long.')
        return
      }

      const chosen = goals.filter((row): row is { goal: WardenGoal; weight: number } => row.goal !== '')
      const draft: CharterDraft = {
        displayName: displayName.trim(),
        controlledSpecies,
        controlledRegion: region,
        goals: chosen.map((row) => row.goal),
        goalWeights: chosen.map((row) => row.weight),
        taboos,
        actionBudget,
        budgetRenewalPerChapter: renewal,
        impactCeilingPerChapter: impactCeiling,
      }

      const problem = describeDraftProblem(draft)
      if (problem !== null) {
        setFailure(problem)
        return
      }

      const accessToken = token()
      if (accessToken === null) {
        setFailure('Your session has expired. Sign in again to save the charter.')
        return
      }

      setBusy(true)
      try {
        await putCharter(worldId, wardenId, draft, accessToken)
        setSavedAtVersion(worldVersion ?? 0)
        setOpen(false)
        onPickingChange(false)
      } catch (error) {
        setFailure(error instanceof Error ? error.message : 'The charter could not be saved.')
      } finally {
        setBusy(false)
      }
    },
    [
      actionBudget,
      controlledSpecies,
      displayName,
      goals,
      impactCeiling,
      onPickingChange,
      region,
      renewal,
      taboos,
      token,
      wardenId,
      worldId,
      worldVersion,
    ],
  )

  if (!signedIn) {
    return null
  }

  if (charters === null) {
    return (
      <section className="panel-section wardens" aria-label="Wardens">
        <h2>Wardens</h2>
        <p className="explanation">
          {worldId
            ? 'This world is not yours, so its Wardens cannot be read or configured. A world nobody owns can be adopted.'
            : 'Choose a world to see its Wardens.'}
        </p>
        {failure !== null && (
          <p className="badge badge-warning" role="alert">
            {failure}
          </p>
        )}
        {worldId && (
          <button type="button" disabled={busy} onClick={() => void onAdopt()}>
            {busy ? 'Adopting…' : 'Adopt this world'}
          </button>
        )}
      </section>
    )
  }

  return (
    <section className="panel-section wardens" aria-label="Wardens">
      <h2>Wardens</h2>

      {pending && (
        <p className="badge badge-warning" role="status">
          Saved. The charter takes effect on the next tick.
        </p>
      )}

      {charters.length === 0 ? (
        <p>This world has no Wardens yet.</p>
      ) : (
        <ul className="warden-list">
          {charters.map((charter) => (
            <li key={charter.wardenId}>
              <button type="button" onClick={() => edit(charter)}>
                <span className="warden-name">{charter.displayName}</span>
                <span className="warden-detail">
                  {charter.controlledSpecies} · {charter.controlledRegion.length} cells ·{' '}
                  {charter.goals.join(', ') || 'no goals'}
                </span>
                <span className="warden-detail">
                  budget {charter.actionBudget} · impact used {charter.impactUsedThisChapter} of{' '}
                  {charter.impactCeilingPerChapter}
                </span>
              </button>
            </li>
          ))}
        </ul>
      )}

      <button
        type="button"
        onClick={() => {
          setOpen((current) => !current)
          setFailure(null)
        }}
        aria-expanded={open}
      >
        {open ? 'Cancel' : 'New or edit Warden'}
      </button>

      {failure !== null && (
        <p className="badge badge-warning" role="alert">
          {failure}
        </p>
      )}

      {open && (
        <form className="warden-form" aria-label="Warden charter" onSubmit={(e) => void onSubmit(e)}>
          <label>
            Identifier
            <input
              value={wardenId}
              onChange={(e) => setWardenId(e.target.value)}
              placeholder="grove-keeper"
              required
            />
          </label>
          <label>
            Name
            <input
              value={displayName}
              onChange={(e) => setDisplayName(e.target.value)}
              maxLength={MAX_DISPLAY_NAME_LENGTH}
              required
            />
          </label>
          <label>
            Species
            <select
              value={controlledSpecies}
              onChange={(e) => setControlledSpecies(e.target.value)}
              required
            >
              <option value="">Choose a species</option>
              {props.species.map((entry) => (
                <option key={entry.species} value={entry.species}>
                  {entry.displayName}
                </option>
              ))}
            </select>
          </label>

          <fieldset>
            <legend>Goals</legend>
            {goals.slice(0, MAX_GOALS).map((row, index) => (
              <div className="warden-goal" key={index}>
                <select
                  aria-label={`Goal ${index + 1}`}
                  value={row.goal}
                  onChange={(e) => setGoals(replaceGoal(goals, index, e.target.value as WardenGoal | ''))}
                >
                  <option value="">None</option>
                  {GOALS.map((goal) => (
                    <option key={goal} value={goal}>
                      {goal}
                    </option>
                  ))}
                </select>
                <input
                  type="number"
                  aria-label={`Weight for goal ${index + 1}`}
                  min={1}
                  max={MAX_WEIGHT}
                  value={row.weight}
                  onChange={(e) => setGoals(replaceWeight(goals, index, toInteger(e.target.value, 1)))}
                />
              </div>
            ))}
          </fieldset>

          <fieldset>
            <legend>Taboos</legend>
            {TABOOS.map((taboo) => (
              <label className="checkbox" key={taboo}>
                <input
                  type="checkbox"
                  checked={taboos.includes(taboo)}
                  onChange={(e) =>
                    setTaboos(
                      e.target.checked
                        ? [...taboos, taboo]
                        : taboos.filter((current) => current !== taboo),
                    )
                  }
                />
                {taboo.replace('Never', 'Never ').toLowerCase()}
              </label>
            ))}
          </fieldset>

          <fieldset>
            <legend>Region</legend>
            <p className="explanation">
              {region.length} of {MAX_REGION_CELLS} cells chosen. While picking is on, clicking the
              planet adds or removes a cell instead of opening it.
            </p>
            <button type="button" onClick={() => onPickingChange(!props.picking)}>
              {props.picking ? 'Stop picking cells' : 'Pick cells on the globe'}
            </button>
            <button type="button" onClick={() => onRegionChange([])} disabled={region.length === 0}>
              Clear region
            </button>
          </fieldset>

          <label>
            Action budget
            <input
              type="number"
              min={0}
              max={MAX_BUDGET}
              value={actionBudget}
              onChange={(e) => setActionBudget(toInteger(e.target.value, 0))}
            />
          </label>
          <label>
            Budget renewed each chapter
            <input
              type="number"
              min={0}
              max={MAX_BUDGET}
              value={renewal}
              onChange={(e) => setRenewal(toInteger(e.target.value, 0))}
            />
          </label>
          <label>
            Impact ceiling per chapter
            <input
              type="number"
              min={0}
              max={MAX_IMPACT_CEILING}
              value={impactCeiling}
              onChange={(e) => setImpactCeiling(toInteger(e.target.value, 0))}
            />
          </label>

          <button type="submit" disabled={busy}>
            {busy ? 'Saving…' : 'Save charter'}
          </button>
          <p className="explanation">
            Writing to an identifier no Warden uses creates one. A charter is carried out by the
            simulation, so it takes effect on the next tick rather than at once.
          </p>
        </form>
      )}
    </section>
  )
}

function replaceGoal(
  rows: readonly GoalRow[],
  index: number,
  goal: WardenGoal | '',
): readonly GoalRow[] {
  return rows.map((row, i) => (i === index ? { goal, weight: row.weight } : row))
}

function replaceWeight(rows: readonly GoalRow[], index: number, weight: number): readonly GoalRow[] {
  return rows.map((row, i) => (i === index ? { goal: row.goal, weight } : row))
}

function toInteger(value: string, fallback: number): number {
  const parsed = Number.parseInt(value, 10)
  return Number.isFinite(parsed) ? parsed : fallback
}

function isTaboo(value: string): value is WardenTaboo {
  return (TABOOS as readonly string[]).includes(value)
}
