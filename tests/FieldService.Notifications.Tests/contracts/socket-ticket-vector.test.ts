import assert from 'node:assert/strict'
import { randomBytes } from 'node:crypto'
import { describe, test } from 'node:test'
import {
  createTicketVerifier,
  TicketRejection,
  type TicketVerifier,
} from '../../../src/FieldService.Notifications/src/features/admin-connections/ticket-verifier.ts'
import { createUsedTicketIds } from '../../../src/FieldService.Notifications/src/features/admin-connections/used-ticket-ids.ts'
import { loadConfig } from '../../../src/FieldService.Notifications/src/config.ts'
import { createFakeClock, type FakeClock } from '../support/clock.ts'
import { createRequiredEnvironment, readSocketTicketVector } from '../support/fixtures.ts'
import { changeCharacterAt, ticketKeyBytes } from '../support/tickets.ts'

const vector = readSocketTicketVector()
const issuedAt = vector.claims.iat
assert.equal(typeof issuedAt, 'number')
const issuedAtMs = Number(issuedAt) * 1000
const defaults = loadConfig(createRequiredEnvironment())

function verifierAt(
  epochMilliseconds: number,
  key = ticketKeyBytes(vector.key),
): {
  readonly verifier: TicketVerifier
  readonly clock: FakeClock
} {
  const clock = createFakeClock(epochMilliseconds)
  return {
    clock,
    verifier: createTicketVerifier({
      key,
      issuer: defaults.socketTicketIssuer,
      audience: defaults.socketTicketAudience,
      clock,
      usedTicketIds: createUsedTicketIds(clock),
    }),
  }
}

async function rejectionOf(promise: Promise<unknown>): Promise<TicketRejection> {
  let caught: unknown
  try {
    await promise
  } catch (error) {
    caught = error
  }
  assert.ok(caught instanceof TicketRejection, 'expected the ticket to be refused')
  return caught
}

function segment(index: number): string {
  const part = vector.token.split('.')[index]
  assert.ok(part !== undefined)
  return part
}

describe('socket ticket test vector', () => {
  test('holds the key, the claims and the token in the documented order', () => {
    const name = 'socket-ticket-test-vector.json'

    assert.deepEqual(Object.keys(vector), ['key', 'claims', 'token'], name)
    assert.deepEqual(Object.keys(vector.claims), [
      'iss',
      'aud',
      'sub',
      'role',
      'jti',
      'iat',
      'nbf',
      'exp',
    ])
    assert.equal(ticketKeyBytes(vector.key).length, 32)
  })

  test('its token carries the HS256 header and exactly the expected claims', () => {
    assert.deepEqual(JSON.parse(Buffer.from(segment(0), 'base64url').toString('utf8')), {
      alg: 'HS256',
      typ: 'JWT',
    })
    assert.deepEqual(
      JSON.parse(Buffer.from(segment(1), 'base64url').toString('utf8')),
      vector.claims,
    )
  })

  test('the claims are those the service requires of a ticket', () => {
    assert.equal(vector.claims.iss, defaults.socketTicketIssuer)
    assert.equal(vector.claims.aud, defaults.socketTicketAudience)
    assert.equal(vector.claims.role, 'Administrator')
    assert.equal(vector.claims.nbf, vector.claims.iat)
    assert.equal(vector.claims.exp, Number(vector.claims.iat) + 60)
  })

  test('the verifier accepts the token with the clock at iat, and its payload equals the claims', async () => {
    const { verifier } = verifierAt(issuedAtMs)

    const verified = await verifier.verify(vector.token)

    assert.deepEqual(verified.claims, vector.claims)
    assert.equal(verified.userId, vector.claims.sub)
    assert.equal(verified.ticketId, vector.claims.jti)
  })

  test('the verifier still accepts the token 64 s after iat', async () => {
    const { verifier } = verifierAt(issuedAtMs + 64_000)

    await assert.doesNotReject(verifier.verify(vector.token))
  })

  for (const [name, index, offset] of [
    ['header', 0, 10],
    ['payload', 1, 40],
    ['signature', 2, 20],
  ] as const) {
    test(`the verifier rejects the token after a one-character change in its ${name}`, async () => {
      const parts = vector.token.split('.')
      parts[index] = changeCharacterAt(segment(index), offset)
      const { verifier } = verifierAt(issuedAtMs)

      const rejection = await rejectionOf(verifier.verify(parts.join('.')))

      assert.equal(rejection.code, 'ticket_invalid')
    })
  }

  test('the verifier rejects the token under another key', async () => {
    const { verifier } = verifierAt(issuedAtMs, randomBytes(32))

    const rejection = await rejectionOf(verifier.verify(vector.token))

    assert.equal(rejection.code, 'ticket_invalid')
    assert.equal(rejection.reason, 'signature')
  })

  test('the verifier rejects the token with the clock more than 65 s past iat', async () => {
    const { verifier } = verifierAt(issuedAtMs + 66_000)

    const rejection = await rejectionOf(verifier.verify(vector.token))

    assert.equal(rejection.code, 'ticket_invalid')
    assert.equal(rejection.reason, 'expired')
  })

  test('the verifier rejects the token with the clock more than 5 s before iat', async () => {
    const { verifier } = verifierAt(issuedAtMs - 6_000)

    const rejection = await rejectionOf(verifier.verify(vector.token))

    assert.equal(rejection.code, 'ticket_invalid')
  })

  test('the verifier accepts the token once and then refuses its jti', async () => {
    const { verifier } = verifierAt(issuedAtMs)
    await verifier.verify(vector.token)

    const rejection = await rejectionOf(verifier.verify(vector.token))

    assert.equal(rejection.reason, 'reused')
  })
})
