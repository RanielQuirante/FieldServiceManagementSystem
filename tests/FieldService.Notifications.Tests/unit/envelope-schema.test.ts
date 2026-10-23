import assert from 'node:assert/strict'
import { describe, test } from 'node:test'
import {
  parseEnvelope,
  type ParsedEnvelope,
} from '../../../src/FieldService.Notifications/src/features/job-events/job-event-envelope.ts'
import { InvalidPayloadError } from '../../../src/FieldService.Notifications/src/shared/http/http-errors.ts'
import {
  cloneJson,
  eventTypes,
  readInternalFixture,
  readInternalFixtureText,
  type JsonObject,
  type JsonValue,
} from '../support/fixtures.ts'

function parseText(text: string): ParsedEnvelope {
  return parseEnvelope(Buffer.from(text, 'utf8'))
}

function parseObject(value: JsonValue): ParsedEnvelope {
  return parseText(JSON.stringify(value))
}

function reasonOf(action: () => unknown): string {
  let caught: unknown
  try {
    action()
  } catch (error) {
    caught = error
  }
  assert.ok(caught instanceof InvalidPayloadError, 'expected a 400 webhook.invalid-payload')
  assert.equal(caught.status, 400)
  assert.equal(caught.code, 'webhook.invalid-payload')
  return caught.reason
}

function statusChanged(): JsonObject {
  return readInternalFixture('job.status-changed')
}

function dataOf(envelope: JsonObject): JsonObject {
  const data = envelope.data
  assert.ok(typeof data === 'object' && data !== null && !Array.isArray(data))
  return data
}

function withMember(name: string, value: JsonValue | undefined): JsonObject {
  const envelope = cloneJson(statusChanged())
  if (value === undefined) {
    delete envelope[name]
  } else {
    envelope[name] = value
  }
  return envelope
}

function withDataMember(name: string, value: JsonValue | undefined): JsonObject {
  const envelope = cloneJson(statusChanged())
  const data = dataOf(envelope)
  if (value === undefined) {
    delete data[name]
  } else {
    data[name] = value
  }
  return envelope
}

describe('parseEnvelope accepts the internal fixtures', () => {
  for (const type of eventTypes) {
    test(`${type} version 1 parses into a typed event`, () => {
      const fixture = readInternalFixture(type)
      const { envelope, event } = parseText(readInternalFixtureText(type))

      assert.deepEqual(envelope, fixture)
      assert.ok(event !== null)
      assert.equal(event.type, type)
      assert.equal(event.version, 1)
      assert.deepEqual(event.data, fixture.data)
    })
  }

  test('unknown members of the envelope and of the typed event data are stripped', () => {
    const envelope = withMember('note', 'free text')
    dataOf(envelope).customerName = 'not forwarded'

    const parsed = parseObject(envelope)

    assert.ok(!('note' in parsed.envelope))
    assert.ok(parsed.event !== null)
    assert.ok(!JSON.stringify(parsed.event).includes('not forwarded'))
    assert.deepEqual(Object.keys(parsed.event.data), [
      'jobId',
      'jobNumber',
      'status',
      'previousStatus',
    ])
  })
})

describe('parseEnvelope returns no event for an unknown type or version', () => {
  test('an unknown type with any data', () => {
    const envelope = withMember('type', 'job.deleted')
    envelope.data = { anything: [1, 2, 3] }

    assert.equal(parseObject(envelope).event, null)
  })

  test('a known type at version 2, whose data is not checked against version 1', () => {
    const envelope = withMember('version', 2)
    envelope.data = { jobId: 'not checked' }

    const parsed = parseObject(envelope)

    assert.equal(parsed.event, null)
    assert.equal(parsed.envelope.version, 2)
  })
})

describe('parseEnvelope rejects with schema', () => {
  for (const name of ['id', 'type', 'version', 'occurredAt', 'correlationId', 'data']) {
    test(`an envelope without ${name}`, () => {
      assert.equal(
        reasonOf(() => parseObject(withMember(name, undefined))),
        'schema',
      )
    })
  }

  for (const name of ['jobId', 'jobNumber', 'status', 'previousStatus']) {
    test(`a known event without data.${name}`, () => {
      assert.equal(
        reasonOf(() => parseObject(withDataMember(name, undefined))),
        'schema',
      )
    })
  }

  const envelopeCases: readonly [string, string, JsonValue][] = [
    ['a non-uuid id', 'id', 'event-1'],
    ['a numeric id', 'id', 1049],
    ['an empty type', 'type', ''],
    ['a type longer than 64 characters', 'type', `job.${'x'.repeat(61)}`],
    ['a numeric type', 'type', 7],
    ['a string version', 'version', '1'],
    ['a fractional version', 'version', 1.5],
    ['version 0', 'version', 0],
    ['an occurredAt without fractional digits', 'occurredAt', '2026-10-19T14:42:00Z'],
    ['an occurredAt with two fractional digits', 'occurredAt', '2026-10-19T14:42:00.00Z'],
    ['an occurredAt with four fractional digits', 'occurredAt', '2026-10-19T14:42:00.0000Z'],
    ['an occurredAt without Z', 'occurredAt', '2026-10-19T14:42:00.000'],
    ['an occurredAt with an offset', 'occurredAt', '2026-10-19T14:42:00.000+00:00'],
    ['a numeric occurredAt', 'occurredAt', 1792420920000],
    ['a correlationId with a space', 'correlationId', 'not valid'],
    ['a correlationId of 65 characters', 'correlationId', 'a'.repeat(65)],
    ['an empty correlationId', 'correlationId', ''],
    ['data as an array', 'data', [1, 2]],
    ['data as null', 'data', null],
    ['data as a string', 'data', 'job'],
  ]

  for (const [name, member, value] of envelopeCases) {
    test(`an envelope with ${name}`, () => {
      assert.equal(
        reasonOf(() => parseObject(withMember(member, value))),
        'schema',
      )
    })
  }

  const dataCases: readonly [string, string, JsonValue][] = [
    ['a jobId that is not a uuid', 'jobId', 'JOB-1049'],
    ['a negative jobNumber', 'jobNumber', -1],
    ['jobNumber 0', 'jobNumber', 0],
    ['a fractional jobNumber', 'jobNumber', 1049.5],
    ['a string jobNumber', 'jobNumber', '1049'],
    ['a jobNumber beyond the safe integers', 'jobNumber', Number.MAX_SAFE_INTEGER + 2],
    ['an unknown status', 'status', 'Paused'],
    ['a lower-case status', 'status', 'inprogress'],
    ['a null status', 'status', null],
    ['an unknown previousStatus', 'previousStatus', 'Started'],
  ]

  for (const [name, member, value] of dataCases) {
    test(`a known event with ${name}`, () => {
      assert.equal(
        reasonOf(() => parseObject(withDataMember(member, value))),
        'schema',
      )
    })
  }

  for (const value of [[], 'text', 42, null, true]) {
    test(`a JSON body that is not an object: ${JSON.stringify(value)}`, () => {
      assert.equal(
        reasonOf(() => parseObject(value)),
        'schema',
      )
    })
  }

  test('the largest safe jobNumber is accepted', () => {
    assert.ok(parseObject(withDataMember('jobNumber', Number.MAX_SAFE_INTEGER)).event !== null)
  })
})

describe('parseEnvelope rejects with invalid-json', () => {
  test('a body that is not JSON', () => {
    assert.equal(
      reasonOf(() => parseText('{"id":')),
      'invalid-json',
    )
  })

  test('an empty body', () => {
    assert.equal(
      reasonOf(() => parseText('')),
      'invalid-json',
    )
  })

  test('a body that is not valid UTF-8', () => {
    const bytes = Buffer.concat([
      Buffer.from(readInternalFixtureText('job.created'), 'utf8'),
      Buffer.from([0xff]),
    ])

    assert.equal(
      reasonOf(() => parseEnvelope(bytes)),
      'invalid-json',
    )
  })

  test('a body that starts with a byte order mark', () => {
    assert.equal(
      reasonOf(() => parseText(`﻿${readInternalFixtureText('job.created')}`)),
      'invalid-json',
    )
  })
})
