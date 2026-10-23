import type { Server, Socket } from 'socket.io'

export const jobStatuses = [
  'Unassigned',
  'Assigned',
  'InProgress',
  'OnHold',
  'Completed',
  'Cancelled',
] as const

export type JobStatus = (typeof jobStatuses)[number]

export const jobEventNames = ['job.created', 'job.assigned', 'job.status-changed'] as const

export type JobEventName = (typeof jobEventNames)[number]

export const administratorsRoom = 'administrators'

export interface JobEventPayload {
  readonly eventId: string
  readonly jobId: string
  readonly jobNumber: number
  readonly status: JobStatus
  readonly previousStatus: JobStatus | null
  readonly occurredAt: string
}

export type ServerToClientEvents = {
  [Name in JobEventName]: (payload: JobEventPayload) => void
}

export type ClientToServerEvents = Record<string, never>

export type InterServerEvents = Record<string, never>

export interface SocketData {
  userId: string
  connectedAt: number
}

export type NotificationIo = Server<
  ClientToServerEvents,
  ServerToClientEvents,
  InterServerEvents,
  SocketData
>

export type AdminSocket = Socket<
  ClientToServerEvents,
  ServerToClientEvents,
  InterServerEvents,
  SocketData
>
