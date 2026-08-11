import { describe, expect, it, vi, beforeAll, afterAll } from 'vitest'
import { webcrypto } from 'node:crypto'
import { challengeFor, displayNameFrom, parseAuthConfig, randomString } from './entra'

const tenantId = '00000000-0000-0000-0000-0000000000aa'
const clientId = '00000000-0000-0000-0000-0000000000bb'

// jsdom's Web Crypto is not guaranteed to expose subtle, so the tests run against the platform's
// own implementation. This is the same API the browser provides; nothing here is a fake.
beforeAll(() => vi.stubGlobal('crypto', webcrypto))
afterAll(() => vi.unstubAllGlobals())

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
    const payload = btoa(JSON.stringify({ name: 'Ada Lovelace' }))

    expect(displayNameFrom(`header.${payload}.signature`)).toBe('Ada Lovelace')
  })

  it('returns null for anything unreadable, because it is only cosmetic', () => {
    expect(displayNameFrom(undefined)).toBeNull()
    expect(displayNameFrom('not-a-token')).toBeNull()
    expect(displayNameFrom('header.$$$.signature')).toBeNull()
  })
})

describe('the PKCE challenge', () => {
  it('is the base64url SHA-256 of the verifier, with no padding', async () => {
    // The known digest of "abc", so this asserts the encoding rather than restating it.
    expect(await challengeFor('abc')).toBe('ungWv48Bz-pBQUDeXa4iI7ADYaOWF3qctBD_YfIAFa0')
  })

  it('produces a verifier long enough to be worth hashing', () => {
    const verifier = randomString(64)

    expect(verifier).toHaveLength(64)
    expect(verifier).toMatch(/^[A-Za-z0-9_-]+$/)
    expect(randomString(64)).not.toBe(verifier)
  })
})
