import assert from 'node:assert/strict'
import { randomUUID } from 'node:crypto'
import { after, before, describe, test } from 'node:test'
import { toClientPayload } from '../../../src/FieldService.Notifications/src/features/job-events/job-event-broadcast.ts'
import { parseEnvelope } from '../../../src/FieldService.Notifications/src/features/job-events/job-event-envelope.ts'
import { InvalidPayloadError } from '../../../src/FieldService.Notifications/src/shared/http/http-errors.ts'
import {
  cloneJson,
  eventTypes,
  readClientFixture,
  readEventFile,
  readInternalFixture,
  readInternalFixtureText,
  type EventType,
  type JsonObject,
  type JsonValue,
} from '../support/fixtures.ts'
import {
  closeClients,
  connectAdministrator,
  outcomeOf,
  postJobEvent,
  problemOf,
  received,
  startServer,
  type ClientSocket,
  type TestServer,
} from '../support/server.ts'

function schemaRejects(envelope: JsonValue): boolean {
  try {
    parseEnvelope(Buffer.from(JSON.stringify(envelope), 'utf8'))
    return false
  } catch (error) {
    return error instanceof InvalidPayloadError && error.reason === 'schema'
  }
}

function mutated(
  type: EventType,
  mutate: (envelope: JsonObject, data: JsonObject) => void,
): JsonObject {
  const envelope = cloneJson(readInternalFixture(type))
  const data = envelope.data
  assert.ok(typeof data === 'object' && data !== null && !Array.isArray(data))
  mutate(envelope, data)
  return envelope
}

describe('internal event fixtures', () => {
  for (const type of eventTypes) {
    test(`${type}.v1.json is stored with two-space indentation and LF endings`, () => {
      const text = readEventFile(`internal/${type}.v1.json`)

      assert.ok(!text.includes('\r'))
      assert.equal(text, `${JSON.stringify(JSON.parse(text), null, 2)}\n`)
    })

    test(`the schema accepts ${type}.v1.json, compacted with its member order`, () => {
      const { envelope, event } = parseEnvelope(Buffer.from(readInternalFixtureText(type), 'utf8'))

      assert.deepEqual(envelope, readInternalFixture(type))
      assert.ok(event !== null)
      assert.equal(event.type, type)
      assert.deepEqual(Object.keys(readInternalFixture(type)), [
        'id',
        'type',
        'version',
        'occurredAt',
        'correlationId',
        'data',
      ])
    })

    const mutations: readonly [string, (envelope: JsonObject, data: JsonObject) => void][] = [
      ['a missing id', (envelope) => delete envelope.id],
      ['a missing type', (envelope) => delete envelope.type],
      ['a missing version', (envelope) => delete envelope.version],
      ['a missing occurredAt', (envelope) => delete envelope.occurredAt],
      ['a missing correlationId', (envelope) => delete envelope.correlationId],
      ['a missing data', (envelope) => delete envelope.data],
      ['a missing data.jobId', (_, data) => delete data.jobId],
      ['a missing data.jobNumber', (_, data) => delete data.jobNumber],
      ['a missing data.status', (_, data) => delete data.status],
      ['a missing data.previousStatus', (_, data) => delete data.previousStatus],
      ['a non-uuid id', (envelope) => (envelope.id = 'JOB-1049-event')],
      ['a non-uuid jobId', (_, data) => (data.jobId = 'JOB-1049')],
      ['an unknown status', (_, data) => (data.status = 'Paused')],
      ['an unknown previousStatus', (_, data) => (data.previousStatus = 'Paused')],
      ['a string jobNumber', (_, data) => (data.jobNumber = String(data.jobNumber))],
      [
        'an occurredAt without milliseconds',
        (envelope) => (envelope.occurredAt = String(envelope.occurredAt).replace('.000Z', 'Z')),
      ],
      [
        'an occurredAt without Z',
        (envelope) => (envelope.occurredAt = String(envelope.occurredAt).replace('Z', '')),
      ],
    ]

    for (const [name, mutate] of mutations) {
      test(`the schema rejects ${type} with ${name}`, () => {
        assert.ok(schemaRejects(mutated(type, mutate)))
      })
    }

    test(`${type} maps to client/${type}.json`, () => {
      const { event } = parseEnvelope(Buffer.from(readInternalFixtureText(type), 'utf8'))
      assert.ok(event !== null)

      assert.deepEqual(toClientPayload(event), readClientFixture(type))
    })
  }
})

describe('internal event fixtures posted to a real server', () => {
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

  for (const type of eventTypes) {
    test(`a signed ${type} reaches an administrator as ${type} with the client fixture`, async () => {
      const body = readInternalFixtureText(type)
      const id = String(readInternalFixture(type).id)
      const delivery = received(administrator, type)

      const response = await postJobEvent(server, body, { id })

      assert.equal(response.status, 202)
      assert.deepEqual(await outcomeOf(response), { outcome: 'broadcast' })
      assert.deepEqual(await delivery, readClientFixture(type))
    })
  }

  test('a fixture whose id differs from webhook-id answers 400 webhook.invalid-payload', async () => {
    const body = readInternalFixtureText('job.status-changed')

    const response = await postJobEvent(server, body, { id: randomUUID() })

    assert.equal(response.status, 400)
    assert.equal((await problemOf(response)).code, 'webhook.invalid-payload')
  })
})
