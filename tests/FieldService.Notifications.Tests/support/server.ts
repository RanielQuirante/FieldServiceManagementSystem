import { io, Manager, type ManagerOptions, type Socket, type SocketOptions } from 'socket.io-client'
import { loadConfig } from '../../../src/FieldService.Notifications/src/config.ts'
import { createLogger } from '../../../src/FieldService.Notifications/src/logging.ts'
import {
  createNotificationServer,
  type NotificationServer,
} from '../../../src/FieldService.Notifications/src/server.ts'
import type {
  ClientToServerEvents,
  JobEventName,
  JobEventPayload,
  ServerToClientEvents,
} from '../../../src/FieldService.Notifications/src/shared/socket-types.ts'
import { createFakeClock, type FakeClock } from './clock.ts'
import { createTestEnvironment, type Environment, type JsonObject } from './fixtures.ts'
import { createMemoryLog, type MemoryLog } from './memory-log.ts'
import { boundPort } from './ports.ts'
import { signedHeaders, type SignedRequestOptions } from './signing.ts'
import { mintTicket, ticketKeyBytes, type TicketOverrides } from './tickets.ts'

export const jobEventsPath = '/internal/job-events'
export const allowedOrigin = 'http://localhost:5173'
export const foreignOrigin = 'http://evil.example'

export interface TestServer {
  readonly baseUrl: string
  readonly port: number
  readonly io: NotificationServer['io']
  readonly httpServer: NotificationServer['httpServer']
  readonly logs: MemoryLog
  readonly clock: FakeClock
  readonly environment: Environment
  readonly webhookSecret: string
  readonly ticketKey: Uint8Array
  close(): Promise<void>
}

function required(environment: Environment, name: string): string {
  const value = environment[name]
  if (value === undefined) {
    throw new Error(`the test environment has no ${name}`)
  }
  return value
}

export async function startServer(
  environmentOverrides: Environment = {},
  clock: FakeClock = createFakeClock(),
): Promise<TestServer> {
  const environment = createTestEnvironment(environmentOverrides)
  const config = loadConfig(environment)
  const logs = createMemoryLog()
  const logger = createLogger(config, logs.destination)
  const server = createNotificationServer({ config, logger, clock })
  await server.listen()
  const port = boundPort(server.httpServer)

  return {
    baseUrl: `http://127.0.0.1:${port}`,
    port,
    io: server.io,
    httpServer: server.httpServer,
    logs,
    clock,
    environment,
    webhookSecret: required(environment, 'WEBHOOK_SECRETS').split(' ')[0] ?? '',
    ticketKey: ticketKeyBytes(required(environment, 'SOCKET_TICKET_KEY')),
    close: () => server.close(),
  }
}

export function postJobEvent(
  server: TestServer,
  body: string | Uint8Array,
  options: SignedRequestOptions = {},
): Promise<Response> {
  return fetch(`${server.baseUrl}${jobEventsPath}`, {
    method: 'POST',
    headers: signedHeaders(body, server.webhookSecret, server.clock.now(), options),
    body,
  })
}

export interface Problem {
  readonly type: string
  readonly title: string
  readonly status: number
  readonly code: string
  readonly correlationId?: string
}

export async function problemOf(response: Response): Promise<Problem> {
  if (response.headers.get('content-type') !== 'application/problem+json') {
    throw new Error(`expected a problem, got ${response.headers.get('content-type') ?? 'nothing'}`)
  }
  return (await response.json()) as Problem
}

export async function outcomeOf(response: Response): Promise<unknown> {
  if (response.headers.get('content-type') !== 'application/json') {
    throw new Error(`expected JSON, got ${response.headers.get('content-type') ?? 'nothing'}`)
  }
  return response.json()
}

export function postEnvelope(
  server: TestServer,
  envelope: JsonObject,
  options: SignedRequestOptions = {},
): Promise<Response> {
  const id = typeof envelope.id === 'string' ? envelope.id : undefined
  return postJobEvent(server, JSON.stringify(envelope), { id, ...options })
}

export function ticketFor(server: TestServer, overrides: TicketOverrides = {}): Promise<string> {
  return mintTicket(server.ticketKey, server.clock.now(), overrides)
}

export type ClientSocket = Socket<ServerToClientEvents, ClientToServerEvents>

export interface ConnectError extends Error {
  readonly data?: unknown
}

export interface ClientOptions {
  readonly auth?: Record<string, unknown>
  readonly origin?: string
  readonly headers?: Readonly<Record<string, string>>
  readonly namespace?: string
}

function clientOptions(options: ClientOptions): Partial<ManagerOptions & SocketOptions> {
  return {
    path: '/socket.io',
    transports: ['websocket'],
    reconnection: false,
    forceNew: true,
    auth: options.auth ?? {},
    extraHeaders: {
      ...(options.origin === undefined ? {} : { origin: options.origin }),
      ...options.headers,
    },
  }
}

export function openClient(server: TestServer, options: ClientOptions = {}): ClientSocket {
  return io(`${server.baseUrl}${options.namespace ?? ''}`, clientOptions(options))
}

export function openUntypedClient(server: TestServer, options: ClientOptions = {}): Socket {
  return io(`${server.baseUrl}${options.namespace ?? ''}`, clientOptions(options))
}

export function openManagerWithoutNamespace(server: TestServer): Manager {
  return new Manager(server.baseUrl, {
    path: '/socket.io',
    transports: ['websocket'],
    reconnection: false,
    autoConnect: false,
  })
}

function withTimeout<Value>(
  description: string,
  timeoutMs: number,
  start: (resolve: (value: Value) => void, reject: (error: Error) => void) => () => void,
): Promise<Value> {
  return new Promise<Value>((resolve, reject) => {
    let stop: () => void = () => undefined
    const timer = setTimeout(() => {
      stop()
      reject(new Error(`timed out waiting for ${description}`))
    }, timeoutMs)
    stop = start(
      (value) => {
        clearTimeout(timer)
        stop()
        resolve(value)
      },
      (error) => {
        clearTimeout(timer)
        stop()
        reject(error)
      },
    )
  })
}

export function connected(socket: ClientSocket, timeoutMs = 5_000): Promise<void> {
  return withTimeout('the socket to connect', timeoutMs, (resolve, reject) => {
    const onConnect = (): void => {
      resolve(undefined)
    }
    const onError = (error: Error): void => {
      reject(error)
    }
    socket.once('connect', onConnect)
    socket.once('connect_error', onError)
    return () => {
      socket.off('connect', onConnect)
      socket.off('connect_error', onError)
    }
  })
}

export function refused(socket: ClientSocket, timeoutMs = 5_000): Promise<ConnectError> {
  return withTimeout('the socket to be refused', timeoutMs, (resolve, reject) => {
    const onConnect = (): void => {
      reject(new Error('the socket connected'))
    }
    const onError = (error: ConnectError): void => {
      resolve(error)
    }
    socket.once('connect', onConnect)
    socket.once('connect_error', onError)
    return () => {
      socket.off('connect', onConnect)
      socket.off('connect_error', onError)
    }
  })
}

export function disconnected(socket: ClientSocket, timeoutMs = 5_000): Promise<string> {
  return withTimeout('the socket to disconnect', timeoutMs, (resolve) => {
    const onDisconnect = (reason: string): void => {
      resolve(reason)
    }
    socket.once('disconnect', onDisconnect)
    return () => {
      socket.off('disconnect', onDisconnect)
    }
  })
}

export function received(
  socket: ClientSocket,
  eventName: JobEventName,
  timeoutMs = 5_000,
): Promise<JobEventPayload> {
  return withTimeout(`the ${eventName} event`, timeoutMs, (resolve) => {
    const onEvent = (payload: JobEventPayload): void => {
      resolve(payload)
    }
    socket.once(eventName, onEvent)
    return () => {
      socket.off(eventName, onEvent)
    }
  })
}

export interface RecordedEvent {
  readonly name: string
  readonly payload: unknown
}

export function recordEvents(socket: ClientSocket): RecordedEvent[] {
  const events: RecordedEvent[] = []
  socket.onAny((name: string, payload: unknown) => {
    events.push({ name, payload })
  })
  return events
}

export async function connectAdministrator(
  server: TestServer,
  options: ClientOptions = {},
): Promise<ClientSocket> {
  const ticket = await ticketFor(server)
  const socket = openClient(server, { ...options, auth: { ticket } })
  try {
    await connected(socket)
  } catch (error) {
    socket.close()
    throw error
  }
  return socket
}

export function closeClients(sockets: readonly ClientSocket[]): void {
  for (const socket of sockets) {
    socket.close()
  }
}
