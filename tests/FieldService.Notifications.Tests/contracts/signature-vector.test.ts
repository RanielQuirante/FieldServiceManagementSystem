import assert from 'node:assert/strict'
import type { IncomingHttpHeaders } from 'node:http'
import { describe, test } from 'node:test'
import { parseEnvelope } from '../../../src/FieldService.Notifications/src/features/job-events/job-event-envelope.ts'
import { verifyWebhook } from '../../../src/FieldService.Notifications/src/features/job-events/webhook-signature.ts'
import { UnauthenticatedError } from '../../../src/FieldService.Notifications/src/shared/http/http-errors.ts'
import {
  createWebhookSecret,
  readInternalFixtureText,
  readSignatureVector,
} from '../support/fixtures.ts'
import { signatureHeaderOf, webhookKey } from '../support/signing.ts'
import { changeCharacterAt } from '../support/tickets.ts'

const vector = readSignatureVector()
const vectorTimestampMs = Number(vector.timestamp) * 1000

interface Attempt {
  readonly id?: string
  readonly timestamp?: string
  readonly body?: string
  readonly signature?: string
  readonly secret?: string
  readonly nowMs?: number
}

function headersOf(attempt: Attempt): IncomingHttpHeaders {
  return {
    'webhook-id': attempt.id ?? vector.id,
    'webhook-timestamp': attempt.timestamp ?? vector.timestamp,
    'webhook-signature': attempt.signature ?? vector.signature,
  }
}

function verifyAttempt(attempt: Attempt = {}): string {
  return verifyWebhook(headersOf(attempt), Buffer.from(attempt.body ?? vector.body, 'utf8'), {
    secrets: [webhookKey(attempt.secret ?? vector.secret)],
    toleranceSeconds: 300,
    nowMs: attempt.nowMs ?? vectorTimestampMs,
  })
}

function rejectionReason(attempt: Attempt): string {
  let caught: unknown
  try {
    verifyAttempt(attempt)
  } catch (error) {
    caught = error
  }
  assert.ok(caught instanceof UnauthenticatedError, 'expected the vector to be rejected')
  return caught.reason
}

describe('signature test vector', () => {
  test('holds the five members in the documented order', () => {
    assert.deepEqual(Object.keys(vector), ['secret', 'id', 'timestamp', 'body', 'signature'])
    assert.match(vector.timestamp, /^\d{1,12}$/)
    assert.equal(webhookKey(vector.secret).length, 32)
  })

  test('its body is the compacted job.status-changed fixture and a valid envelope', () => {
    assert.equal(vector.body, readInternalFixtureText('job.status-changed'))
    const { envelope, event } = parseEnvelope(Buffer.from(vector.body, 'utf8'))
    assert.equal(envelope.id, vector.id)
    assert.ok(event !== null)
  })

  test('the test signer reproduces the vector signature', () => {
    assert.equal(
      signatureHeaderOf(vector.secret, vector.id, vector.timestamp, vector.body),
      vector.signature,
    )
  })

  test('the verifier accepts the vector with the fake clock at its timestamp', () => {
    assert.equal(verifyAttempt(), vector.id)
  })

  test('the verifier accepts the vector at the edges of the 300-second tolerance', () => {
    assert.equal(verifyAttempt({ nowMs: vectorTimestampMs - 300_000 }), vector.id)
    assert.equal(verifyAttempt({ nowMs: vectorTimestampMs + 300_999 }), vector.id)
  })

  test('the verifier rejects a one-character change to the id', () => {
    assert.equal(rejectionReason({ id: changeCharacterAt(vector.id, 10) }), 'signature-mismatch')
  })

  test('the verifier rejects a one-character change to the timestamp', () => {
    const timestamp = `${vector.timestamp.slice(0, -1)}${vector.timestamp.endsWith('0') ? '1' : '0'}`

    assert.equal(rejectionReason({ timestamp }), 'signature-mismatch')
  })

  test('the verifier rejects a one-byte change to the body', () => {
    const index = vector.body.indexOf('1049')
    const body = `${vector.body.slice(0, index)}2${vector.body.slice(index + 1)}`

    assert.equal(rejectionReason({ body }), 'signature-mismatch')
  })

  test('the verifier rejects a one-character change to the signature', () => {
    assert.equal(
      rejectionReason({ signature: changeCharacterAt(vector.signature, 20) }),
      'signature-mismatch',
    )
  })

  test('the verifier rejects the vector under a wrong secret', () => {
    assert.equal(rejectionReason({ secret: createWebhookSecret() }), 'signature-mismatch')
  })

  test('the verifier rejects the vector signature under a v2, prefix', () => {
    const signature = vector.signature.replace(/^v1,/, 'v2,')

    assert.equal(rejectionReason({ signature }), 'signature-mismatch')
  })

  test('the verifier rejects the vector once the clock is beyond the tolerance', () => {
    for (const nowMs of [vectorTimestampMs + 301_000, vectorTimestampMs - 301_000]) {
      assert.equal(rejectionReason({ nowMs }), 'timestamp-out-of-range')
    }
  })
})
