import type { SessionResponse } from '../auth-api'

export const accessTokenLifetimeMilliseconds = 15 * 60_000

export const demoAdministratorEmail = 'dana.whitfield@fieldservice.example'

export function accessToken(): string {
  return `access-${crypto.randomUUID()}`
}

export function password(): string {
  return `pw-${crypto.randomUUID()}`
}

export function sessionResponse(
  token: string = accessToken(),
  expiresAt: Date = new Date(Date.now() + accessTokenLifetimeMilliseconds),
): SessionResponse {
  return {
    accessToken: token,
    accessTokenExpiresAt: expiresAt.toISOString(),
    user: {
      id: '01a0f6b1-2680-7152-92a5-bb4d3a6df7ca',
      displayName: 'Dana Whitfield',
      role: 'Administrator',
    },
    refreshToken: null,
  }
}

export function sessionResponseBody(token: string): Response {
  return new Response(JSON.stringify(sessionResponse(token)), {
    status: 200,
    headers: { 'Content-Type': 'application/json; charset=utf-8' },
  })
}
