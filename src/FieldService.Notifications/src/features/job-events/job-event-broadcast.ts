import {
  administratorsRoom,
  type JobEventPayload,
  type NotificationIo,
} from '../../shared/socket-types.ts'
import type { JobEvent } from './job-event-envelope.ts'

export interface AdminBroadcaster {
  broadcast(event: JobEvent): number
}

export function toClientPayload(event: JobEvent): JobEventPayload {
  return {
    eventId: event.id,
    jobId: event.data.jobId,
    jobNumber: event.data.jobNumber,
    status: event.data.status,
    previousStatus: event.data.previousStatus,
    occurredAt: event.occurredAt,
  }
}

export function createAdminBroadcaster(io: NotificationIo): AdminBroadcaster {
  return {
    broadcast: (event) => {
      const recipients = io.sockets.adapter.rooms.get(administratorsRoom)?.size ?? 0
      io.to(administratorsRoom).emit(event.type, toClientPayload(event))
      return recipients
    },
  }
}
