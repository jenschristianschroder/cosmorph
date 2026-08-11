import { useCallback, useState } from 'react'
import { createWorld, isValidWorldId } from '../api/client'
import type { Auth } from './useAuth'

export interface SignInPanelProps {
  readonly auth: Auth
  /** Called with the new world's identifier once the API has accepted it. */
  readonly onWorldCreated: (worldId: string) => void
}

/**
 * Sign-in control and, once signed in, the only mutation the Observatory offers: creating a world.
 * Renders nothing at all when the API reports that sign-in is not configured, which is the ordinary
 * local-development state.
 *
 * The form is behind a toggle so the header stays one line while nobody is creating anything.
 */
export function SignInPanel({ auth, onWorldCreated }: SignInPanelProps): React.ReactElement | null {
  const [open, setOpen] = useState(false)
  const [worldId, setWorldId] = useState('')
  const [name, setName] = useState('')
  const [seed, setSeed] = useState('')
  const [isPublic, setIsPublic] = useState(true)
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<string | null>(null)

  const { signedIn, token } = auth
  const onSubmit = useCallback(
    async (event: React.FormEvent): Promise<void> => {
      event.preventDefault()
      setFailure(null)

      const accessToken = token()
      if (accessToken === null) {
        setFailure('Your session has expired. Sign in again.')
        return
      }

      setBusy(true)
      try {
        const parsedSeed = seed.trim().length === 0 ? randomSeed() : Number(seed)
        await createWorld({ worldId, name, seed: parsedSeed, isPublic }, accessToken)
        setWorldId('')
        setName('')
        setSeed('')
        setOpen(false)
        onWorldCreated(worldId)
      } catch (error) {
        setFailure(error instanceof Error ? error.message : 'The world could not be created.')
      } finally {
        setBusy(false)
      }
    },
    [isPublic, name, onWorldCreated, seed, token, worldId],
  )

  if (!auth.available) {
    return null
  }

  return (
    <div className="signin">
      {signedIn ? (
        <>
          <span className="signin-account">{auth.account ?? 'Signed in'}</span>
          <button type="button" onClick={() => setOpen((current) => !current)} aria-expanded={open}>
            {open ? 'Cancel' : 'New world'}
          </button>
          <button type="button" onClick={auth.signOut}>
            Sign out
          </button>
        </>
      ) : (
        <button type="button" onClick={auth.signIn}>
          Sign in
        </button>
      )}

      {auth.error !== null && (
        <p className="badge badge-warning" role="alert">
          {auth.error}
        </p>
      )}

      {signedIn && open && (
        <form className="new-world" aria-label="New world" onSubmit={(event) => void onSubmit(event)}>
          <label>
            Identifier
            <input
              value={worldId}
              onChange={(event) => setWorldId(event.target.value)}
              placeholder="quiet-meridian"
              required
            />
          </label>
          <label>
            Name
            <input
              value={name}
              onChange={(event) => setName(event.target.value)}
              maxLength={60}
              required
            />
          </label>
          <label>
            Seed
            <input
              value={seed}
              onChange={(event) => setSeed(event.target.value)}
              inputMode="numeric"
              placeholder="random"
            />
          </label>
          <label className="checkbox">
            <input
              type="checkbox"
              checked={isPublic}
              onChange={(event) => setIsPublic(event.target.checked)}
            />
            Anyone may watch it
          </label>
          <button type="submit" disabled={busy || !isValidWorldId(worldId)}>
            {busy ? 'Creating…' : 'Create world'}
          </button>
          {failure !== null && (
            <p className="badge badge-warning" role="alert">
              {failure}
            </p>
          )}
          <p className="explanation">
            The identifier is lower-case letters, digits and hyphens, three to forty characters, and
            appears in the address. Only you can configure this world&rsquo;s Wardens.
          </p>
        </form>
      )}
    </div>
  )
}

/** A seed the browser picks, kept inside the range that survives a JSON round trip. */
function randomSeed(): number {
  return Math.floor(Math.random() * 2 ** 48)
}
