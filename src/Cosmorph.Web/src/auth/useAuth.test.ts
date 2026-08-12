import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, renderHook, waitFor } from '@testing-library/react'
import { useAuth } from './useAuth'

const tenantId = '00000000-0000-0000-0000-0000000000aa'
const clientId = '00000000-0000-0000-0000-0000000000bb'
const config = { tenantId, clientId, scope: 'api://x/World.Write' }
const PENDING_KEY = 'cosmorph.signin.pending'
const SESSION_KEY = 'cosmorph.signin.session'

describe('useAuth', () => {
  beforeEach(() => {
    sessionStorage.clear()
    window.history.replaceState(null, '', '/')
  })

  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
    window.history.replaceState(null, '', '/')
  })

  it('says it is completing a sign-in while nothing has answered yet', () => {
    // The reported failure: the container was cold, /api/config took seconds, and the page in the
    // meantime offered "Sign in" again — which starts a second round trip and abandons the code
    // that had just come back. Nothing may present that offer until redemption has finished.
    sessionStorage.setItem(
      PENDING_KEY,
      JSON.stringify({ verifier: 'a-verifier', state: 'state-1', config }),
    )
    window.history.replaceState(null, '', '/?code=the-code&state=state-1')
    vi.stubGlobal('fetch', () => new Promise(() => {}))

    const { result } = renderHook(() => useAuth())

    expect(result.current.completing).toBe(true)
    expect(result.current.signedIn).toBe(false)
  })

  it('is not completing anything on an ordinary page load', async () => {
    stubConfigOnly()

    const { result } = renderHook(() => useAuth())

    expect(result.current.completing).toBe(false)
    await waitFor(() => expect(result.current.available).toBe(true))
  })

  it('starts signed out when the stored session has already expired', async () => {
    // Within a minute of expiry the token is treated as gone, so the header must not claim a
    // session it cannot back with a bearer token.
    storeSession(Date.now() + 1000)
    stubConfigOnly()

    const { result } = renderHook(() => useAuth())

    await waitFor(() => expect(result.current.available).toBe(true))
    expect(result.current.signedIn).toBe(false)
    expect(result.current.account).toBeNull()
    await act(async () => {
      expect(result.current.token()).toBeNull()
    })
  })

  it('drops the account from the header the moment the token behind it is gone', async () => {
    storeSession(Date.now() + 30 * 60_000)
    stubConfigOnly()

    const { result } = renderHook(() => useAuth())

    await waitFor(() => expect(result.current.signedIn).toBe(true))
    expect(result.current.account).toBe('Ada Lovelace')

    sessionStorage.removeItem(SESSION_KEY)
    await act(async () => {
      expect(result.current.token()).toBeNull()
    })

    expect(result.current.signedIn).toBe(false)
    expect(result.current.account).toBeNull()
  })

  it('reports no sign-in at all when the API does not publish any', async () => {
    vi.stubGlobal('fetch', () =>
      Promise.resolve({ ok: true, json: () => Promise.resolve({ auth: null }) }),
    )

    const { result } = renderHook(() => useAuth())

    await waitFor(() => expect(result.current.completing).toBe(false))
    expect(result.current.available).toBe(false)
  })
})

function storeSession(expiresAtMs: number): void {
  sessionStorage.setItem(
    SESSION_KEY,
    JSON.stringify({ accessToken: 'an-access-token', expiresAtMs, account: 'Ada Lovelace' }),
  )
}

function stubConfigOnly(): void {
  vi.stubGlobal('fetch', () =>
    Promise.resolve({ ok: true, json: () => Promise.resolve({ auth: config }) }),
  )
}
