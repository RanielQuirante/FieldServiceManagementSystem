import { randomUUID } from 'node:crypto'
import { SignJWT } from 'jose'

export const ticketIssuer = 'fieldservice-api'
export const ticketAudience = 'fieldservice-notifications'
export const ticketLifetimeSeconds = 60

export type TicketClaim = 'iss' | 'aud' | 'sub' | 'role' | 'jti' | 'iat' | 'nbf' | 'exp'

export interface TicketOverrides {
  readonly issuer?: string
  readonly audience?: string
  readonly subject?: unknown
  readonly role?: unknown
  readonly ticketId?: unknown
  readonly issuedAt?: number
  readonly notBefore?: number
  readonly expiresAt?: number
  readonly algorithm?: 'HS256' | 'HS384' | 'HS512'
  readonly omit?: readonly TicketClaim[]
}

export function ticketKeyBytes(base64Key: string): Uint8Array {
  return Buffer.from(base64Key, 'base64')
}

export function epochSeconds(epochMilliseconds: number): number {
  return Math.floor(epochMilliseconds / 1000)
}

export function ticketClaims(
  nowMs: number,
  overrides: TicketOverrides = {},
): Record<string, unknown> {
  const issuedAt = overrides.issuedAt ?? epochSeconds(nowMs)
  const claims: Record<string, unknown> = {
    iss: overrides.issuer ?? ticketIssuer,
    aud: overrides.audience ?? ticketAudience,
    sub: 'subject' in overrides ? overrides.subject : randomUUID(),
    role: 'role' in overrides ? overrides.role : 'Administrator',
    jti: 'ticketId' in overrides ? overrides.ticketId : randomUUID(),
    iat: issuedAt,
    nbf: overrides.notBefore ?? issuedAt,
    exp: overrides.expiresAt ?? issuedAt + ticketLifetimeSeconds,
  }
  for (const claim of overrides.omit ?? []) {
    delete claims[claim]
  }
  return claims
}

export async function mintTicket(
  key: Uint8Array,
  nowMs: number,
  overrides: TicketOverrides = {},
): Promise<string> {
  return new SignJWT(ticketClaims(nowMs, overrides))
    .setProtectedHeader({ alg: overrides.algorithm ?? 'HS256', typ: 'JWT' })
    .sign(key)
}

export function unsignedTicket(nowMs: number, overrides: TicketOverrides = {}): string {
  const encode = (value: unknown): string =>
    Buffer.from(JSON.stringify(value), 'utf8').toString('base64url')
  return `${encode({ alg: 'none', typ: 'JWT' })}.${encode(ticketClaims(nowMs, overrides))}.`
}

export function changeCharacterAt(text: string, index: number): string {
  const current = text[index]
  const replacement = current === 'A' ? 'B' : 'A'
  return `${text.slice(0, index)}${replacement}${text.slice(index + 1)}`
}
