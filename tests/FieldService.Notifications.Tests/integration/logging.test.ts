import assert from 'node:assert/strict'
import { randomUUID } from 'node:crypto'
import { createServer } from 'node:http'
import { after, before, beforeEach, describe, test } from 'node:test'
import { runInNewContext } from 'node:vm'
import { createLogger, failureFields } from '../../../src/FieldService.Notifications/src/logging.ts'
import {
  createRouter,
  type RouteHandler,
} from '../../../src/FieldService.Notifications/src/shared/http/router.ts'
import { createWebhookSecret, freshEnvelope } from '../support/fixtures.ts'
import { createMemoryLog, type LogEntry, type MemoryLog } from '../support/memory-log.ts'
import { boundPort } from '../support/ports.ts'
import {
  closeClients,
  connectAdministrator,
  connected,
  disconnected,
  foreignOrigin,
  jobEventsPath,
  openClient,
  postEnvelope,
  postJobEvent,
  received,
  refused,
  startServer,
  ticketFor,
  type ClientSocket,
  type TestServer,
} from '../support/server.ts'
import { signatureHeaderOf, unixSeconds, webhookSecretPrefix } from '../support/signing.ts'
import { epochSeconds } from '../support/tickets.ts'

function only(server: TestServer, message: string, eventId: string): LogEntry {
  const entries = server.logs.all(message).filter((entry) => entry.eventId === eventId)
  assert.equal(entries.length, 1, `exactly one ${message} for ${eventId}`)
  const [entry] = entries
  assert.ok(entry !== undefined)
  return entry
}

async function waitForEntry(
  server: TestServer,
  message: string,
  predicate: (entry: LogEntry) => boolean,
): Promise<LogEntry> {
  const deadline = Date.now() + 3_000
  for (;;) {
    const entry = server.logs.all(message).find(predicate)
    if (entry !== undefined) {
      return entry
    }
    if (Date.now() > deadline) {
      throw new Error(`timed out waiting for ${message}`)
    }
    await new Promise((resolve) => setTimeout(resolve, 10))
  }
}

describe('job-event log entries (R41)', () => {
  let server: TestServer
  let administrator: ClientSocket

  before(async () => {
    server = await startServer()
    administrator = await connectAdministrator(server)
  })

  after(async () => {
    closeClients([administrator])
    await server.close()
  })

  test('a broadcast event logs received and broadcast at info with its identifiers', async () => {
    const { id, envelope, data } = freshEnvelope('job.status-changed')
    const delivery = received(administrator, 'job.status-changed')
    await postEnvelope(server, envelope)
    await delivery

    const receivedEntry = only(server, 'job-event.received', id)
    assert.equal(server.logs.levelOf(receivedEntry), 'info')
    assert.equal(receivedEntry.type, 'job.status-changed')
    assert.equal(receivedEntry.version, 1)
    assert.equal(receivedEntry.jobId, data.jobId)
    assert.equal(receivedEntry.correlationId, envelope.correlationId)

    const broadcast = only(server, 'job-event.broadcast', id)
    assert.equal(server.logs.levelOf(broadcast), 'info')
    assert.equal(broadcast.type, 'job.status-changed')
    assert.equal(broadcast.jobId, data.jobId)
    assert.equal(broadcast.correlationId, envelope.correlationId)
    assert.equal(broadcast.room, 'administrators')
    assert.equal(broadcast.recipients, 1)
  })

  test('the correlation id logged is the signed body one, never a differing header', async () => {
    const { id, envelope } = freshEnvelope('job.created')
    await postEnvelope(server, envelope, { correlationId: 'header-correlation-id' })

    for (const message of ['job-event.received', 'job-event.broadcast']) {
      assert.equal(only(server, message, id).correlationId, envelope.correlationId)
    }
    assert.ok(server.logs.entries.every((entry) => entry.correlationId !== 'header-correlation-id'))
  })

  test('a duplicate logs job-event.duplicate at info', async () => {
    const { id, envelope } = freshEnvelope('job.assigned')
    await postEnvelope(server, envelope)
    await postEnvelope(server, envelope)

    const duplicate = only(server, 'job-event.duplicate', id)
    assert.equal(server.logs.levelOf(duplicate), 'info')
    assert.equal(duplicate.correlationId, envelope.correlationId)
    assert.equal(
      server.logs.all('job-event.broadcast').filter((entry) => entry.eventId === id).length,
      1,
    )
  })

  test('an unknown type or version logs job-event.ignored at warn', async () => {
    const unknownType = freshEnvelope('job.created')
    unknownType.envelope.type = 'job.archived'
    const unknownVersion = freshEnvelope('job.assigned')
    unknownVersion.envelope.version = 3
    await postEnvelope(server, unknownType.envelope)
    await postEnvelope(server, unknownVersion.envelope)

    const ignoredType = only(server, 'job-event.ignored', unknownType.id)
    assert.equal(server.logs.levelOf(ignoredType), 'warn')
    assert.equal(ignoredType.type, 'job.archived')
    assert.equal(ignoredType.version, 1)
    assert.equal(ignoredType.jobId, undefined)
    const ignoredVersion = only(server, 'job-event.ignored', unknownVersion.id)
    assert.equal(ignoredVersion.type, 'job.assigned')
    assert.equal(ignoredVersion.version, 3)
  })

  test('each rejection logs job-event.rejected at error with its reason and no job-event.received', async () => {
    const cases: readonly [string, (id: string) => Promise<Response>][] = [
      [
        'unsupported-media-type',
        (id) => postJobEvent(server, '{}', { id, contentType: 'text/plain' }),
      ],
      ['too-large', (id) => postJobEvent(server, 'x'.repeat(16_385), { id })],
      ['missing-headers', (id) => postJobEvent(server, '{}', { id, omit: ['webhook-signature'] })],
      [
        'timestamp-out-of-range',
        (id) =>
          postJobEvent(server, '{}', {
            id,
            timestamp: String(Number(unixSeconds(server.clock.now())) - 301),
          }),
      ],
      [
        'signature-mismatch',
        (id) => postJobEvent(server, '{}', { id, secret: createWebhookSecret() }),
      ],
      ['invalid-json', (id) => postJobEvent(server, '{"id":', { id })],
      ['schema', (id) => postJobEvent(server, '{"id":"x"}', { id })],
      ['id-mismatch', (id) => postEnvelope(server, freshEnvelope('job.created').envelope, { id })],
    ]

    for (const [reason, send] of cases) {
      const id = randomUUID()
      const response = await send(id)
      await response.arrayBuffer()

      const entry = only(server, 'job-event.rejected', id)
      assert.equal(server.logs.levelOf(entry), 'error', reason)
      assert.equal(entry.reason, reason)
      assert.equal(entry.status, response.status)
      assert.ok(
        server.logs.all('job-event.received').every((line) => line.eventId !== id),
        `no job-event.received for ${reason}`,
      )
    }
  })

  test('a rejection carries the claimed id and header correlation id only when well formed', async () => {
    const id = randomUUID()
    await (
      await postJobEvent(server, '{}', {
        id,
        secret: createWebhookSecret(),
        correlationId: 'trace-0099',
      })
    ).arrayBuffer()
    await (
      await postJobEvent(server, '{}', {
        id: 'NOT-A-UUID forged=entry',
        secret: createWebhookSecret(),
        headers: { 'x-correlation-id': 'bad correlation id' },
      })
    ).arrayBuffer()

    const wellFormed = only(server, 'job-event.rejected', id)
    assert.equal(wellFormed.correlationId, 'trace-0099')
    const malformed = server.logs
      .all('job-event.rejected')
      .filter((entry) => entry.eventId === undefined && entry.reason === 'signature-mismatch')
    assert.ok(malformed.length >= 1)
    for (const entry of malformed) {
      assert.equal(entry.correlationId, undefined)
    }
  })

  test('routing failures before the content type check log nothing', async () => {
    const before = server.logs.entries.length
    await (await fetch(`${server.baseUrl}${jobEventsPath}`)).arrayBuffer()
    await (await fetch(`${server.baseUrl}/internal/other`, { method: 'POST' })).arrayBuffer()

    assert.equal(server.logs.entries.length, before)
  })
})

describe('socket log entries', () => {
  let server: TestServer

  before(async () => {
    server = await startServer()
  })

  after(async () => {
    await server.close()
  })

  test('socket.connected and socket.disconnected carry socketId, userId, reason and durationMs', async () => {
    const userId = randomUUID()
    const ticket = await ticketFor(server, { subject: userId })
    const socket = openClient(server, { auth: { ticket } })
    await connected(socket)
    const socketId = socket.id
    server.clock.advance(4_000)
    const gone = disconnected(socket)
    socket.disconnect()
    await gone

    const connectedEntry = await waitForEntry(
      server,
      'socket.connected',
      (entry) => entry.socketId === socketId,
    )
    assert.equal(server.logs.levelOf(connectedEntry), 'info')
    assert.equal(connectedEntry.userId, userId)
    const disconnectedEntry = await waitForEntry(
      server,
      'socket.disconnected',
      (entry) => entry.socketId === socketId,
    )
    assert.equal(server.logs.levelOf(disconnectedEntry), 'info')
    assert.equal(disconnectedEntry.userId, userId)
    assert.equal(disconnectedEntry.reason, 'client namespace disconnect')
    assert.equal(disconnectedEntry.durationMs, 4_000)
  })

  test('socket.rejected carries the code, at warn', async () => {
    const cases: readonly [string, Record<string, unknown>][] = [
      ['ticket_missing', {}],
      ['ticket_invalid', { ticket: await ticketFor(server, { audience: 'fieldservice-api' }) }],
      ['forbidden', { ticket: await ticketFor(server, { role: 'Technician' }) }],
    ]
    for (const [code, auth] of cases) {
      const socket = openClient(server, { auth })
      await refused(socket)
      socket.close()

      const entry = await waitForEntry(server, 'socket.rejected', (line) => line.code === code)
      assert.equal(server.logs.levelOf(entry), 'warn')
    }
  })

  test('an Origin refusal logs socket.rejected without a code', async () => {
    const socket = openClient(server, { auth: {}, origin: foreignOrigin })
    await refused(socket)
    socket.close()

    const entry = await waitForEntry(
      server,
      'socket.rejected',
      (line) => line.reason === 'origin-not-allowed',
    )
    assert.equal(entry.code, undefined)
    assert.ok(!JSON.stringify(entry).includes(foreignOrigin))
  })
})

describe('redaction', () => {
  const sensitiveKeys = [
    'auth',
    'ticket',
    'token',
    'password',
    'secret',
    'signature',
    'webhookSecrets',
    'socketTicketKey',
  ]

  for (const key of sensitiveKeys) {
    test(`${key} is written as [redacted] at depth 0 and at depth 1`, () => {
      const logs = createMemoryLog()
      const logger = createLogger(
        { logLevel: 'debug', logDir: 'unused', logPretty: false },
        logs.destination,
      )
      const sentinel = `sentinel-${randomUUID()}`

      logger.info({ [key]: sentinel }, 'depth-0')
      logger.info({ nested: { [key]: sentinel } }, 'depth-1')

      assert.equal(logs.find('depth-0')?.[key], '[redacted]')
      assert.deepEqual(logs.find('depth-1')?.nested, { [key]: '[redacted]' })
      assert.ok(logs.lines.every((line) => !line.includes(sentinel)))
    })
  }

  test('authorization, cookie and webhook-signature headers are redacted', () => {
    const logs = createMemoryLog()
    const logger = createLogger(
      { logLevel: 'debug', logDir: 'unused', logPretty: false },
      logs.destination,
    )
    const sentinel = `sentinel-${randomUUID()}`
    const headers = {
      authorization: sentinel,
      cookie: sentinel,
      'webhook-signature': sentinel,
    }

    logger.info({ headers }, 'headers')
    logger.info({ req: { headers } }, 'request')

    assert.ok(logs.lines.every((line) => !line.includes(sentinel)))
  })
})

describe('log sentinel scan (LOG-20)', () => {
  let server: TestServer
  const sentinels: string[] = []

  before(async () => {
    server = await startServer()
  })

  after(async () => {
    await server.close()
  })

  test('no log line holds a ticket, signature, secret, key, body, Authorization or Cookie value', async () => {
    const authorization = `Bearer authorization-sentinel-${randomUUID()}`
    const cookie = `session=cookie-sentinel-${randomUUID()}`
    const bodySentinel = `body-sentinel-${randomUUID()}`
    const querySentinel = `query-sentinel-${randomUUID()}`
    const headers = { authorization, cookie }
    const secret = server.webhookSecret
    const ticketKey = server.environment.SOCKET_TICKET_KEY ?? ''
    sentinels.push(
      authorization,
      cookie,
      bodySentinel,
      querySentinel,
      secret,
      secret.slice(webhookSecretPrefix.length),
      ticketKey,
    )

    const administrator = await connectAdministrator(server, { headers })
    const signedEvent = freshEnvelope('job.status-changed')
    signedEvent.envelope.note = bodySentinel
    signedEvent.data.siteAccessNotes = bodySentinel
    const timestamp = unixSeconds(server.clock.now())
    const signature = signatureHeaderOf(
      secret,
      signedEvent.id,
      timestamp,
      JSON.stringify(signedEvent.envelope),
    )
    sentinels.push(signature, signature.slice(3), JSON.stringify(signedEvent.envelope))
    const delivery = received(administrator, 'job.status-changed')
    await (
      await postEnvelope(server, signedEvent.envelope, { timestamp, signature, headers })
    ).arrayBuffer()
    const payload = await delivery
    assert.ok(!JSON.stringify(payload).includes(bodySentinel), 'the payload carries no body text')

    const unknownType = freshEnvelope('job.created')
    unknownType.envelope.type = 'job.archived'
    unknownType.envelope.note = bodySentinel
    await (await postEnvelope(server, unknownType.envelope, { headers })).arrayBuffer()
    await (await postEnvelope(server, signedEvent.envelope, { headers })).arrayBuffer()

    const forged = freshEnvelope('job.created')
    forged.envelope.note = bodySentinel
    const forgedSignature = signatureHeaderOf(
      createWebhookSecret(),
      forged.id,
      timestamp,
      JSON.stringify(forged.envelope),
    )
    sentinels.push(forgedSignature, forgedSignature.slice(3))
    await (
      await postEnvelope(server, forged.envelope, { signature: forgedSignature, headers })
    ).arrayBuffer()
    await (await postJobEvent(server, `{"note":"${bodySentinel}"`, { headers })).arrayBuffer()
    await (await postJobEvent(server, `{"note":"${bodySentinel}"}`, { headers })).arrayBuffer()
    await (
      await postJobEvent(server, bodySentinel.repeat(400), { headers, contentType: 'text/plain' })
    ).arrayBuffer()
    await (await postJobEvent(server, bodySentinel.repeat(400), { headers })).arrayBuffer()
    await (
      await fetch(`${server.baseUrl}${jobEventsPath}?token=${querySentinel}`, {
        method: 'POST',
        headers: { ...headers, 'content-type': 'application/json' },
        body: `{"note":"${bodySentinel}"}`,
      })
    ).arrayBuffer()

    const tickets = [
      await ticketFor(server),
      await ticketFor(server, { role: 'Technician' }),
      await ticketFor(server, { audience: 'fieldservice-api' }),
      await ticketFor(server, {
        issuedAt: epochSeconds(server.clock.now()) - 120,
        expiresAt: epochSeconds(server.clock.now()) - 60,
      }),
      `ticket-sentinel-${randomUUID()}`,
    ]
    sentinels.push(...tickets, ...tickets.map((ticket) => ticket.split('.')[2] ?? ticket))
    const accepted = openClient(server, { auth: { ticket: tickets[0] }, headers })
    await connected(accepted)
    for (const ticket of tickets) {
      const socket = openClient(server, { auth: { ticket }, headers })
      await refused(socket)
      socket.close()
    }
    const foreign = openClient(server, {
      auth: { ticket: tickets[1] },
      headers,
      origin: foreignOrigin,
    })
    await refused(foreign)
    foreign.close()
    accepted.close()
    administrator.close()
    await waitForEntry(server, 'socket.disconnected', (entry) => entry.socketId !== undefined)

    assert.ok(server.logs.lines.length > 20, 'the scenario produced log lines to scan')
    for (const sentinel of sentinels) {
      assert.ok(sentinel.length >= 16, 'every sentinel is specific enough to scan for')
      for (const line of server.logs.lines) {
        assert.ok(
          !line.includes(sentinel),
          `a log line holds a sensitive value: ${line.slice(0, 120)}`,
        )
      }
    }
    const jwtShape = /eyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\./
    assert.ok(server.logs.lines.every((line) => !line.includes('whsec_')))
    assert.ok(server.logs.lines.every((line) => !jwtShape.test(line)))
    assert.deepEqual(server.logs.all('request.failed'), [])
  })
})

interface RouterHarness {
  readonly baseUrl: string
  readonly logs: MemoryLog
  close(): Promise<void>
}

async function startRouter(routes: Readonly<Record<string, RouteHandler>>): Promise<RouterHarness> {
  const logs = createMemoryLog()
  const logger = createLogger(
    { logLevel: 'debug', logDir: 'unused', logPretty: false },
    logs.destination,
  )
  const router = createRouter(logger)
  for (const [path, handler] of Object.entries(routes)) {
    router.post(path, handler)
  }
  const httpServer = createServer(router.handle)
  await new Promise<void>((resolve) => {
    httpServer.listen(0, '127.0.0.1', resolve)
  })
  return {
    baseUrl: `http://127.0.0.1:${boundPort(httpServer)}`,
    logs,
    close: () =>
      new Promise<void>((resolve, reject) => {
        httpServer.closeAllConnections()
        httpServer.close((error) => {
          if (error === undefined) {
            resolve()
          } else {
            reject(error)
          }
        })
      }),
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

describe('route handler failures (LOG-12, SEC-W4-07)', () => {
  const failureMessage = `route handler failure ${randomUUID()}`
  const valueSentinel = `value-sentinel-${randomUUID()}`
  const correlationId = 'trace-0412'
  let harness: RouterHarness

  before(async () => {
    harness = await startRouter({
      '/throws': () => {
        throw new Error(failureMessage)
      },
      '/rejects': async () => {
        await Promise.resolve()
        throw new Error(failureMessage)
      },
      '/fails-after-headers': (_request, response) => {
        response.writeHead(200, { 'content-type': 'text/plain' })
        response.write('partial')
        throw new Error(failureMessage)
      },
      '/throws-error-with-properties': () => {
        throw Object.assign(new Error(failureMessage), {
          handshake: { auth: { ticket: valueSentinel } },
        })
      },
      '/throws-string': () => {
        throw valueSentinel
      },
      '/rejects-object': async () => {
        await Promise.resolve()
        throw { handshake: { auth: { ticket: valueSentinel } } }
      },
    })
  })

  beforeEach(() => {
    harness.logs.clear()
  })

  after(async () => {
    await harness.close()
  })

  function sensitiveRequest(path: string): { sentinels: string[]; send: () => Promise<Response> } {
    const authorization = `Bearer authorization-sentinel-${randomUUID()}`
    const cookie = `session=cookie-sentinel-${randomUUID()}`
    const signature = `v1,signature-sentinel-${randomUUID()}`
    const headerValue = `header-sentinel-${randomUUID()}`
    const bodySentinel = `body-sentinel-${randomUUID()}`
    const querySentinel = `query-sentinel-${randomUUID()}`
    return {
      sentinels: [authorization, cookie, signature, headerValue, bodySentinel, querySentinel],
      send: () =>
        fetch(`${harness.baseUrl}${path}?token=${querySentinel}`, {
          method: 'POST',
          headers: {
            authorization,
            cookie,
            'webhook-signature': signature,
            'x-request-note': headerValue,
            'x-correlation-id': correlationId,
            'content-type': 'application/json',
          },
          body: JSON.stringify({ note: bodySentinel }),
        }),
    }
  }

  function onlyFailureLine(sentinels: readonly string[]): LogEntry {
    const failures = harness.logs.all('request.failed')
    assert.equal(failures.length, 1, 'exactly one request.failed line')
    const [entry] = failures
    assert.ok(entry !== undefined)
    assert.equal(entry.level, 50)
    assert.equal(harness.logs.levelOf(entry), 'error')

    assert.equal(harness.logs.lines.length, 1, 'the failure is the only line written')
    for (const sentinel of sentinels) {
      assert.ok(
        harness.logs.lines.every((line) => !line.includes(sentinel)),
        'the line holds no header, query, body or failure value',
      )
    }
    return entry
  }

  function assertOneErrorLine(sentinels: readonly string[]): void {
    const entry = onlyFailureLine(sentinels)
    assert.deepEqual(Object.keys(entry).sort(), ['err', 'level', 'msg', 'time'])

    const err = entry.err
    assert.ok(isRecord(err), 'the line carries the error')
    assert.deepEqual(Object.keys(err).sort(), ['message', 'stack', 'type'])
    assert.equal(err.type, 'Error')
    assert.equal(err.message, failureMessage)
    assert.equal(typeof err.stack, 'string')
    const stack = String(err.stack)
    assert.ok(stack.startsWith(`Error: ${failureMessage}\n`), 'the stack starts with the error')
    assert.match(stack, /\n {4}at .*logging\.test\.ts:\d+:\d+/)
  }

  function assertServerErrorProblem(response: Response, text: string): void {
    assert.equal(response.status, 500)
    assert.equal(response.headers.get('content-type'), 'application/problem+json')
    assert.equal(response.headers.get('x-correlation-id'), correlationId)
    assert.deepEqual(JSON.parse(text), {
      type: 'about:blank',
      title: 'Internal Server Error',
      status: 500,
      code: 'server.error',
      correlationId,
    })
  }

  for (const path of ['/throws', '/rejects']) {
    test(`a handler that ${path.slice(1)} logs request.failed at error with its stack and answers 500`, async () => {
      const { sentinels, send } = sensitiveRequest(path)
      const response = await send()
      const text = await response.text()

      assertServerErrorProblem(response, text)
      assert.ok(!text.includes(failureMessage), 'the response never carries the error')
      assertOneErrorLine(sentinels)
    })
  }

  test('a handler that throws an Error carrying a nested ticket logs only its type, message and stack', async () => {
    const { sentinels, send } = sensitiveRequest('/throws-error-with-properties')
    const response = await send()
    const text = await response.text()

    assertServerErrorProblem(response, text)
    assert.ok(!text.includes(valueSentinel), 'the response never carries the value')
    assertOneErrorLine([...sentinels, valueSentinel])
  })

  test('a handler that fails after sending headers logs request.failed and cuts the response', async () => {
    const { sentinels, send } = sensitiveRequest('/fails-after-headers')

    await assert.rejects(async () => {
      const response = await send()
      await response.text()
    })
    assertOneErrorLine(sentinels)
  })

  const nonErrorCases = [
    ['throws a string', '/throws-string', 'string'],
    ['rejects with an object nesting a ticket', '/rejects-object', 'object'],
  ] as const

  for (const [behaviour, path, errType] of nonErrorCases) {
    test(`a handler that ${behaviour} logs request.failed at error with its type only and answers 500`, async () => {
      const { sentinels, send } = sensitiveRequest(path)
      const response = await send()
      const text = await response.text()

      assertServerErrorProblem(response, text)
      assert.ok(!text.includes(valueSentinel), 'the response never carries the value')
      const entry = onlyFailureLine([...sentinels, valueSentinel])
      assert.deepEqual(Object.keys(entry).sort(), ['errType', 'level', 'msg', 'time'])
      assert.equal(entry.errType, errType)
    })
  }
})

describe('failure values are logged by type only (SEC-W4-07)', () => {
  const sentinel = `value-sentinel-${randomUUID()}`

  function logged(value: unknown): { entry: LogEntry; lines: readonly string[] } {
    const logs = createMemoryLog()
    const logger = createLogger(
      { logLevel: 'debug', logDir: 'unused', logPretty: false },
      logs.destination,
    )
    logger.error(failureFields(value), 'failure-probe')
    const entry = logs.find('failure-probe')
    assert.ok(entry !== undefined)
    return { entry, lines: logs.lines }
  }

  const nonErrors: readonly (readonly [string, unknown, string])[] = [
    ['a string', sentinel, 'string'],
    ['a number', 4012, 'number'],
    ['a bigint', 4012n, 'bigint'],
    ['a boolean', true, 'boolean'],
    ['a symbol', Symbol(sentinel), 'symbol'],
    ['undefined', undefined, 'undefined'],
    ['null', null, 'null'],
    ['an array', [sentinel], 'array'],
    ['an object nesting a ticket', { handshake: { auth: { ticket: sentinel } } }, 'object'],
    [
      'an object shaped like an Error',
      { name: 'Error', message: sentinel, stack: sentinel },
      'object',
    ],
    ['a function', () => sentinel, 'function'],
    [
      'an Error from another realm',
      runInNewContext(`new Error(${JSON.stringify(sentinel)})`),
      'object',
    ],
  ]

  for (const [name, value, errType] of nonErrors) {
    test(`${name} is logged as errType ${errType} and never as given`, () => {
      const { entry, lines } = logged(value)

      assert.deepEqual(Object.keys(entry).sort(), ['errType', 'level', 'msg', 'time'])
      assert.equal(entry.errType, errType)
      assert.ok(lines.every((line) => !line.includes(sentinel)))
    })
  }

  test('an Error and its subclasses keep err with the type, message and stack', () => {
    for (const error of [new Error(sentinel), new TypeError(sentinel), new RangeError(sentinel)]) {
      const fields = failureFields(error)
      assert.ok('err' in fields)
      assert.equal(fields.err, error)

      const { entry } = logged(error)
      const err = entry.err
      assert.ok(isRecord(err), 'the line carries the error')
      assert.deepEqual(Object.keys(err).sort(), ['message', 'stack', 'type'])
      assert.equal(err.type, error.name)
      assert.equal(err.message, sentinel)
      assert.ok(String(err.stack).startsWith(`${error.name}: ${sentinel}\n`))
    }
  })
})

describe('errors are logged with type, message, stack and code only (SEC-W4-07)', () => {
  function loggedErr(fields: Record<string, unknown>): {
    err: Record<string, unknown>
    lines: readonly string[]
  } {
    const logs = createMemoryLog()
    const logger = createLogger(
      { logLevel: 'debug', logDir: 'unused', logPretty: false },
      logs.destination,
    )
    logger.error(fields, 'error-probe')
    const err = logs.find('error-probe')?.err
    assert.ok(isRecord(err), 'the line carries err')
    return { err, lines: logs.lines }
  }

  function assertAbsent(lines: readonly string[], sentinel: string): void {
    assert.ok(
      lines.every((line) => !line.includes(sentinel)),
      'no line holds the sentinel',
    )
  }

  test('an Error carrying a nested ticket on an extra property logs only its type, message and stack', () => {
    const ticket = `ticket-sentinel-${randomUUID()}`
    const error = Object.assign(new Error('carrier'), {
      handshake: { auth: { ticket } },
      detail: `detail-${ticket}`,
    })

    const { err, lines } = loggedErr(failureFields(error))

    assert.deepEqual(Object.keys(err).sort(), ['message', 'stack', 'type'])
    assert.equal(err.type, 'Error')
    assert.equal(err.message, 'carrier')
    assert.ok(String(err.stack).startsWith('Error: carrier\n'))
    assertAbsent(lines, ticket)
  })

  for (const code of ['E_SAMPLE_CODE', 4012]) {
    test(`an Error with the ${typeof code} code ${code} keeps the code`, () => {
      const { err } = loggedErr(failureFields(Object.assign(new Error('coded'), { code })))

      assert.deepEqual(Object.keys(err).sort(), ['code', 'message', 'stack', 'type'])
      assert.equal(err.code, code)
    })
  }

  test('an Error whose code is neither a string nor a number drops the code', () => {
    const ticket = `ticket-sentinel-${randomUUID()}`
    const error = Object.assign(new Error('coded'), { code: { ticket } })

    const { err, lines } = loggedErr(failureFields(error))

    assert.deepEqual(Object.keys(err).sort(), ['message', 'stack', 'type'])
    assertAbsent(lines, ticket)
  })

  test('an object that borrows Error.prototype is serialized through the same four fields', () => {
    const ticket = `ticket-sentinel-${randomUUID()}`
    const borrowed: unknown = Object.setPrototypeOf(
      { message: 'borrowed', code: 'E_BORROWED', handshake: { auth: { ticket } } },
      Error.prototype,
    )
    assert.ok(borrowed instanceof Error)

    const fields = failureFields(borrowed)
    assert.ok('err' in fields, 'it takes the Error path')
    const { err, lines } = loggedErr(fields)

    assert.deepEqual(Object.keys(err).sort(), ['code', 'message', 'stack', 'type'])
    assert.equal(err.type, 'Error')
    assert.equal(err.message, 'borrowed')
    assert.equal(err.code, 'E_BORROWED')
    assert.equal(typeof err.stack, 'string')
    assertAbsent(lines, ticket)
  })

  test('a cause chain is folded into message and stack, and no property of a cause is written', () => {
    const ticket = `ticket-sentinel-${randomUUID()}`
    const inner = Object.assign(new RangeError('inner'), { handshake: { auth: { ticket } } })
    const outer = new Error('outer', { cause: inner })

    const { err, lines } = loggedErr(failureFields(outer))

    assert.deepEqual(Object.keys(err).sort(), ['message', 'stack', 'type'])
    assert.equal(err.message, 'outer: inner')
    const stack = String(err.stack)
    assert.ok(stack.startsWith('Error: outer\n'))
    assert.ok(stack.includes('\ncaused by: RangeError: inner\n'))
    assertAbsent(lines, ticket)
  })

  test('a circular cause chain ends with the circular marker', () => {
    const first = new Error('first')
    const second = new Error('second', { cause: first })
    first.cause = second

    const { err } = loggedErr(failureFields(first))

    assert.equal(err.message, 'first: second: first: ...')
    assert.ok(String(err.stack).endsWith('\ncauses have become circular...'))
  })

  test('a cause that is not an Error is not followed', () => {
    const ticket = `ticket-sentinel-${randomUUID()}`
    const outer = new Error('outer', { cause: { message: ticket, stack: ticket } })

    const { err, lines } = loggedErr(failureFields(outer))

    assert.equal(err.message, 'outer')
    assert.ok(!String(err.stack).includes('caused by'))
    assertAbsent(lines, ticket)
  })

  test('an AggregateError writes none of its inner errors', () => {
    const ticket = `ticket-sentinel-${randomUUID()}`
    const aggregate = new AggregateError([new Error(ticket)], 'aggregate')

    const { err, lines } = loggedErr(failureFields(aggregate))

    assert.deepEqual(Object.keys(err).sort(), ['message', 'stack', 'type'])
    assert.equal(err.type, 'AggregateError')
    assert.equal(err.message, 'aggregate')
    assertAbsent(lines, ticket)
  })

  test('a value that is not an Error, logged under err directly, is written as its type only', () => {
    const ticket = `ticket-sentinel-${randomUUID()}`

    const { err, lines } = loggedErr({ err: { handshake: { auth: { ticket } } } })

    assert.deepEqual(err, { errType: 'object' })
    assertAbsent(lines, ticket)
  })
})
