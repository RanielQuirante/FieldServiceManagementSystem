import { errors, jwtVerify, type JWTPayload } from 'jose'
import { z } from 'zod'
import type { Clock } from '../../shared/clock.ts'
import type { UsedTicketIds } from './used-ticket-ids.ts'

export type TicketRejectionCode = 'ticket_missing' | 'ticket_invalid' | 'forbidden'

export type TicketRejectionReason =
  | 'missing'
  | 'signature'
  | 'expired'
  | 'claims'
  | 'algorithm'
  | 'malformed'
  | 'subject'
  | 'ticket-id'
  | 'role'
  | 'reused'
  | 'ticket-cache-full'

export interface TicketRejectionDetails {
  readonly userId?: string
  readonly ticketId?: string
}

export class TicketRejection extends Error {
  readonly code: TicketRejectionCode
  readonly reason: TicketRejectionReason
  readonly userId: string | undefined
  readonly ticketId: string | undefined

  constructor(
    code: TicketRejectionCode,
    reason: TicketRejectionReason,
    details: TicketRejectionDetails = {},
  ) {
    super(code)
    this.name = 'TicketRejection'
    this.code = code
    this.reason = reason
    this.userId = details.userId
    this.ticketId = details.ticketId
  }
}

export interface VerifiedTicket {
  readonly userId: string
  readonly ticketId: string
  readonly claims: JWTPayload
}

export interface TicketVerifierOptions {
  readonly key: Uint8Array
  readonly issuer: string
  readonly audience: string
  readonly clock: Clock
  readonly usedTicketIds: UsedTicketIds
}

export interface TicketVerifier {
  verify(ticket: unknown): Promise<VerifiedTicket>
}

export const administratorRole = 'Administrator'

export const ticketVerificationOptions = {
  algorithms: ['HS256'],
  maxTokenAge: '60s',
  clockTolerance: 5,
  requiredClaims: ['sub', 'role', 'jti', 'iat', 'nbf', 'exp'],
} as const

const maximumTicketIdLength = 128
const subjectSchema = z.uuid()

function rejectionFor(error: unknown): TicketRejection {
  if (error instanceof errors.JWTExpired) {
    return new TicketRejection('ticket_invalid', 'expired')
  }
  if (error instanceof errors.JWSSignatureVerificationFailed) {
    return new TicketRejection('ticket_invalid', 'signature')
  }
  if (error instanceof errors.JOSEAlgNotAllowed) {
    return new TicketRejection('ticket_invalid', 'algorithm')
  }
  if (error instanceof errors.JWTClaimValidationFailed) {
    return new TicketRejection('ticket_invalid', 'claims')
  }
  if (error instanceof errors.JOSEError) {
    return new TicketRejection('ticket_invalid', 'malformed')
  }
  throw error
}

export function createTicketVerifier(options: TicketVerifierOptions): TicketVerifier {
  const { key, issuer, audience, clock, usedTicketIds } = options

  async function verifiedClaims(ticket: string): Promise<JWTPayload> {
    try {
      const { payload } = await jwtVerify(ticket, key, {
        algorithms: [...ticketVerificationOptions.algorithms],
        issuer,
        audience,
        maxTokenAge: ticketVerificationOptions.maxTokenAge,
        clockTolerance: ticketVerificationOptions.clockTolerance,
        requiredClaims: [...ticketVerificationOptions.requiredClaims],
        currentDate: new Date(clock.now()),
      })
      return payload
    } catch (error) {
      throw rejectionFor(error)
    }
  }

  return {
    verify: async (ticket) => {
      if (typeof ticket !== 'string' || ticket === '') {
        throw new TicketRejection('ticket_missing', 'missing')
      }

      const claims = await verifiedClaims(ticket)
      const subject = subjectSchema.safeParse(claims.sub)
      if (!subject.success) {
        throw new TicketRejection('ticket_invalid', 'subject')
      }
      const userId = subject.data
      const ticketId = claims.jti
      if (
        typeof ticketId !== 'string' ||
        ticketId === '' ||
        ticketId.length > maximumTicketIdLength
      ) {
        throw new TicketRejection('ticket_invalid', 'ticket-id', { userId })
      }
      if (claims.role !== administratorRole) {
        throw new TicketRejection('forbidden', 'role', { userId, ticketId })
      }
      if (typeof claims.iat !== 'number') {
        throw new TicketRejection('ticket_invalid', 'claims', { userId, ticketId })
      }

      const outcome = usedTicketIds.add(ticketId, claims.iat)
      if (outcome === 'reused') {
        throw new TicketRejection('ticket_invalid', 'reused', { userId, ticketId })
      }
      if (outcome === 'full') {
        throw new TicketRejection('ticket_invalid', 'ticket-cache-full', { userId, ticketId })
      }
      return { userId, ticketId, claims }
    },
  }
}
