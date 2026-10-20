import { createServer, type IncomingMessage, type Server as HttpServer } from 'node:http'
import { Server } from 'socket.io'
import type { Config } from './config.ts'
import { registerHealthRoutes } from './features/health/health-routes.ts'
import type { Logger } from './logging.ts'
import type { Clock } from './shared/clock.ts'
import { createRouter } from './shared/http/router.ts'

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
  readonly io: Server
  listen(): Promise<ListeningAddress>
  close(): Promise<void>
}

function refuseHandshakes(
  _request: IncomingMessage,
  callback: (error: string | null | undefined, success: boolean) => void,
): void {
  callback(null, false)
}

function boundPort(httpServer: HttpServer, fallback: number): number {
  const address = httpServer.address()
  return address !== null && typeof address === 'object' ? address.port : fallback
}

export function createNotificationServer(
  dependencies: NotificationServerDependencies,
): NotificationServer {
  const { config, logger } = dependencies
  const router = createRouter(logger)
  const httpServer = createServer(router.handle)
  httpServer.headersTimeout = 10_000
  httpServer.requestTimeout = 15_000

  const io = new Server(httpServer, {
    path: '/socket.io',
    transports: ['websocket'],
    serveClient: false,
    maxHttpBufferSize: 4096,
    connectTimeout: 10_000,
    allowRequest: refuseHandshakes,
  })

  let closing = false
  registerHealthRoutes(router, { isReady: () => httpServer.listening && !closing })

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
