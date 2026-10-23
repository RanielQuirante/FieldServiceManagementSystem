import type { ExtendedError } from 'socket.io'
import type { Config } from '../../config.ts'
import { failureFields, type Logger } from '../../logging.ts'
import type { Clock } from '../../shared/clock.ts'
import {
  administratorsRoom,
  type NotificationIo,
  type SocketData,
} from '../../shared/socket-types.ts'
import {
  logSocketConnected,
  logSocketDisconnected,
  logSocketRejected,
} from './connection-logging.ts'
import { startSessionCap } from './session-cap.ts'
import {
  createTicketVerifier,
  TicketRejection,
  type TicketRejectionCode,
  type TicketVerifier,
} from './ticket-verifier.ts'
import { createUsedTicketIds } from './used-ticket-ids.ts'

export type AdminConnectionsConfig = Pick<
  Config,
  'socketTicketKey' | 'socketTicketIssuer' | 'socketTicketAudience' | 'socketSessionMaxSeconds'
>

export interface AdminConnectionsDependencies {
  readonly config: AdminConnectionsConfig
  readonly logger: Logger
  readonly clock: Clock
  readonly ticketVerifier?: TicketVerifier
}

export interface HandshakeSocket {
  readonly id: string
  readonly handshake: { readonly auth: unknown }
  data: SocketData
}

export type TicketMiddleware = (
  socket: HandshakeSocket,
  next: (error?: ExtendedError) => void,
) => void

export class ConnectionRefusal extends Error implements ExtendedError {
  readonly data: { readonly code: TicketRejectionCode }

  constructor(code: TicketRejectionCode) {
    super(code)
    this.name = 'ConnectionRefusal'
    this.data = { code }
  }
}

function ticketOf(auth: unknown): unknown {
  if (typeof auth !== 'object' || auth === null || !('ticket' in auth)) {
    return undefined
  }
  return auth.ticket
}

export function createTicketMiddleware(
  verifier: TicketVerifier,
  logger: Logger,
  clock: Clock,
): TicketMiddleware {
  return (socket, next) => {
    verifier.verify(ticketOf(socket.handshake.auth)).then(
      (ticket) => {
        socket.data = { userId: ticket.userId, connectedAt: clock.now() }
        next()
      },
      (error: unknown) => {
        if (error instanceof TicketRejection) {
          logSocketRejected(logger, {
            code: error.code,
            reason: error.reason,
            socketId: socket.id,
            userId: error.userId,
            ticketId: error.ticketId,
          })
          next(new ConnectionRefusal(error.code))
          return
        }
        logger.error(
          {
            ...failureFields(error),
            code: 'ticket_invalid',
            reason: 'verification-error',
            socketId: socket.id,
          },
          'socket.rejected',
        )
        next(new ConnectionRefusal('ticket_invalid'))
      },
    )
  }
}

export function registerAdminConnections(
  io: NotificationIo,
  dependencies: AdminConnectionsDependencies,
): void {
  const { config, logger, clock } = dependencies
  const verifier =
    dependencies.ticketVerifier ??
    createTicketVerifier({
      key: config.socketTicketKey,
      issuer: config.socketTicketIssuer,
      audience: config.socketTicketAudience,
      clock,
      usedTicketIds: createUsedTicketIds(clock),
    })

  io.use(createTicketMiddleware(verifier, logger, clock))

  io.on('connection', (socket) => {
    void socket.join(administratorsRoom)
    logSocketConnected(logger, socket)
    const cancelSessionCap = startSessionCap(socket, config.socketSessionMaxSeconds)
    socket.on('disconnect', (reason) => {
      cancelSessionCap()
      logSocketDisconnected(logger, socket, reason, clock.now() - socket.data.connectedAt)
    })
  })
}
