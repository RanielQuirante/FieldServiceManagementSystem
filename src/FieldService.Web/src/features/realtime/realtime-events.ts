import { isJobStatus, type JobStatus } from '@/shared/format/labels'

export const jobEventNames = ['job.created', 'job.assigned', 'job.status-changed'] as const

export type JobEventName = (typeof jobEventNames)[number]

export interface JobEventPayload {
  readonly eventId: string
  readonly jobId: string
  readonly jobNumber: number
  readonly status: JobStatus
  readonly previousStatus: JobStatus | null
  readonly occurredAt: string
}

export type ServerToClientEvents = {
  [Name in JobEventName]: (payload: unknown) => void
}

export type ClientToServerEvents = Record<string, never>

const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i

const instantPattern = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function isUuid(value: unknown): value is string {
  return typeof value === 'string' && uuidPattern.test(value)
}

function isInstant(value: unknown): value is string {
  if (typeof value !== 'string' || !instantPattern.test(value)) {
    return false
  }
  const parsed = new Date(value)
  return !Number.isNaN(parsed.getTime()) && parsed.toISOString() === value
}

function isJobNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 1
}

export function isJobEventName(value: unknown): value is JobEventName {
  return typeof value === 'string' && (jobEventNames as readonly string[]).includes(value)
}

export function isJobEventPayload(value: unknown): value is JobEventPayload {
  if (!isRecord(value)) {
    return false
  }
  return (
    isUuid(value.eventId) &&
    isUuid(value.jobId) &&
    isJobNumber(value.jobNumber) &&
    isJobStatus(value.status) &&
    'previousStatus' in value &&
    (value.previousStatus === null || isJobStatus(value.previousStatus)) &&
    isInstant(value.occurredAt)
  )
}
