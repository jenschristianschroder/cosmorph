/**
 * Microsoft Entra ID sign-in for the Observatory: authorization code with PKCE, from a public
 * client. There is no client secret, and there cannot be one — a browser cannot keep it.
 *
 * This is written directly against the endpoints rather than through a sign-in library because the
 * flow is small and adding a dependency here buys nothing the platform does not already provide:
 * the security-critical half is token validation, and that happens server-side against the
 * directory's published signing keys.
 */

const AUTHORITY = 'https://login.microsoftonline.com'
const PENDING_KEY = 'cosmorph.signin.pending'
const SESSION_KEY = 'cosmorph.signin.session'
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

export interface AuthConfig {
  readonly tenantId: string
  readonly clientId: string
  readonly scope: string
}

export interface Session {
  /** Bearer token for the mutation surface. Never persisted anywhere a later tab could read it. */
  readonly accessToken: string
  readonly expiresAtMs: number
  /** Display only. The API decides who the caller is from the token, never from this. */
  readonly account: string | null
}

/**
 * Reads the non-secret client configuration the API publishes. A response without an auth block
 * means sign-in is not configured, which is the ordinary local-development state.
 */
export function parseAuthConfig(value: unknown): AuthConfig | null {
  if (typeof value !== 'object' || value === null) {
    return null
  }

  const auth = (value as { auth?: unknown }).auth
  if (typeof auth !== 'object' || auth === null) {
    return null
  }

  const { tenantId, clientId, scope } = auth as Record<string, unknown>
  if (typeof tenantId !== 'string' || !GUID.test(tenantId)) {
    return null
  }
  if (typeof clientId !== 'string' || !GUID.test(clientId)) {
    return null
  }
  if (typeof scope !== 'string' || scope.length === 0) {
    return null
  }

  return { tenantId, clientId, scope }
}

export async function fetchAuthConfig(signal: AbortSignal): Promise<AuthConfig | null> {
  const response = await fetch('/api/config', { signal, headers: { Accept: 'application/json' } })
  if (!response.ok) {
    throw new Error(`Request failed with status ${response.status}.`)
  }
  return parseAuthConfig((await response.json()) as unknown)
}

/** Must match a redirect URI registered on the application, so it is the origin and nothing else. */
export function redirectUri(): string {
  return `${window.location.origin}/`
}

/** Sends the browser to the directory. Nothing secret leaves the page: only a hashed challenge. */
export async function beginSignIn(config: AuthConfig): Promise<void> {
  const verifier = randomString(64)
  const state = randomString(32)
  sessionStorage.setItem(PENDING_KEY, JSON.stringify({ verifier, state }))

  const url = new URL(`${AUTHORITY}/${config.tenantId}/oauth2/v2.0/authorize`)
  url.searchParams.set('client_id', config.clientId)
  url.searchParams.set('response_type', 'code')
  url.searchParams.set('redirect_uri', redirectUri())
  url.searchParams.set('response_mode', 'query')

  // No offline_access: a refresh token would have to be kept in browser storage, and an hour of
  // access is enough to create a world. An expired session simply signs in again.
  url.searchParams.set('scope', `openid profile ${config.scope}`)
  url.searchParams.set('state', state)
  url.searchParams.set('code_challenge', await challengeFor(verifier))
  url.searchParams.set('code_challenge_method', 'S256')

  window.location.assign(url.toString())
}

/**
 * Completes a redirect back from the directory. Returns null when this load is not a sign-in
 * response, so it is safe to call on every start-up.
 */
export async function completeSignIn(config: AuthConfig): Promise<Session | null> {
  const params = new URLSearchParams(window.location.search)
  const code = params.get('code')
  const error = params.get('error')
  if (code === null && error === null) {
    return null
  }

  const pending = readPending()
  sessionStorage.removeItem(PENDING_KEY)
  stripQuery()

  if (error !== null) {
    throw new Error('Sign-in did not complete.')
  }

  // A response that does not match the request this tab started is discarded rather than redeemed.
  if (pending === null || code === null || pending.state !== params.get('state')) {
    throw new Error('Sign-in could not be verified.')
  }

  const body = new URLSearchParams({
    client_id: config.clientId,
    grant_type: 'authorization_code',
    code,
    redirect_uri: redirectUri(),
    code_verifier: pending.verifier,
    scope: config.scope,
  })

  const response = await fetch(`${AUTHORITY}/${config.tenantId}/oauth2/v2.0/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body,
  })
  if (!response.ok) {
    throw new Error('Sign-in could not be completed.')
  }

  const token = (await response.json()) as Record<string, unknown>
  const accessToken = token['access_token']
  const expiresIn = token['expires_in']
  if (typeof accessToken !== 'string' || typeof expiresIn !== 'number') {
    throw new Error('Sign-in returned an unusable response.')
  }

  const session: Session = {
    accessToken,
    expiresAtMs: Date.now() + expiresIn * 1000,
    account: displayNameFrom(token['id_token']),
  }
  sessionStorage.setItem(SESSION_KEY, JSON.stringify(session))
  return session
}

/** Returns the stored session, or null when there is none or it has expired. */
export function readSession(): Session | null {
  const raw = sessionStorage.getItem(SESSION_KEY)
  if (raw === null) {
    return null
  }

  let parsed: unknown
  try {
    parsed = JSON.parse(raw)
  } catch {
    sessionStorage.removeItem(SESSION_KEY)
    return null
  }

  const { accessToken, expiresAtMs, account } = (parsed ?? {}) as Record<string, unknown>
  if (typeof accessToken !== 'string' || typeof expiresAtMs !== 'number') {
    sessionStorage.removeItem(SESSION_KEY)
    return null
  }

  // A minute of headroom, so a token cannot expire between the check and the request.
  if (expiresAtMs - 60_000 <= Date.now()) {
    sessionStorage.removeItem(SESSION_KEY)
    return null
  }

  return { accessToken, expiresAtMs, account: typeof account === 'string' ? account : null }
}

export function clearSession(): void {
  sessionStorage.removeItem(SESSION_KEY)
  sessionStorage.removeItem(PENDING_KEY)
}

function readPending(): { verifier: string; state: string } | null {
  const raw = sessionStorage.getItem(PENDING_KEY)
  if (raw === null) {
    return null
  }

  try {
    const { verifier, state } = JSON.parse(raw) as Record<string, unknown>
    return typeof verifier === 'string' && typeof state === 'string' ? { verifier, state } : null
  } catch {
    return null
  }
}

/** Removes the authorization response from the address bar so a reload cannot replay it. */
function stripQuery(): void {
  window.history.replaceState(null, '', window.location.pathname)
}

/**
 * Reads a display name out of the identity token. Purely cosmetic: the token is not verified here,
 * because nothing in the browser is trusted to decide who the caller is.
 */
export function displayNameFrom(idToken: unknown): string | null {
  if (typeof idToken !== 'string') {
    return null
  }

  const payload = idToken.split('.')[1]
  if (payload === undefined) {
    return null
  }

  try {
    const claims = JSON.parse(decodeBase64Url(payload)) as Record<string, unknown>
    const name = claims['name'] ?? claims['preferred_username']
    return typeof name === 'string' ? name : null
  } catch {
    return null
  }
}

/**
 * Decodes an unpadded base64url segment as UTF-8. `atob` alone returns one character per byte, which
 * turns every name outside ASCII into mojibake — Schrøder arrives as SchrÃ¸der.
 */
function decodeBase64Url(segment: string): string {
  const base64 = segment.replace(/-/g, '+').replace(/_/g, '/')
  const padded = base64.padEnd(base64.length + ((4 - (base64.length % 4)) % 4), '=')
  const binary = atob(padded)
  return new TextDecoder().decode(Uint8Array.from(binary, (character) => character.charCodeAt(0)))
}

export function randomString(length: number): string {
  const bytes = new Uint8Array(length)
  crypto.getRandomValues(bytes)
  return base64Url(bytes).slice(0, length)
}

export async function challengeFor(verifier: string): Promise<string> {
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(verifier))
  return base64Url(new Uint8Array(digest))
}

function base64Url(bytes: Uint8Array): string {
  let binary = ''
  for (const byte of bytes) {
    binary += String.fromCharCode(byte)
  }
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
}
