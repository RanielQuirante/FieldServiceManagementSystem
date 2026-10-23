import assert from 'node:assert/strict'
import { randomUUID } from 'node:crypto'
import { after, before, beforeEach, describe, test } from 'node:test'
import {
  createWebhookSecret,
  freshEnvelope,
  readClientFixture,
  readInternalFixtureText,
  type JsonObject,
} from '../support/fixtures.ts'
import {
  closeClients,
  connectAdministrator,
  jobEventsPath,
  outcomeOf,
  postEnvelope,
  postJobEvent,
  problemOf,
  received,
  recordEvents,
  startServer,
  type ClientSocket,
  type RecordedEvent,
  type TestServer,
} from '../support/server.ts'
import { signatureHeaderOf, unixSeconds } from '../support/signing.ts'

const maxEventBytes = 16_384

function assertCommonHeaders(response: Response): void {
  assert.equal(response.headers.get('x-content-type-options'), 'nosniff')
  assert.equal(response.headers.get('cache-control'), 'no-store')
}

async function assertProblem(response: Response, status: number, code: string): Promise<void> {
  assert.equal(response.status, status)
  assertCommonHeaders(response)
  const problem = await problemOf(response)
  assert.equal(problem.status, status)
  assert.equal(problem.code, code)
  assert.equal(problem.type, 'about:blank')
}

async function assertOutcome(response: Response, outcome: string): Promise<void> {
  assert.equal(response.status, 202)
  assertCommonHeaders(response)
  assert.deepEqual(await outcomeOf(response), { outcome })
}

function padded(envelope: JsonObject, totalBytes: number): string {
  const base = JSON.stringify({ ...envelope, padding: '' })
  const fill = totalBytes - Buffer.byteLength(base)
  assert.ok(fill >= 0, 'the envelope fits the requested size')
  return JSON.stringify({ ...envelope, padding: 'x'.repeat(fill) })
}

describe('POST /internal/job-events checks in order', () => {
  let server: TestServer
  let administrator: ClientSocket
  let events: RecordedEvent[]

  before(async () => {
    server = await startServer()
    administrator = await connectAdministrator(server)
    events = recordEvents(administrator)
  })

  after(async () => {
    closeClients([administrator])
    await server.close()
  })

  beforeEach(() => {
    events.length = 0
  })

  async function assertNoBroadcastSince(): Promise<void> {
    const marker = freshEnvelope('job.created')
    const delivery = received(administrator, 'job.created')
    await assertOutcome(await postEnvelope(server, marker.envelope), 'broadcast')
    await delivery
    assert.deepEqual(
      events.map((event) => event.name),
      ['job.created'],
      'only the marker event was broadcast',
    )
  }

  test('1: an unknown path under /internal answers 404 resource.not-found', async () => {
    for (const path of ['/internal/job-event', '/internal/job-events/extra', '/internal']) {
      const response = await fetch(`${server.baseUrl}${path}`, { method: 'POST' })

      await assertProblem(response, 404, 'resource.not-found')
    }
  })

  test('1: another method answers 405 request.method-not-allowed with Allow: POST', async () => {
    for (const method of ['GET', 'PUT', 'DELETE']) {
      const response = await fetch(`${server.baseUrl}${jobEventsPath}`, { method })

      assert.equal(response.headers.get('allow'), 'POST')
      await assertProblem(response, 405, 'request.method-not-allowed')
    }
  })

  test('2: a content type other than application/json answers 415', async () => {
    const { envelope } = freshEnvelope('job.created')
    for (const contentType of [
      'text/plain',
      'application/jsonx',
      'application/x-www-form-urlencoded',
    ]) {
      const response = await postEnvelope(server, envelope, { contentType })

      await assertProblem(response, 415, 'request.unsupported-media-type')
    }
    const missing = await postEnvelope(server, envelope, { contentType: null })
    await assertProblem(missing, 415, 'request.unsupported-media-type')
  })

  test('2: application/json is accepted with or without parameters and in any case', async () => {
    for (const contentType of ['application/json', 'Application/JSON; charset=utf-8']) {
      const { envelope } = freshEnvelope('job.assigned')

      await assertOutcome(await postEnvelope(server, envelope, { contentType }), 'broadcast')
    }
  })

  test('2: the content type is checked before the size', async () => {
    const response = await postJobEvent(server, 'x'.repeat(maxEventBytes + 1), {
      contentType: 'text/plain',
    })

    await assertProblem(response, 415, 'request.unsupported-media-type')
  })

  test('3: a declared body of 16 KB + 1 answers 413, even unsigned', async () => {
    const response = await fetch(`${server.baseUrl}${jobEventsPath}`, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: 'x'.repeat(maxEventBytes + 1),
    })

    assert.equal(response.headers.get('connection'), 'close')
    await assertProblem(response, 413, 'request.too-large')
  })

  test('3: a streamed body above 16 KB without Content-Length answers 413', async () => {
    const chunk = new TextEncoder().encode('x'.repeat(4096))
    let sent = 0
    const body = new ReadableStream<Uint8Array>({
      pull(controller) {
        if (sent >= 5) {
          controller.close()
          return
        }
        sent += 1
        controller.enqueue(chunk)
      },
    })
    const response = await fetch(`${server.baseUrl}${jobEventsPath}`, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body,
      duplex: 'half',
    })

    await assertProblem(response, 413, 'request.too-large')
  })

  test('3: a signed body of exactly 16384 bytes passes the size check', async () => {
    const { envelope } = freshEnvelope('job.status-changed')
    const body = padded(envelope, maxEventBytes)
    assert.equal(Buffer.byteLength(body), maxEventBytes)

    const id = String(envelope.id)
    await assertOutcome(await postJobEvent(server, body, { id }), 'broadcast')
  })

  for (const header of ['webhook-id', 'webhook-timestamp', 'webhook-signature']) {
    test(`4: a request without ${header} answers 401 webhook.unauthenticated`, async () => {
      const { envelope } = freshEnvelope('job.created')

      await assertProblem(
        await postEnvelope(server, envelope, { omit: [header] }),
        401,
        'webhook.unauthenticated',
      )
    })
  }

  test('4: a stale or future timestamp answers 401', async () => {
    for (const offset of [-301, 301]) {
      const { envelope } = freshEnvelope('job.created')
      const timestamp = String(Number(unixSeconds(server.clock.now())) + offset)

      await assertProblem(
        await postEnvelope(server, envelope, { timestamp }),
        401,
        'webhook.unauthenticated',
      )
    }
  })

  test('5: a bad HMAC answers 401', async () => {
    const { envelope } = freshEnvelope('job.created')

    await assertProblem(
      await postEnvelope(server, envelope, { secret: createWebhookSecret() }),
      401,
      'webhook.unauthenticated',
    )
  })

  test('5: a body changed after signing answers 401', async () => {
    const { id, envelope } = freshEnvelope('job.created')
    const signed = JSON.stringify(envelope)
    const timestamp = unixSeconds(server.clock.now())
    const signature = signatureHeaderOf(server.webhookSecret, id, timestamp, signed)

    const response = await postJobEvent(server, signed.replace('1051', '1052'), {
      id,
      timestamp,
      signature,
    })

    await assertProblem(response, 401, 'webhook.unauthenticated')
  })

  test('6: a signed body that is not JSON answers 400 webhook.invalid-payload', async () => {
    await assertProblem(await postJobEvent(server, '{"id":'), 400, 'webhook.invalid-payload')
  })

  test('6: a signed envelope that fails the schema answers 400', async () => {
    const { envelope, data } = freshEnvelope('job.status-changed')
    data.jobNumber = '1049'

    await assertProblem(await postEnvelope(server, envelope), 400, 'webhook.invalid-payload')
  })

  test('6: an envelope id that differs from webhook-id answers 400', async () => {
    const { envelope } = freshEnvelope('job.status-changed')

    await assertProblem(
      await postEnvelope(server, envelope, { id: randomUUID() }),
      400,
      'webhook.invalid-payload',
    )
  })

  test('7: a duplicate webhook-id answers 202 duplicate without a second broadcast', async () => {
    const { envelope } = freshEnvelope('job.assigned')
    const delivery = received(administrator, 'job.assigned')
    await assertOutcome(await postEnvelope(server, envelope), 'broadcast')
    await delivery
    events.length = 0

    await assertOutcome(await postEnvelope(server, envelope), 'duplicate')
    server.clock.advance(659_000)
    await assertOutcome(
      await postEnvelope(server, envelope, { timestamp: unixSeconds(server.clock.now()) }),
      'duplicate',
    )

    await assertNoBroadcastSince()
  })

  test('8: an unknown type answers 202 ignored without a broadcast', async () => {
    const { envelope } = freshEnvelope('job.created')
    envelope.type = 'job.deleted'

    await assertOutcome(await postEnvelope(server, envelope), 'ignored')
    await assertNoBroadcastSince()
  })

  test('8: a known type at an unknown version answers 202 ignored without a broadcast', async () => {
    const { envelope } = freshEnvelope('job.status-changed')
    envelope.version = 2

    await assertOutcome(await postEnvelope(server, envelope), 'ignored')
    await assertNoBroadcastSince()
  })

  test('9: a valid event answers 202 broadcast and reaches the administrators room', async () => {
    const { id, envelope } = freshEnvelope('job.status-changed')
    const delivery = received(administrator, 'job.status-changed')

    const response = await postEnvelope(server, envelope, { correlationId: 'trace-0042' })

    await assertOutcome(response, 'broadcast')
    assert.equal(response.headers.get('x-correlation-id'), 'trace-0042')
    assert.deepEqual(await delivery, { ...readClientFixture('job.status-changed'), eventId: id })
    assert.equal(server.io.sockets.adapter.rooms.get('administrators')?.size, 1)
  })

  test('a problem echoes a valid X-Correlation-ID in its body and header', async () => {
    const { envelope } = freshEnvelope('job.created')

    const response = await postEnvelope(server, envelope, {
      secret: createWebhookSecret(),
      correlationId: 'trace-0043',
    })

    assert.equal(response.headers.get('x-correlation-id'), 'trace-0043')
    assert.equal((await problemOf(response)).correlationId, 'trace-0043')
  })

  test('the internal fixture is broadcast when posted byte for byte', async () => {
    const body = readInternalFixtureText('job.created')
    const id = String(JSON.parse(body).id)
    const delivery = received(administrator, 'job.created')

    await assertOutcome(await postJobEvent(server, body, { id }), 'broadcast')
    assert.deepEqual(await delivery, readClientFixture('job.created'))
  })
})

describe('POST /internal/job-events records an id only after the checks pass', () => {
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

  test('an id rejected at the timestamp, signature or schema check is broadcast when sent valid', async () => {
    const { id, envelope, data } = freshEnvelope('job.status-changed')
    const stale = String(Number(unixSeconds(server.clock.now())) - 1000)
    await assertProblem(
      await postEnvelope(server, envelope, { timestamp: stale }),
      401,
      'webhook.unauthenticated',
    )
    await assertProblem(
      await postEnvelope(server, envelope, { secret: createWebhookSecret() }),
      401,
      'webhook.unauthenticated',
    )
    const broken = structuredClone(envelope)
    const brokenData = broken.data
    assert.ok(typeof brokenData === 'object' && brokenData !== null && !Array.isArray(brokenData))
    brokenData.status = 'Paused'
    await assertProblem(await postEnvelope(server, broken), 400, 'webhook.invalid-payload')

    const delivery = received(administrator, 'job.status-changed')
    await assertOutcome(await postEnvelope(server, envelope), 'broadcast')
    assert.equal((await delivery).eventId, id)
    assert.equal(data.status, 'InProgress')
  })

  test('a replayed request meets the id window, then the timestamp check', async () => {
    const { envelope } = freshEnvelope('job.created')
    const timestamp = unixSeconds(server.clock.now())
    await assertOutcome(await postEnvelope(server, envelope, { timestamp }), 'broadcast')

    server.clock.advance(300_000)
    await assertOutcome(await postEnvelope(server, envelope, { timestamp }), 'duplicate')

    server.clock.advance(1_000)
    await assertProblem(
      await postEnvelope(server, envelope, { timestamp }),
      401,
      'webhook.unauthenticated',
    )
  })

  test('the same id signed afresh after the 660-second window is broadcast again', async () => {
    const { id, envelope } = freshEnvelope('job.assigned')
    await assertOutcome(await postEnvelope(server, envelope), 'broadcast')

    server.clock.advance(659_000)
    await assertOutcome(await postEnvelope(server, envelope), 'duplicate')

    server.clock.advance(1_000)
    const delivery = received(administrator, 'job.assigned')
    await assertOutcome(await postEnvelope(server, envelope), 'broadcast')
    assert.equal((await delivery).eventId, id)
  })
})
