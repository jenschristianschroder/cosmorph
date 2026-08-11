import { describe, expect, it } from 'vitest'
import { challengeFor, displayNameFrom, parseAuthConfig, randomString } from './entra'

const tenantId = '00000000-0000-0000-0000-0000000000aa'
const clientId = '00000000-0000-0000-0000-0000000000bb'

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
