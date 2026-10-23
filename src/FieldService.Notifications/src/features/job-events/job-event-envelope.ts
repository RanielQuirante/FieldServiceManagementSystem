import { z } from 'zod'
import { InvalidPayloadError } from '../../shared/http/http-errors.ts'
import { jobEventNames, jobStatuses, type JobEventName } from '../../shared/socket-types.ts'

export const envelopeRejectionReasons = {
  invalidJson: 'invalid-json',
  schema: 'schema',
  idMismatch: 'id-mismatch',
} as const

export const jobStatus = z.enum(jobStatuses)

export const jobEventDataV1 = z.object({
  jobId: z.uuid(),
  jobNumber: z.number().int().min(1).max(Number.MAX_SAFE_INTEGER),
  status: jobStatus,
  previousStatus: jobStatus.nullable(),
})

export const jobEventEnvelope = z.object({
  id: z.uuid(),
  type: z.string().min(1).max(64),
  version: z.number().int().min(1),
  occurredAt: z.iso.datetime({ precision: 3 }),
  correlationId: z.string().regex(/^[A-Za-z0-9._-]{1,64}$/),
  data: z.record(z.string(), z.unknown()),
})

export const knownJobEventTypes: ReadonlySet<string> = new Set<string>(jobEventNames)

export const supportedJobEventVersion = 1

export type JobEventEnvelope = z.infer<typeof jobEventEnvelope>

export type JobEventDataV1 = z.infer<typeof jobEventDataV1>

export interface JobEvent {
  readonly id: string
  readonly type: JobEventName
  readonly version: typeof supportedJobEventVersion
  readonly occurredAt: string
  readonly correlationId: string
  readonly data: JobEventDataV1
}

export interface ParsedEnvelope {
  readonly envelope: JobEventEnvelope
  readonly event: JobEvent | null
}

const utf8 = new TextDecoder('utf-8', { fatal: true, ignoreBOM: true })

function isKnownJobEventType(type: string): type is JobEventName {
  return knownJobEventTypes.has(type)
}

function parseJson(body: Uint8Array): unknown {
  try {
    return JSON.parse(utf8.decode(body))
  } catch {
    throw new InvalidPayloadError(envelopeRejectionReasons.invalidJson)
  }
}

export function parseEnvelope(body: Uint8Array): ParsedEnvelope {
  const parsed = jobEventEnvelope.safeParse(parseJson(body))
  if (!parsed.success) {
    throw new InvalidPayloadError(envelopeRejectionReasons.schema)
  }
  const envelope = parsed.data
  const { type } = envelope
  if (!isKnownJobEventType(type) || envelope.version !== supportedJobEventVersion) {
    return { envelope, event: null }
  }

  const data = jobEventDataV1.safeParse(envelope.data)
  if (!data.success) {
    throw new InvalidPayloadError(envelopeRejectionReasons.schema)
  }
  return {
    envelope,
    event: {
      id: envelope.id,
      type,
      version: supportedJobEventVersion,
      occurredAt: envelope.occurredAt,
      correlationId: envelope.correlationId,
      data: data.data,
    },
  }
}
