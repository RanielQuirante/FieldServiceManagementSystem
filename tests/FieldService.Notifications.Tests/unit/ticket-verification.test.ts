import assert from 'node:assert/strict'
import { randomBytes, randomUUID } from 'node:crypto'
import { describe, test } from 'node:test'
import {
  createTicketMiddleware,
  type HandshakeSocket,
} from '../../../src/FieldService.Notifications/src/features/admin-connections/admin-connections.ts'
import {
  createTicketVerifier,
  TicketRejection,
  ticketVerificationOptions,
  type TicketVerifier,
} from '../../../src/FieldService.Notifications/src/features/admin-connections/ticket-verifier.ts'
import {
  createUsedTicketIds,
  usedTicketIdCapacity,
  type UsedTicketIds,
} from '../../../src/FieldService.Notifications/src/features/admin-connections/used-ticket-ids.ts'
import { createLogger } from '../../../src/FieldService.Notifications/src/logging.ts'
import { createFakeClock, type FakeClock } from '../support/clock.ts'
import { createMemoryLog, type LogEntry, type MemoryLog } from '../support/memory-log.ts'
import {
  epochSeconds,
  mintTicket,
  ticketAudience,
  ticketIssuer,
  unsignedTicket,
  type TicketClaim,
  type TicketOverrides,
} from '../support/tickets.ts'

interface Harness {
  readonly key: Uint8Array
  readonly clock: FakeClock
  readonly usedTicketIds: UsedTicketIds
  readonly verifier: TicketVerifier
  ticket(overrides?: TicketOverrides): Promise<string>
}

function createHarness(): Harness {
  const key = randomBytes(32)
  const clock = createFakeClock()
  const usedTicketIds = createUsedTicketIds(clock)
  const verifier = createTicketVerifier({
    key,
    issuer: ticketIssuer,
    audience: ticketAudience,
    clock,
    usedTicketIds,
  })
  return {
    key,
    clock,
    usedTicketIds,
    verifier,
    ticket: (overrides = {}) => mintTicket(key, clock.now(), overrides),
  }
}

async function rejectionOf(promise: Promise<unknown>): Promise<TicketRejection> {
  let caught: unknown
  try {
    await promise
  } catch (error) {
    caught = error
  }
  assert.ok(caught instanceof TicketRejection, 'expected a TicketRejection')
  return caught
}

function fillUsedTicketIds(usedTicketIds: UsedTicketIds, issuedAtSeconds: number): string[] {
  const ticketIds = Array.from({ length: usedTicketIdCapacity }, () => randomUUID())
  for (const ticketId of ticketIds) {
    assert.equal(usedTicketIds.add(ticketId, issuedAtSeconds), 'accepted')
  }
  return ticketIds
}

describe('ticket verification options', () => {
  test('match R35: HS256, 60 s, 5 s tolerance and the six required claims', () => {
    assert.deepEqual(ticketVerificationOptions, {
      algorithms: ['HS256'],
      maxTokenAge: '60s',
      clockTolerance: 5,
      requiredClaims: ['sub', 'role', 'jti', 'iat', 'nbf', 'exp'],
    })
  })
})

describe('ticket verification accepts', () => {
  test('a valid Administrator ticket and returns its subject and id', async () => {
    const harness = createHarness()
    const subject = randomUUID()
    const ticketId = randomUUID()

    const verified = await harness.verifier.verify(await harness.ticket({ subject, ticketId }))

    assert.equal(verified.userId, subject)
    assert.equal(verified.ticketId, ticketId)
    assert.equal(verified.claims.role, 'Administrator')
  })

  test('a ticket within 5 s past its exp', async () => {
    const harness = createHarness()
    const ticket = await harness.ticket()
    harness.clock.advance(64_000)

    await assert.doesNotReject(harness.verifier.verify(ticket))
  })

  test('a ticket issued up to 5 s ahead of the service clock', async () => {
    const harness = createHarness()
    const issuedAt = epochSeconds(harness.clock.now()) + 5

    await assert.doesNotReject(harness.verifier.verify(await harness.ticket({ issuedAt })))
  })
})

describe('ticket verification refuses with ticket_missing', () => {
  for (const ticket of [undefined, null, '', 42, { value: 'x' }, ['x']]) {
    test(`a ticket of ${JSON.stringify(ticket) ?? 'undefined'}`, async () => {
      const rejection = await rejectionOf(createHarness().verifier.verify(ticket))

      assert.equal(rejection.code, 'ticket_missing')
      assert.equal(rejection.reason, 'missing')
    })
  }
})

describe('ticket verification refuses with ticket_invalid', () => {
  const claimCases: readonly [string, (nowSeconds: number) => TicketOverrides, string][] = [
    ['a wrong audience', () => ({ audience: 'fieldservice-api' }), 'claims'],
    ['a wrong issuer', () => ({ issuer: 'someone-else' }), 'claims'],
    ['an nbf more than 5 s ahead', (nowSeconds) => ({ notBefore: nowSeconds + 6 }), 'claims'],
    ['a sub that is not a uuid', () => ({ subject: 'not-a-uuid' }), 'subject'],
    ['a sub that is not a string', () => ({ subject: 1049 }), 'subject'],
  ]

  for (const [name, overridesAt, reason] of claimCases) {
    test(`a ticket with ${name}`, async () => {
      const harness = createHarness()
      const overrides = overridesAt(epochSeconds(harness.clock.now()))

      const rejection = await rejectionOf(harness.verifier.verify(await harness.ticket(overrides)))

      assert.equal(rejection.code, 'ticket_invalid')
      assert.equal(rejection.reason, reason)
    })
  }

  for (const claim of ['sub', 'role', 'jti', 'iat', 'nbf', 'exp'] satisfies TicketClaim[]) {
    test(`a ticket without ${claim}`, async () => {
      const harness = createHarness()

      const rejection = await rejectionOf(
        harness.verifier.verify(await harness.ticket({ omit: [claim] })),
      )

      assert.equal(rejection.code, 'ticket_invalid')
    })
  }

  test('a ticket with alg none', async () => {
    const harness = createHarness()

    const rejection = await rejectionOf(
      harness.verifier.verify(unsignedTicket(harness.clock.now())),
    )

    assert.equal(rejection.code, 'ticket_invalid')
    assert.equal(rejection.reason, 'algorithm')
  })

  for (const algorithm of ['HS384', 'HS512'] as const) {
    test(`a ticket signed with ${algorithm} under the right key`, async () => {
      const harness = createHarness()

      const rejection = await rejectionOf(
        harness.verifier.verify(await harness.ticket({ algorithm })),
      )

      assert.equal(rejection.code, 'ticket_invalid')
      assert.equal(rejection.reason, 'algorithm')
    })
  }

  test('a token signed with another key, such as an API access token', async () => {
    const harness = createHarness()
    const accessToken = await mintTicket(randomBytes(32), harness.clock.now(), {
      audience: 'fieldservice-api',
    })

    const rejection = await rejectionOf(harness.verifier.verify(accessToken))

    assert.equal(rejection.code, 'ticket_invalid')
    assert.equal(rejection.reason, 'signature')
  })

  test('a ticket more than 5 s past its exp', async () => {
    const harness = createHarness()
    const ticket = await harness.ticket()
    harness.clock.advance(66_000)

    const rejection = await rejectionOf(harness.verifier.verify(ticket))

    assert.equal(rejection.code, 'ticket_invalid')
    assert.equal(rejection.reason, 'expired')
  })

  test('a ticket whose exp lies 120 s after its iat, once it is 66 s old', async () => {
    const harness = createHarness()
    const issuedAt = epochSeconds(harness.clock.now())
    const ticket = await harness.ticket({ issuedAt, expiresAt: issuedAt + 120 })
    harness.clock.advance(66_000)

    const rejection = await rejectionOf(harness.verifier.verify(ticket))

    assert.equal(rejection.code, 'ticket_invalid')
    assert.equal(rejection.reason, 'expired')
  })

  test('a reused jti', async () => {
    const harness = createHarness()
    const ticket = await harness.ticket()
    await harness.verifier.verify(ticket)

    const rejection = await rejectionOf(harness.verifier.verify(ticket))

    assert.equal(rejection.code, 'ticket_invalid')
    assert.equal(rejection.reason, 'reused')
  })

  for (const ticket of ['not-a-token', 'a.b.c', '   ', 'eyJhbGciOiJIUzI1NiJ9..']) {
    test(`a malformed ticket ${JSON.stringify(ticket)}`, async () => {
      const rejection = await rejectionOf(createHarness().verifier.verify(ticket))

      assert.equal(rejection.code, 'ticket_invalid')
    })
  }

  for (const ticketId of [42, '', 'x'.repeat(129)]) {
    test(`a jti of ${JSON.stringify(ticketId).slice(0, 20)}`, async () => {
      const harness = createHarness()

      const rejection = await rejectionOf(
        harness.verifier.verify(await harness.ticket({ ticketId })),
      )

      assert.equal(rejection.code, 'ticket_invalid')
    })
  }
})

describe('ticket verification refuses with forbidden', () => {
  for (const role of ['Technician', 'administrator', ['Administrator'], 7]) {
    test(`a verified ticket whose role is ${JSON.stringify(role)}`, async () => {
      const harness = createHarness()
      const subject = randomUUID()

      const rejection = await rejectionOf(
        harness.verifier.verify(await harness.ticket({ role, subject })),
      )

      assert.equal(rejection.code, 'forbidden')
      assert.equal(rejection.reason, 'role')
      assert.equal(rejection.userId, subject)
    })
  }
})

describe('single-use ticket ids', () => {
  test('only a ticket that passed every check enters the cache', async () => {
    const harness = createHarness()
    const ticketId = randomUUID()

    await rejectionOf(
      harness.verifier.verify(await harness.ticket({ ticketId, role: 'Technician' })),
    )
    await rejectionOf(
      harness.verifier.verify(await mintTicket(randomBytes(32), harness.clock.now(), { ticketId })),
    )

    await assert.doesNotReject(harness.verifier.verify(await harness.ticket({ ticketId })))
  })

  test('keeps a verified id for 120 s', () => {
    const clock = createFakeClock()
    const usedTicketIds = createUsedTicketIds(clock)
    const ticketId = randomUUID()
    const issuedAt = epochSeconds(clock.now())

    assert.equal(usedTicketIds.add(ticketId, issuedAt), 'accepted')
    clock.advance(119_999)
    assert.equal(usedTicketIds.add(ticketId, issuedAt), 'reused')
    clock.advance(1)
    assert.equal(usedTicketIds.add(ticketId, issuedAt), 'accepted')
  })

  test('with the cache full of entries younger than 65 s, refuses and evicts nothing', async () => {
    const harness = createHarness()
    const issuedAt = epochSeconds(harness.clock.now())
    const cached = fillUsedTicketIds(harness.usedTicketIds, issuedAt)
    harness.clock.advance(65_000)

    const rejection = await rejectionOf(harness.verifier.verify(await harness.ticket()))

    assert.equal(rejection.code, 'ticket_invalid')
    assert.equal(rejection.reason, 'ticket-cache-full')
    assert.equal(harness.usedTicketIds.add(cached[0] ?? '', issuedAt), 'reused')
    assert.equal(harness.usedTicketIds.add(cached.at(-1) ?? '', issuedAt), 'reused')
  })

  test('once the oldest entries are past 65 s, accepts a new ticket in their place', async () => {
    const harness = createHarness()
    const issuedAt = epochSeconds(harness.clock.now())
    const cached = fillUsedTicketIds(harness.usedTicketIds, issuedAt)
    harness.clock.advance(66_000)

    const fresh = await harness.ticket()
    await assert.doesNotReject(harness.verifier.verify(fresh))

    const freshRejection = await rejectionOf(harness.verifier.verify(fresh))
    assert.equal(freshRejection.reason, 'reused')
    assert.equal(harness.usedTicketIds.add(cached[1] ?? '', issuedAt), 'reused')
    assert.equal(harness.usedTicketIds.add(cached[0] ?? '', issuedAt), 'accepted')
  })

  test('never evicts an entry whose ticket can still verify, even past 65 s', () => {
    const clock = createFakeClock()
    const usedTicketIds = createUsedTicketIds(clock)
    const issuedAheadBy5 = epochSeconds(clock.now()) + 5
    fillUsedTicketIds(usedTicketIds, issuedAheadBy5)

    clock.advance(66_000)
    assert.equal(usedTicketIds.add(randomUUID(), epochSeconds(clock.now())), 'full')

    clock.advance(5_000)
    assert.equal(usedTicketIds.add(randomUUID(), epochSeconds(clock.now())), 'accepted')
  })
})

describe('ticket middleware', () => {
  interface MiddlewareHarness extends Harness {
    readonly logs: MemoryLog
    run(auth: unknown): Promise<{ socket: HandshakeSocket; error: unknown }>
  }

  function createMiddlewareHarness(): MiddlewareHarness {
    const harness = createHarness()
    const logs = createMemoryLog()
    const logger = createLogger(
      { logLevel: 'debug', logDir: 'unused', logPretty: false },
      logs.destination,
    )
    const middleware = createTicketMiddleware(harness.verifier, logger, harness.clock)
    return {
      ...harness,
      logs,
      run: (auth) =>
        new Promise((resolve) => {
          const socket: HandshakeSocket = {
            id: `socket-${randomUUID()}`,
            handshake: { auth },
            data: { userId: '', connectedAt: 0 },
          }
          middleware(socket, (error) => {
            resolve({ socket, error })
          })
        }),
    }
  }

  test('stores userId and connectedAt for an accepted ticket', async () => {
    const harness = createMiddlewareHarness()
    const subject = randomUUID()

    const { socket, error } = await harness.run({ ticket: await harness.ticket({ subject }) })

    assert.equal(error, undefined)
    assert.deepEqual(socket.data, { userId: subject, connectedAt: harness.clock.now() })
  })

  for (const [name, auth] of [
    ['no auth object', undefined],
    ['an empty auth object', {}],
    ['a null ticket', { ticket: null }],
  ] as const) {
    test(`refuses ${name} with ticket_missing and logs socket.rejected`, async () => {
      const harness = createMiddlewareHarness()

      const { socket, error } = await harness.run(auth)

      assert.ok(error instanceof Error)
      assert.equal(error.message, 'ticket_missing')
      assert.deepEqual((error as Error & { data?: unknown }).data, { code: 'ticket_missing' })
      const entry = harness.logs.find('socket.rejected')
      assert.ok(entry !== undefined)
      assert.equal(harness.logs.levelOf(entry), 'warn')
      assert.equal(entry.code, 'ticket_missing')
      assert.equal(entry.socketId, socket.id)
    })
  }

  test('refuses a full cache with ticket_invalid and logs a Warning without the ticket', async () => {
    const harness = createMiddlewareHarness()
    fillUsedTicketIds(harness.usedTicketIds, epochSeconds(harness.clock.now()))
    const ticket = await harness.ticket()

    const { error } = await harness.run({ ticket })

    assert.ok(error instanceof Error)
    assert.deepEqual((error as Error & { data?: unknown }).data, { code: 'ticket_invalid' })
    const entry = harness.logs.find('socket.rejected')
    assert.ok(entry !== undefined)
    assert.equal(harness.logs.levelOf(entry), 'warn')
    assert.equal(entry.code, 'ticket_invalid')
    assert.equal(entry.reason, 'ticket-cache-full')
    assert.ok(harness.logs.lines.every((line) => !line.includes(ticket)))
  })

  function runWithFailingVerifier(
    failure: unknown,
    auth: unknown,
  ): Promise<{ socket: HandshakeSocket; error: unknown; logs: MemoryLog }> {
    const logs = createMemoryLog()
    const logger = createLogger(
      { logLevel: 'debug', logDir: 'unused', logPretty: false },
      logs.destination,
    )
    const verifier: TicketVerifier = { verify: () => Promise.reject(failure) }
    const middleware = createTicketMiddleware(verifier, logger, createFakeClock())
    const socket: HandshakeSocket = {
      id: `socket-${randomUUID()}`,
      handshake: { auth },
      data: { userId: '', connectedAt: 0 },
    }
    return new Promise((resolve) => {
      middleware(socket, (error) => {
        resolve({ socket, error, logs })
      })
    })
  }

  function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value)
  }

  function onlyVerificationErrorLine(logs: MemoryLog, socketId: string): LogEntry {
    const entries = logs.all('socket.rejected')
    assert.equal(entries.length, 1, 'exactly one socket.rejected line')
    const [entry] = entries
    assert.ok(entry !== undefined)
    assert.equal(entry.level, 50)
    assert.equal(logs.levelOf(entry), 'error')
    assert.equal(entry.code, 'ticket_invalid')
    assert.equal(entry.reason, 'verification-error')
    assert.equal(entry.socketId, socketId)
    return entry
  }

  test('refuses with ticket_invalid and logs an unexpected verifier Error with its stack', async () => {
    const ticket = `ticket-sentinel-${randomUUID()}`
    const failureMessage = `verifier failure ${randomUUID()}`

    const { socket, error, logs } = await runWithFailingVerifier(new Error(failureMessage), {
      ticket,
    })

    assert.ok(error instanceof Error)
    assert.equal(error.message, 'ticket_invalid')
    assert.deepEqual((error as Error & { data?: unknown }).data, { code: 'ticket_invalid' })
    const entry = onlyVerificationErrorLine(logs, socket.id)
    const err = entry.err
    assert.ok(isRecord(err), 'the line carries the error')
    assert.deepEqual(Object.keys(err).sort(), ['message', 'stack', 'type'])
    assert.equal(err.type, 'Error')
    assert.equal(err.message, failureMessage)
    assert.ok(String(err.stack).startsWith(`Error: ${failureMessage}\n`))
    assert.ok(logs.lines.every((line) => !line.includes(ticket)))
  })

  test('logs an unexpected verifier Error carrying a nested ticket with its type, message and stack only', async () => {
    const ticket = `ticket-sentinel-${randomUUID()}`
    const nestedTicket = `nested-ticket-sentinel-${randomUUID()}`
    const failure = Object.assign(new Error('verifier failure'), {
      handshake: { auth: { ticket: nestedTicket } },
    })

    const { socket, error, logs } = await runWithFailingVerifier(failure, { ticket })

    assert.ok(error instanceof Error)
    assert.equal(error.message, 'ticket_invalid')
    const entry = onlyVerificationErrorLine(logs, socket.id)
    const err = entry.err
    assert.ok(isRecord(err), 'the line carries the error')
    assert.deepEqual(Object.keys(err).sort(), ['message', 'stack', 'type'])
    assert.equal(err.message, 'verifier failure')
    for (const sentinel of [ticket, nestedTicket]) {
      assert.ok(logs.lines.every((line) => !line.includes(sentinel)))
    }
  })

  test('refuses with ticket_invalid and logs an unexpected non-Error by its type only', async () => {
    const ticket = `ticket-sentinel-${randomUUID()}`
    const valueSentinel = `value-sentinel-${randomUUID()}`

    const { socket, error, logs } = await runWithFailingVerifier(
      { handshake: { auth: { ticket: valueSentinel } } },
      { ticket },
    )

    assert.ok(error instanceof Error)
    assert.equal(error.message, 'ticket_invalid')
    const entry = onlyVerificationErrorLine(logs, socket.id)
    assert.equal(entry.errType, 'object')
    assert.equal(entry.err, undefined)
    for (const sentinel of [ticket, valueSentinel]) {
      assert.ok(logs.lines.every((line) => !line.includes(sentinel)))
    }
  })

  test('refuses a Technician ticket with forbidden', async () => {
    const harness = createMiddlewareHarness()
    const subject = randomUUID()

    const { error } = await harness.run({
      ticket: await harness.ticket({ role: 'Technician', subject }),
    })

    assert.ok(error instanceof Error)
    assert.equal(error.message, 'forbidden')
    const entry = harness.logs.find('socket.rejected')
    assert.equal(entry?.code, 'forbidden')
    assert.equal(entry?.userId, subject)
  })
})
