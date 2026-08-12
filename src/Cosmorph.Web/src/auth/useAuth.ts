import { useCallback, useEffect, useState } from 'react'
import {
  beginSignIn,
  clearSession,
  completeSignIn,
  fetchAuthConfig,
  hasSignInResponse,
  readSession,
  type AuthConfig,
  type Session,
} from './entra'

export interface Auth {
  /** False until the API has been asked whether sign-in exists, and whenever it does not. */
  readonly available: boolean
  readonly account: string | null
  readonly signedIn: boolean
  /** True while a redirect back from the directory is being redeemed. */
  readonly completing: boolean
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

  // Read before the first paint, so a page that came back from the directory never renders a plain
  // "Sign in" button. Clicking that button starts a second round trip and abandons the code that
  // was already waiting, which is why signing in used to need two attempts.
  const [completing, setCompleting] = useState(hasSignInResponse)

  useEffect(() => {
    const controller = new AbortController()
    let cancelled = false

    // Redeeming the code and reading the configuration are independent: the pending request already
    // carries the tenant and client, so redemption does not wait on a container that may be cold.
    //
    // Redemption is deliberately not cancelled with the effect. A code can only be exchanged once,
    // so wherever the answer arrives it has to be kept rather than discarded.
    const redeem = async (): Promise<void> => {
      try {
        setSession((await completeSignIn()) ?? readSession())
      } catch (failure) {
        setError(failure instanceof Error ? failure.message : 'Sign-in did not complete.')
        setSession(readSession())
      } finally {
        setCompleting(false)
      }
    }

    void redeem()
    void fetchAuthConfig(controller.signal)
      .then((loaded) => {
        if (!cancelled) {
          setConfig(loaded)
        }
      })
      .catch(() => {
        if (!cancelled) {
          setConfig(null)
        }
      })

    return () => {
      cancelled = true
      controller.abort()
    }
  }, [])

  // A session that has lapsed has to leave the header with it. Otherwise the page goes on showing a
  // name and a "Sign out" button while every mutation fails, which is exactly the state that reads
  // as a broken button rather than as an expired hour.
  useEffect(() => {
    if (session === null) {
      return
    }

    const delay = Math.max(0, session.expiresAtMs - 60_000 - Date.now())
    const timer = window.setTimeout(() => {
      setSession((current) => (readSession() === null ? null : current))
    }, delay)
    return () => window.clearTimeout(timer)
  }, [session])

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

  const token = useCallback(() => {
    const current = readSession()
    if (current === null) {
      // Whatever state says, there is nothing left to send. Fall back to the signed-out header so
      // the only offer on screen is the one that helps.
      setSession(null)
      return null
    }
    return current.accessToken
  }, [])

  return {
    available: config !== null,
    account: session?.account ?? null,
    signedIn: session !== null,
    completing,
    error,
    signIn,
    signOut,
    token,
  }
}
