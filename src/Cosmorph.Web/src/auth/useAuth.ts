import { useCallback, useEffect, useState } from 'react'
import {
  beginSignIn,
  clearSession,
  completeSignIn,
  fetchAuthConfig,
  readSession,
  type AuthConfig,
  type Session,
} from './entra'

export interface Auth {
  /** False until the API has been asked whether sign-in exists, and whenever it does not. */
  readonly available: boolean
  readonly account: string | null
  readonly signedIn: boolean
  readonly error: string | null
  readonly signIn: () => void
  readonly signOut: () => void
  /** The bearer token for a mutation, or null when the session is gone or has expired. */
  readonly token: () => string | null
}

/**
 * Sign-in state for the Observatory. Reads runtime configuration from the API, because the static
 * build is served by the container and cannot be compiled with a directory identifier.
 */
export function useAuth(): Auth {
  const [config, setConfig] = useState<AuthConfig | null>(null)
  const [session, setSession] = useState<Session | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    let cancelled = false

    const start = async (): Promise<void> => {
      const loaded = await fetchAuthConfig(controller.signal)
      if (cancelled || loaded === null) {
        return
      }

      setConfig(loaded)

      // A redirect back from the directory has to be redeemed before anything else looks at the
      // address bar, so the authorization code never survives into a later navigation.
      try {
        setSession((await completeSignIn(loaded)) ?? readSession())
      } catch (failure) {
        setError(failure instanceof Error ? failure.message : 'Sign-in did not complete.')
        setSession(readSession())
      }
    }

    void start().catch(() => {
      if (!cancelled) {
        setConfig(null)
      }
    })

    return () => {
      cancelled = true
      controller.abort()
    }
  }, [])

  const signIn = useCallback(() => {
    if (config === null) {
      return
    }

    setError(null)
    void beginSignIn(config).catch(() => setError('Sign-in could not be started.'))
  }, [config])

  const signOut = useCallback(() => {
    // Local sign-out only: the directory session is left alone, so signing back in does not
    // necessarily prompt. Nothing of this account remains in the page.
    clearSession()
    setSession(null)
    setError(null)
  }, [])

  const token = useCallback(() => readSession()?.accessToken ?? null, [])

  return {
    available: config !== null,
    account: session?.account ?? null,
    signedIn: session !== null,
    error,
    signIn,
    signOut,
    token,
  }
}
