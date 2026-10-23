import assert from 'node:assert/strict'
import { randomBytes } from 'node:crypto'
import type { IncomingHttpHeaders } from 'node:http'
import { describe, test } from 'node:test'
import { verifyWebhook } from '../../../src/FieldService.Notifications/src/features/job-events/webhook-signature.ts'
import { UnauthenticatedError } from '../../../src/FieldService.Notifications/src/shared/http/http-errors.ts'
import { defaultClockStart } from '../support/clock.ts'
import { createWebhookSecret } from '../support/fixtures.ts'
import {
  newEventId,
  signatureHeaderOf,
  signatureOf,
  signedHeaders,
  unixSeconds,
  webhookKey,
  type SignedRequestOptions,
} from '../support/signing.ts'

const now = defaultClockStart
const secret = createWebhookSecret()
const body = '{"id":"01a1549c-bac0-7272-8af7-8acf82fc5f4d","type":"job.status-changed","version":1}'

function headersFor(
  options: SignedRequestOptions = {},
  signedBody: string | Uint8Array = body,
): IncomingHttpHeaders {
  return signedHeaders(signedBody, secret, now, options)
}

function verify(
  headers: IncomingHttpHeaders,
  options: {
    body?: string | Uint8Array
    secrets?: string[]
    nowMs?: number
    tolerance?: number
  } = {},
): string {
  const rawBody = options.body ?? body
  return verifyWebhook(
    headers,
    typeof rawBody === 'string' ? Buffer.from(rawBody, 'utf8') : rawBody,
    {
      secrets: (options.secrets ?? [secret]).map(webhookKey),
      toleranceSeconds: options.tolerance ?? 300,
      nowMs: options.nowMs ?? now,
    },
  )
}

function reasonOf(action: () => unknown): string {
  let caught: unknown
  try {
    action()
  } catch (error) {
    caught = error
  }
  assert.ok(caught instanceof UnauthenticatedError, 'expected a 401 webhook.unauthenticated')
  assert.equal(caught.status, 401)
  assert.equal(caught.code, 'webhook.unauthenticated')
  return caught.reason
}

function otherSignature(byteLength = 32): string {
  return `v1,${randomBytes(byteLength).toString('base64')}`
}

describe('verifyWebhook accepts', () => {
  test('a valid signature and returns the webhook-id', () => {
    const id = newEventId()

    assert.equal(verify(headersFor({ id })), id)
  })

  test('a timestamp exactly 300 s old or 300 s ahead at the default tolerance', () => {
    for (const offset of [-300, 300]) {
      const timestamp = String(Number(unixSeconds(now)) + offset)

      assert.doesNotThrow(() => verify(headersFor({ timestamp })))
    }
  })

  test('a header with several space-separated v1 signatures when one of them matches', () => {
    const id = newEventId()
    const timestamp = unixSeconds(now)
    const valid = signatureHeaderOf(secret, id, timestamp, body)
    const signature = [otherSignature(), valid, otherSignature()].join(' ')

    assert.equal(verify(headersFor({ id, timestamp, signature })), id)
  })

  test('a v1 signature beside entries of another version', () => {
    const id = newEventId()
    const timestamp = unixSeconds(now)
    const valid = signatureOf(secret, id, timestamp, body)
    const signature = `v2,${valid} v1a,${valid} v1,${valid}`

    assert.equal(verify(headersFor({ id, timestamp, signature })), id)
  })

  test('both configured secrets during a rotation', () => {
    const newest = createWebhookSecret()
    const oldest = createWebhookSecret()

    for (const signingSecret of [newest, oldest]) {
      const id = newEventId()
      const headers = signedHeaders(body, signingSecret, now, { id })

      assert.equal(verify(headers, { secrets: [newest, oldest] }), id)
    }
  })

  test('the raw body bytes, whatever their formatting', () => {
    const spaced = '{ "id" : "x",\n  "type": "job.created" }'
    const id = newEventId()

    assert.equal(verify(headersFor({ id }, spaced), { body: spaced }), id)
  })

  test('a body that is not valid UTF-8, because the HMAC covers bytes', () => {
    const bytes = Uint8Array.from([0xff, 0xfe, 0x7b, 0x7d])
    const id = newEventId()

    assert.equal(verify(headersFor({ id }, bytes), { body: bytes }), id)
  })

  test('a signature header of exactly 1024 characters', () => {
    const id = newEventId()
    const timestamp = unixSeconds(now)
    const valid = signatureHeaderOf(secret, id, timestamp, body)
    const filler = `v2,${'A'.repeat(1024 - valid.length - 4)}`
    const signature = `${filler} ${valid}`

    assert.equal(signature.length, 1024)
    assert.equal(verify(headersFor({ id, timestamp, signature })), id)
  })
})

describe('verifyWebhook rejects with signature-mismatch', () => {
  test('a body changed by one byte', () => {
    const headers = headersFor()
    const changed = body.replace('"version":1', '"version":2')

    assert.equal(
      reasonOf(() => verify(headers, { body: changed })),
      'signature-mismatch',
    )
  })

  test('a changed webhook-id', () => {
    const headers = { ...headersFor(), 'webhook-id': newEventId() }

    assert.equal(
      reasonOf(() => verify(headers)),
      'signature-mismatch',
    )
  })

  test('a changed timestamp inside the tolerance', () => {
    const headers = headersFor()
    const timestamp = String(Number(headers['webhook-timestamp']) + 1)

    assert.equal(
      reasonOf(() => verify({ ...headers, 'webhook-timestamp': timestamp })),
      'signature-mismatch',
    )
  })

  test('a signature made with another secret', () => {
    const headers = headersFor({ secret: createWebhookSecret() })

    assert.equal(
      reasonOf(() => verify(headers)),
      'signature-mismatch',
    )
  })

  test('a signature without the v1, prefix', () => {
    const id = newEventId()
    const timestamp = unixSeconds(now)
    const signature = signatureOf(secret, id, timestamp, body)

    assert.equal(
      reasonOf(() => verify(headersFor({ id, timestamp, signature }))),
      'signature-mismatch',
    )
  })

  test('a valid signature under a v2, prefix', () => {
    const id = newEventId()
    const timestamp = unixSeconds(now)
    const signature = `v2,${signatureOf(secret, id, timestamp, body)}`

    assert.equal(
      reasonOf(() => verify(headersFor({ id, timestamp, signature }))),
      'signature-mismatch',
    )
  })

  for (const byteLength of [0, 16, 31, 33, 64]) {
    test(`a signature of ${byteLength} bytes without throwing anything but the 401`, () => {
      const signature = otherSignature(byteLength)

      assert.equal(
        reasonOf(() => verify(headersFor({ signature }))),
        'signature-mismatch',
      )
    })
  }

  test('a signature that is not canonical base64', () => {
    const id = newEventId()
    const timestamp = unixSeconds(now)
    const valid = signatureOf(secret, id, timestamp, body)
    const unpadded = `v1,${valid.replace(/=+$/, '')}`
    const urlSafe = `v1,${valid.replaceAll('+', '-').replaceAll('/', '_')}`

    for (const signature of [unpadded, urlSafe, 'v1,not base64!', 'v1,']) {
      if (signature === `v1,${valid}`) {
        continue
      }
      assert.equal(
        reasonOf(() => verify(headersFor({ id, timestamp, signature }))),
        'signature-mismatch',
      )
    }
  })

  test('a signature header longer than 1024 characters, even with a valid entry', () => {
    const id = newEventId()
    const timestamp = unixSeconds(now)
    const valid = signatureHeaderOf(secret, id, timestamp, body)
    const signature = `${'v2,'.padEnd(1024 - valid.length, 'A')} ${valid}`

    assert.equal(signature.length, 1025)
    assert.equal(
      reasonOf(() => verify(headersFor({ id, timestamp, signature }))),
      'signature-mismatch',
    )
  })

  test('a signature over the re-serialized JSON instead of the raw bytes', () => {
    const raw = '{ "id": "x" }'
    const reserialized = JSON.stringify(JSON.parse(raw))
    const headers = headersFor({}, reserialized)

    assert.equal(
      reasonOf(() => verify(headers, { body: raw })),
      'signature-mismatch',
    )
  })
})

describe('verifyWebhook rejects with timestamp-out-of-range', () => {
  test('a timestamp 301 s old or 301 s ahead on the fake clock', () => {
    for (const offset of [-301, 301]) {
      const timestamp = String(Number(unixSeconds(now)) + offset)

      assert.equal(
        reasonOf(() => verify(headersFor({ timestamp }))),
        'timestamp-out-of-range',
      )
    }
  })

  test('a timestamp outside a 30-second tolerance', () => {
    const accepted = String(Number(unixSeconds(now)) - 30)
    const rejected = String(Number(unixSeconds(now)) - 31)

    assert.doesNotThrow(() => verify(headersFor({ timestamp: accepted }), { tolerance: 30 }))
    assert.equal(
      reasonOf(() => verify(headersFor({ timestamp: rejected }), { tolerance: 30 })),
      'timestamp-out-of-range',
    )
  })

  test('a fresh event once the clock has moved beyond the tolerance', () => {
    const headers = headersFor()

    assert.equal(
      reasonOf(() => verify(headers, { nowMs: now + 301_000 })),
      'timestamp-out-of-range',
    )
  })

  for (const timestamp of ['12a', '-5', '1.5', ' 1792420920', '1234567890123', '0x10']) {
    test(`a timestamp that is not 1 to 12 digits: ${JSON.stringify(timestamp)}`, () => {
      assert.equal(
        reasonOf(() => verify(headersFor({ timestamp }))),
        'timestamp-out-of-range',
      )
    })
  }
})

describe('verifyWebhook rejects with missing-headers', () => {
  for (const name of ['webhook-id', 'webhook-timestamp', 'webhook-signature']) {
    test(`a request without ${name}`, () => {
      assert.equal(
        reasonOf(() => verify(headersFor({ omit: [name] }))),
        'missing-headers',
      )
    })

    test(`a request with an empty ${name}`, () => {
      assert.equal(
        reasonOf(() => verify({ ...headersFor(), [name]: '' })),
        'missing-headers',
      )
    })
  }

  test('missing headers before a stale timestamp', () => {
    const timestamp = String(Number(unixSeconds(now)) - 1000)

    assert.equal(
      reasonOf(() => verify(headersFor({ timestamp, omit: ['webhook-signature'] }))),
      'missing-headers',
    )
  })

  test('a stale timestamp before a bad signature', () => {
    const timestamp = String(Number(unixSeconds(now)) - 1000)

    assert.equal(
      reasonOf(() => verify(headersFor({ timestamp, signature: otherSignature() }))),
      'timestamp-out-of-range',
    )
  })
})
