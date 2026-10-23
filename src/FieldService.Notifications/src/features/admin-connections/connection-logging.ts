import type { Logger } from '../../logging.ts'
import type { AdminSocket } from '../../shared/socket-types.ts'
import type { TicketRejectionCode, TicketRejectionReason } from './ticket-verifier.ts'

export interface SocketRejection {
  readonly code?: TicketRejectionCode
  readonly reason: TicketRejectionReason | 'origin-not-allowed'
  readonly socketId?: string
  readonly userId?: string
  readonly ticketId?: string
}

export function logSocketConnected(logger: Logger, socket: AdminSocket): void {
  logger.info({ socketId: socket.id, userId: socket.data.userId }, 'socket.connected')
}

export function logSocketDisconnected(
  logger: Logger,
  socket: AdminSocket,
  reason: string,
  durationMs: number,
): void {
  logger.info(
    { socketId: socket.id, userId: socket.data.userId, reason, durationMs },
    'socket.disconnected',
  )
}

export function logSocketRejected(logger: Logger, rejection: SocketRejection): void {
  logger.warn(
    {
      code: rejection.code,
      reason: rejection.reason,
      socketId: rejection.socketId,
      userId: rejection.userId,
      ticketId: rejection.ticketId,
    },
    'socket.rejected',
  )
}
