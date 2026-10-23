import { createHmac, timingSafeEqual } from 'node:crypto'
import type { IncomingHttpHeaders } from 'node:http'
import { UnauthenticatedError } from '../../shared/http/http-errors.ts'

export const webhookHeaders = {
  id: 'webhook-id',
  timestamp: 'webhook-timestamp',
  signature: 'webhook-signature',
} as const

export const webhookRejectionReasons = {
  missingHeaders: 'missing-headers',
  timestampOutOfRange: 'timestamp-out-of-range',
  signatureMismatch: 'signature-mismatch',
} as const

export interface WebhookVerificationOptions {
  readonly secrets: readonly Uint8Array[]
  readonly toleranceSeconds: number
  readonly nowMs: number
}

const timestampPattern = /^\d{1,12}$/
const maxSignatureHeaderLength = 1024
const signatureVersion = 'v1'
const signatureByteLength = 32

function headerOf(headers: IncomingHttpHeaders, name: string): string | undefined {
  const value = headers[name]
  return typeof value === 'string' && value !== '' ? value : undefined
}

function decodeSignature(text: string): Buffer | undefined {
  const bytes = Buffer.from(text, 'base64')
  if (bytes.length !== signatureByteLength || bytes.toString('base64') !== text) {
    return undefined
  }
  return bytes
}

function candidateSignatures(header: string): Buffer[] {
  const candidates: Buffer[] = []
  for (const entry of header.split(' ')) {
    const separator = entry.indexOf(',')
    if (separator === -1 || entry.slice(0, separator) !== signatureVersion) {
      continue
    }
    const decoded = decodeSignature(entry.slice(separator + 1))
    if (decoded !== undefined) {
      candidates.push(decoded)
    }
  }
  return candidates
}

function matchesAny(candidates: readonly Buffer[], expected: Buffer): boolean {
  let matched = false
  for (const candidate of candidates) {
    if (candidate.length === expected.length && timingSafeEqual(candidate, expected)) {
      matched = true
    }
  }
  return matched
}

export function verifyWebhook(
  headers: IncomingHttpHeaders,
  body: Uint8Array,
  options: WebhookVerificationOptions,
): string {
  const id = headerOf(headers, webhookHeaders.id)
  const timestamp = headerOf(headers, webhookHeaders.timestamp)
  const signature = headerOf(headers, webhookHeaders.signature)
  if (id === undefined || timestamp === undefined || signature === undefined) {
    throw new UnauthenticatedError(webhookRejectionReasons.missingHeaders)
  }

  if (!timestampPattern.test(timestamp)) {
    throw new UnauthenticatedError(webhookRejectionReasons.timestampOutOfRange)
  }
  const nowSeconds = Math.floor(options.nowMs / 1000)
  if (Math.abs(nowSeconds - Number(timestamp)) > options.toleranceSeconds) {
    throw new UnauthenticatedError(webhookRejectionReasons.timestampOutOfRange)
  }

  if (signature.length > maxSignatureHeaderLength) {
    throw new UnauthenticatedError(webhookRejectionReasons.signatureMismatch)
  }
  const candidates = candidateSignatures(signature)
  const signedContent = Buffer.concat([Buffer.from(`${id}.${timestamp}.`, 'utf8'), body])
  let verified = false
  for (const secret of options.secrets) {
    const expected = createHmac('sha256', secret).update(signedContent).digest()
    if (matchesAny(candidates, expected)) {
      verified = true
    }
  }
  if (!verified) {
    throw new UnauthenticatedError(webhookRejectionReasons.signatureMismatch)
  }
  return id
}
