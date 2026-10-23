import { createServer, type Server as HttpServer } from 'node:http'
import { Server } from 'socket.io'
import type { Config } from './config.ts'
import { registerAdminConnections } from './features/admin-connections/admin-connections.ts'
import { createOriginCheck } from './features/admin-connections/origin-check.ts'
import { registerHealthRoutes } from './features/health/health-routes.ts'
import { createAdminBroadcaster } from './features/job-events/job-event-broadcast.ts'
import { registerJobEventsRoute } from './features/job-events/job-events-route.ts'
import { createRecentEventIds } from './features/job-events/recent-event-ids.ts'
import type { Logger } from './logging.ts'
import type { Clock } from './shared/clock.ts'
import { createRouter } from './shared/http/router.ts'
import type { NotificationIo } from './shared/socket-types.ts'

export interface NotificationServerDependencies {
  readonly config: Config
  readonly logger: Logger
  readonly clock: Clock
}

export interface ListeningAddress {
  readonly host: string
  readonly port: number
}

export interface NotificationServer {
  readonly httpServer: HttpServer
  readonly io: NotificationIo
  listen(): Promise<ListeningAddress>
  close(): Promise<void>
}

export const socketIoPath = '/socket.io'
export const maxSocketMessageBytes = 4096
export const socketConnectTimeoutMs = 10_000

function boundPort(httpServer: HttpServer, fallback: number): number {
  const address = httpServer.address()
  return address !== null && typeof address === 'object' ? address.port : fallback
}

export function createNotificationServer(
  dependencies: NotificationServerDependencies,
): NotificationServer {
  const { config, logger, clock } = dependencies
  const router = createRouter(logger)
  const httpServer = createServer(router.handle)
  httpServer.headersTimeout = 10_000
  httpServer.requestTimeout = 15_000

  const io: NotificationIo = new Server(httpServer, {
    path: socketIoPath,
    transports: ['websocket'],
    serveClient: false,
    maxHttpBufferSize: maxSocketMessageBytes,
    connectTimeout: socketConnectTimeoutMs,
    allowEIO3: false,
    perMessageDeflate: false,
    allowRequest: createOriginCheck(config.socketAllowedOrigins, logger),
  })

  let closing = false
  registerHealthRoutes(router, { isReady: () => httpServer.listening && !closing })
  registerJobEventsRoute(router, {
    config,
    logger,
    clock,
    recentEventIds: createRecentEventIds(clock, config.webhookToleranceSeconds),
    broadcaster: createAdminBroadcaster(io),
  })
  registerAdminConnections(io, { config, logger, clock })

  return {
    httpServer,
    io,
    listen: () =>
      new Promise<ListeningAddress>((resolve, reject) => {
        const onError = (error: Error): void => {
          reject(error)
        }
        httpServer.once('error', onError)
        httpServer.listen(config.port, config.host, () => {
          httpServer.off('error', onError)
          resolve({ host: config.host, port: boundPort(httpServer, config.port) })
        })
      }),
    close: async () => {
      closing = true
      await io.close()
    },
  }
}
