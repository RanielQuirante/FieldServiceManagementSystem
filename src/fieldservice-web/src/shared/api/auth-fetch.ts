export interface SessionAccess {
  currentAccessToken(): string | null
  renewAccessToken(): Promise<string | null>
}

export const correlationIdHeader = 'X-Correlation-ID'

export const csrfHeader = 'X-FieldService-Csrf'

const anonymousPaths: ReadonlySet<string> = new Set([
  '/api/auth/sign-in',
  '/api/auth/refresh',
  '/api/auth/sign-out',
])

const csrfPaths: ReadonlySet<string> = new Set(['/api/auth/refresh', '/api/auth/sign-out'])

let sessionAccess: SessionAccess | null = null

export function connectSessionAccess(access: SessionAccess | null): void {
  sessionAccess = access
}

function isOwnApi(url: URL): boolean {
  return url.origin === window.location.origin && url.pathname.startsWith('/api/')
}

function send(request: Request, accessToken: string | null): Promise<Response> {
  request.headers.set(correlationIdHeader, crypto.randomUUID())
  if (accessToken === null) {
    request.headers.delete('Authorization')
  } else {
    request.headers.set('Authorization', `Bearer ${accessToken}`)
  }
  return fetch(request)
}

async function renewedToken(
  access: SessionAccess,
  sentToken: string | null,
): Promise<string | null> {
  const current = access.currentAccessToken()
  if (current !== null && current !== sentToken) {
    return current
  }
  return access.renewAccessToken()
}

export async function authFetch(request: Request): Promise<Response> {
  const url = new URL(request.url)
  if (!isOwnApi(url)) {
    return fetch(request)
  }
  if (csrfPaths.has(url.pathname)) {
    request.headers.set(csrfHeader, '1')
  }
  if (anonymousPaths.has(url.pathname)) {
    return send(request, null)
  }
  const access = sessionAccess
  if (access === null) {
    return send(request, null)
  }
  const sentToken = access.currentAccessToken()
  const replay = request.clone()
  const response = await send(request, sentToken)
  if (response.status !== 401) {
    return response
  }
  const token = await renewedToken(access, sentToken)
  if (token === null) {
    return response
  }
  return send(replay, token)
}
