import { createHmac, randomUUID } from 'node:crypto'

export const webhookSecretPrefix = 'whsec_'

export function webhookKey(secret: string): Buffer {
  if (!secret.startsWith(webhookSecretPrefix)) {
    throw new Error('a webhook secret starts with whsec_')
  }
  return Buffer.from(secret.slice(webhookSecretPrefix.length), 'base64')
}

export function signatureOf(
  secret: string,
  id: string,
  timestamp: string,
  body: string | Uint8Array,
): string {
  const content = Buffer.concat([
    Buffer.from(`${id}.${timestamp}.`, 'utf8'),
    typeof body === 'string' ? Buffer.from(body, 'utf8') : body,
  ])
  return createHmac('sha256', webhookKey(secret)).update(content).digest('base64')
}

export function signatureHeaderOf(
  secret: string,
  id: string,
  timestamp: string,
  body: string | Uint8Array,
): string {
  return `v1,${signatureOf(secret, id, timestamp, body)}`
}

export function unixSeconds(epochMilliseconds: number): string {
  return String(Math.floor(epochMilliseconds / 1000))
}

export function newEventId(): string {
  return randomUUID()
}

export interface SignedRequestOptions {
  readonly id?: string
  readonly timestamp?: string
  readonly secret?: string
  readonly signature?: string
  readonly contentType?: string | null
  readonly correlationId?: string
  readonly omit?: readonly string[]
  readonly headers?: Readonly<Record<string, string>>
}

export function signedHeaders(
  body: string | Uint8Array,
  secret: string,
  nowMs: number,
  options: SignedRequestOptions = {},
): Record<string, string> {
  const id = options.id ?? newEventId()
  const timestamp = options.timestamp ?? unixSeconds(nowMs)
  const signature =
    options.signature ?? signatureHeaderOf(options.secret ?? secret, id, timestamp, body)
  const headers: Record<string, string> = {
    'webhook-id': id,
    'webhook-timestamp': timestamp,
    'webhook-signature': signature,
    ...(options.contentType === null
      ? {}
      : { 'content-type': options.contentType ?? 'application/json; charset=utf-8' }),
    ...(options.correlationId === undefined ? {} : { 'x-correlation-id': options.correlationId }),
    ...options.headers,
  }
  for (const name of options.omit ?? []) {
    delete headers[name]
  }
  return headers
}
