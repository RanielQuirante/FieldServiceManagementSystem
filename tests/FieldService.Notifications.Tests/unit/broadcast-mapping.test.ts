import assert from 'node:assert/strict'
import { describe, test } from 'node:test'
import { toClientPayload } from '../../../src/FieldService.Notifications/src/features/job-events/job-event-broadcast.ts'
import { parseEnvelope } from '../../../src/FieldService.Notifications/src/features/job-events/job-event-envelope.ts'
import {
  cloneJson,
  eventTypes,
  readClientFixture,
  readEventFile,
  readInternalFixture,
  readInternalFixtureText,
} from '../support/fixtures.ts'

const payloadMembers = ['eventId', 'jobId', 'jobNumber', 'status', 'previousStatus', 'occurredAt']

function eventOf(text: string) {
  const { event } = parseEnvelope(Buffer.from(text, 'utf8'))
  assert.ok(event !== null, 'the fixture is a known event')
  return event
}

describe('broadcast mapping', () => {
  for (const type of eventTypes) {
    test(`${type} maps to its client fixture exactly`, () => {
      const payload = toClientPayload(eventOf(readInternalFixtureText(type)))

      assert.deepEqual(payload, readClientFixture(type))
      assert.equal(
        JSON.stringify(payload),
        JSON.stringify(JSON.parse(readEventFile(`client/${type}.json`))),
        'the members appear in the order of the client fixture',
      )
    })
  }

  test('emits only the six payload members, whatever the envelope carries', () => {
    const envelope = cloneJson(readInternalFixture('job.assigned'))
    envelope.technicianName = 'not forwarded'
    const data = envelope.data
    assert.ok(typeof data === 'object' && data !== null && !Array.isArray(data))
    data.siteAddress = 'not forwarded'

    const payload = toClientPayload(eventOf(JSON.stringify(envelope)))

    assert.deepEqual(Object.keys(payload), payloadMembers)
    assert.ok(!JSON.stringify(payload).includes('not forwarded'))
  })

  test('carries no correlation id', () => {
    const payload = toClientPayload(eventOf(readInternalFixtureText('job.status-changed')))

    assert.ok(!('correlationId' in payload))
    assert.ok(
      !Object.values(payload).includes(
        readInternalFixture('job.status-changed').correlationId ?? '',
      ),
    )
  })

  test('passes occurredAt through unchanged', () => {
    const fixture = readInternalFixture('job.created')
    const payload = toClientPayload(eventOf(readInternalFixtureText('job.created')))

    assert.equal(payload.occurredAt, fixture.occurredAt)
  })
})
