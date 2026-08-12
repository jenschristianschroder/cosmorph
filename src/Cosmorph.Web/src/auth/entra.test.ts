import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  challengeFor,
  completeSignIn,
  displayNameFrom,
  hasSignInResponse,
  parseAuthConfig,
  randomString,
} from './entra'

const tenantId = '00000000-0000-0000-0000-0000000000aa'
const clientId = '00000000-0000-0000-0000-0000000000bb'
const config = { tenantId, clientId, scope: 'api://x/World.Write' }
const PENDING_KEY = 'cosmorph.signin.pending'

describe('parseAuthConfig', () => {
  it('accepts the configuration the API publishes', () => {
    const config = parseAuthConfig({ auth: { tenantId, clientId, scope: 'api://x/World.Write' } })

    expect(config).toEqual({ tenantId, clientId, scope: 'api://x/World.Write' })
  })

  it('reports no sign-in when the API omits the auth block', () => {
    // This is the ordinary local-development response, not a failure.
    expect(parseAuthConfig({})).toBeNull()
    expect(parseAuthConfig({ auth: null })).toBeNull()
  })

  it.each([
    ['a non-guid tenant', { tenantId: 'contoso.onmicrosoft.com', clientId, scope: 's' }],
    ['a non-guid client', { tenantId, clientId: 'cosmorph', scope: 's' }],
    ['an empty scope', { tenantId, clientId, scope: '' }],
    ['a missing scope', { tenantId, clientId }],
  ])('refuses %s rather than redirecting somewhere unintended', (_label, auth) => {
    expect(parseAuthConfig({ auth })).toBeNull()
  })

  it('refuses a response that is not an object', () => {
    expect(parseAuthConfig(null)).toBeNull()
    expect(parseAuthConfig('auth')).toBeNull()
  })
})

describe('displayNameFrom', () => {
  it('reads a name out of an identity token without verifying it', () => {
    expect(displayNameFrom(tokenFor({ name: 'Ada Lovelace' }))).toBe('Ada Lovelace')
  })

  it('reads a name that is not ASCII', () => {
    // atob alone returns one character per byte, which would render this as Jens SchrÃ¸der.
    expect(displayNameFrom(tokenFor({ name: 'Jens Schrøder' }))).toBe('Jens Schrøder')
    expect(displayNameFrom(tokenFor({ name: '大明' }))).toBe('大明')
  })

  it('falls back to the sign-in name when the token carries no display name', () => {
    expect(displayNameFrom(tokenFor({ preferred_username: 'ada@example.com' }))).toBe(
      'ada@example.com',
    )
  })

  it('returns null for anything unreadable, because it is only cosmetic', () => {
    expect(displayNameFrom(undefined)).toBeNull()
    expect(displayNameFrom('not-a-token')).toBeNull()
    expect(displayNameFrom('header.$$$.signature')).toBeNull()
  })
})

/** Builds an unsigned token whose payload is base64url-encoded UTF-8, as a real one is. */
function tokenFor(claims: Record<string, string>): string {
  const utf8 = new TextEncoder().encode(JSON.stringify(claims))
  const binary = String.fromCharCode(...utf8)
  const payload = btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
  return `header.${payload}.signature`
}

describe('the PKCE challenge', () => {
  it('is the base64url SHA-256 of the verifier, with no padding', async () => {
    // The known digest of "abc", so this asserts the encoding against a published value rather
    // than restating whatever the implementation happens to produce.
    expect(await challengeFor('abc')).toBe('ungWv48Bz-pBQUDeXa4iI7ADYaOWF3qctBD_YfIAFa0')
  })

  it('produces a verifier long enough to be worth hashing', () => {
    const verifier = randomString(64)

    expect(verifier).toHaveLength(64)
    expect(verifier).toMatch(/^[A-Za-z0-9_-]+$/)
    expect(randomString(64)).not.toBe(verifier)
  })
})

describe('hasSignInResponse', () => {
  afterEach(() => window.history.replaceState(null, '', '/'))

  it('is true only on the way back from the directory', () => {
    expect(hasSignInResponse()).toBe(false)

    window.history.replaceState(null, '', '/?code=abc&state=xyz')
    expect(hasSignInResponse()).toBe(true)

    window.history.replaceState(null, '', '/?error=access_denied')
    expect(hasSignInResponse()).toBe(true)
  })
})

describe('completeSignIn', () => {
  beforeEach(() => {
    sessionStorage.clear()
    window.history.replaceState(null, '', '/')
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    window.history.replaceState(null, '', '/')
  })

  it('redeems the code with the configuration stored when sign-in began', async () => {
    // Nothing here reads /api/config. That is the point: the container can take seconds to answer
    // when it is cold, and the code must not sit unredeemed while a "Sign in" button invites a
    // second round trip that would throw it away.
    startedSignIn('state-1')
    window.history.replaceState(null, '', '/?code=the-code&state=state-1')
    const requests = captureTokenRequest()

    const session = await completeSignIn()

    expect(session?.accessToken).toBe('an-access-token')
    expect(session?.account).toBe('Ada Lovelace')
    expect(requests).toHaveLength(1)
    expect(requests[0]?.url).toBe(
      `https://login.microsoftonline.com/${tenantId}/oauth2/v2.0/token`,
    )
    expect(requests[0]?.body).toContain('code=the-code')
    expect(requests[0]?.body).toContain('code_verifier=a-verifier')
    // The address bar is cleared so a reload cannot replay the code.
    expect(window.location.search).toBe('')
  })

  it('returns null on an ordinary page load, so start-up can always call it', async () => {
    expect(await completeSignIn()).toBeNull()
  })

  it('refuses a response that does not match the request this tab started', async () => {
    startedSignIn('state-1')
    window.history.replaceState(null, '', '/?code=the-code&state=somewhere-else')

    await expect(completeSignIn()).rejects.toThrow('Sign-in could not be verified.')
  })

  it('refuses a response with no pending request at all', async () => {
    window.history.replaceState(null, '', '/?code=the-code&state=state-1')

    await expect(completeSignIn()).rejects.toThrow('Sign-in could not be verified.')
  })
})

/** Puts the browser in the state `beginSignIn` leaves behind before it navigates away. */
function startedSignIn(state: string): void {
  sessionStorage.setItem(
    PENDING_KEY,
    JSON.stringify({ verifier: 'a-verifier', state, config }),
  )
}

function captureTokenRequest(): { url: string; body: string }[] {
  const requests: { url: string; body: string }[] = []
  vi.stubGlobal('fetch', (url: unknown, init: { body?: unknown }) => {
    requests.push({ url: String(url), body: String(init.body) })
    return Promise.resolve({
      ok: true,
      json: () =>
        Promise.resolve({
          access_token: 'an-access-token',
          expires_in: 3600,
          id_token: tokenFor({ name: 'Ada Lovelace' }),
        }),
    })
  })
  return requests
}
