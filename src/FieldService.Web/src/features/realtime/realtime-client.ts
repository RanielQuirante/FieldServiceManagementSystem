import { io, type Socket } from 'socket.io-client'

import { isApiError } from '@/shared/api/api-error'

import { issueSocketTicket, type SocketTicket } from './realtime-api'
import {
  jobEventNames,
  type ClientToServerEvents,
  type JobEventName,
  type ServerToClientEvents,
} from './realtime-events'

export type RealtimeSocket = Socket<ServerToClientEvents, ClientToServerEvents>

export type TicketCallback = (data: object) => void

export type ConnectionPhase = 'connecting' | 'reconnecting' | 'paused'

export type SessionRefreshOutcome = 'refreshed' | 'ended' | 'failed'

export interface RealtimeSessionAccess {
  isSignedIn(): boolean
  refreshSession(): Promise<SessionRefreshOutcome>
  expireSession(): void
}

export interface RealtimeClientHandlers {
  onPhase(phase: ConnectionPhase): void
  onOfflineNotice(): void
  onConnect(): void
  onEvent(name: JobEventName, payload: unknown): void
}

export interface RealtimeClientOptions {
  readonly session: RealtimeSessionAccess
  readonly handlers: RealtimeClientHandlers
  readonly fetchTicket?: () => Promise<SocketTicket>
  readonly random?: () => number
}

export interface RealtimeClient {
  start(): void
  stop(): void
  retryNow(): void
  isRunning(): boolean
  isConnected(): boolean
}

export const realtimePath = '/socket.io'

export const offlineNoticeAfterMilliseconds = 3_000

export const pausedAfterMilliseconds = 30_000

export const backoffInitialMilliseconds = 1_000

export const backoffMaximumMilliseconds = 30_000

export const backoffJitter = 0.25

export const transportRetryInitialMilliseconds = 1_000

export const transportRetryMaximumMilliseconds = 5_000

export const transportRetryRandomization = 0.5

export function createRealtimeSocket(auth: (callback: TicketCallback) => void): RealtimeSocket {
  return io(window.location.origin, {
    path: realtimePath,
    transports: ['websocket'],
    autoConnect: false,
    reconnectionDelay: transportRetryInitialMilliseconds,
    reconnectionDelayMax: transportRetryMaximumMilliseconds,
    randomizationFactor: transportRetryRandomization,
    auth,
  })
}

export function backoffDelay(attempt: number, random: () => number = Math.random): number {
  const base = Math.min(
    backoffInitialMilliseconds * 2 ** Math.max(attempt, 0),
    backoffMaximumMilliseconds,
  )
  const jittered = Math.round(base * (1 - backoffJitter + random() * 2 * backoffJitter))
  return Math.min(Math.max(jittered, backoffInitialMilliseconds), backoffMaximumMilliseconds)
}

export function connectionErrorCode(error: unknown): string | null {
  if (typeof error !== 'object' || error === null || !('data' in error)) {
    return null
  }
  const { data } = error
  if (typeof data !== 'object' || data === null || !('code' in data)) {
    return null
  }
  return typeof data.code === 'string' ? data.code : null
}

function isSessionExpired(error: unknown): boolean {
  return isApiError(error) && error.status === 401 && error.code === 'auth.session-expired'
}

function retryAfterMilliseconds(error: unknown): number {
  if (!isApiError(error) || error.status !== 429) {
    return 0
  }
  return (error.retryAfterSeconds ?? 60) * 1_000
}

export function createRealtimeClient(options: RealtimeClientOptions): RealtimeClient {
  const { session, handlers } = options
  const fetchTicket = options.fetchTicket ?? issueSocketTicket
  const random = options.random ?? Math.random

  let socket: RealtimeSocket | null = null
  let running = false
  let connected = false
  let phase: ConnectionPhase | 'connected' | 'stopped' = 'stopped'
  let attempt = 0
  let refreshedAfterInvalid = false
  let forbiddenRefusals = 0
  let halted = false
  let ticketFailure: unknown = null
  let retryTimer: ReturnType<typeof setTimeout> | undefined
  let noticeTimer: ReturnType<typeof setTimeout> | undefined
  let pausedTimer: ReturnType<typeof setTimeout> | undefined

  function setPhase(next: ConnectionPhase): void {
    if (phase === next) {
      return
    }
    phase = next
    handlers.onPhase(next)
  }

  function clearRetry(): void {
    if (retryTimer !== undefined) {
      clearTimeout(retryTimer)
      retryTimer = undefined
    }
  }

  function clearOfflineTimers(): void {
    if (noticeTimer !== undefined) {
      clearTimeout(noticeTimer)
      noticeTimer = undefined
    }
    if (pausedTimer !== undefined) {
      clearTimeout(pausedTimer)
      pausedTimer = undefined
    }
  }

  function startOfflineTimers(): void {
    if (pausedTimer !== undefined || phase === 'paused') {
      return
    }
    noticeTimer = setTimeout(() => {
      noticeTimer = undefined
      if (running && !connected) {
        handlers.onOfflineNotice()
      }
    }, offlineNoticeAfterMilliseconds)
    pausedTimer = setTimeout(() => {
      pausedTimer = undefined
      if (running && !connected) {
        setPhase('paused')
      }
    }, pausedAfterMilliseconds)
  }

  function resetRetries(): void {
    attempt = 0
    refreshedAfterInvalid = false
    forbiddenRefusals = 0
    halted = false
  }

  function requestTicket(callback: TicketCallback): void {
    ticketFailure = null
    fetchTicket().then(
      ({ ticket }) => {
        callback({ ticket })
      },
      (error: unknown) => {
        ticketFailure = error
        if (running && isSessionExpired(error) && session.isSignedIn()) {
          session.expireSession()
        }
        callback({})
      },
    )
  }

  function connectionLost(): void {
    if (phase !== 'paused') {
      setPhase('reconnecting')
    }
    if (!halted) {
      startOfflineTimers()
    }
  }

  function scheduleRetry(): void {
    if (halted) {
      return
    }
    clearRetry()
    const delay = Math.max(backoffDelay(attempt, random), retryAfterMilliseconds(ticketFailure))
    attempt += 1
    retryTimer = setTimeout(() => {
      retryTimer = undefined
      if (running && !connected && !halted) {
        socket?.connect()
      }
    }, delay)
  }

  function halt(): void {
    halted = true
    clearRetry()
    clearOfflineTimers()
    setPhase('paused')
  }

  async function refreshThenConnect(): Promise<void> {
    const outcome = await session.refreshSession()
    if (!running || connected || halted) {
      return
    }
    if (outcome === 'refreshed') {
      socket?.connect()
    } else if (outcome === 'failed') {
      scheduleRetry()
    }
  }

  function onConnect(): void {
    connected = true
    resetRetries()
    clearRetry()
    clearOfflineTimers()
    if (!running) {
      return
    }
    phase = 'connected'
    handlers.onConnect()
  }

  function onDisconnect(reason: string): void {
    connected = false
    if (!running) {
      return
    }
    connectionLost()
    if (reason === 'io server disconnect' && session.isSignedIn()) {
      socket?.connect()
    }
  }

  function onConnectError(error: Error): void {
    connected = false
    if (!running) {
      return
    }
    connectionLost()
    if (socket === null || socket.active || isSessionExpired(ticketFailure)) {
      return
    }
    const code = connectionErrorCode(error)
    if (code === 'ticket_invalid' && !refreshedAfterInvalid) {
      refreshedAfterInvalid = true
      void refreshThenConnect()
      return
    }
    if (code === 'forbidden') {
      forbiddenRefusals += 1
      if (forbiddenRefusals === 1) {
        void refreshThenConnect()
      } else {
        halt()
      }
      return
    }
    scheduleRetry()
  }

  function onServerEvent(name: JobEventName, payload: unknown): void {
    if (running && connected) {
      handlers.onEvent(name, payload)
    }
  }

  function ensureSocket(): RealtimeSocket {
    if (socket !== null) {
      return socket
    }
    const created = createRealtimeSocket(requestTicket)
    created.on('connect', onConnect)
    created.on('disconnect', onDisconnect)
    created.on('connect_error', onConnectError)
    for (const name of jobEventNames) {
      created.on(name, (payload: unknown) => {
        onServerEvent(name, payload)
      })
    }
    socket = created
    return created
  }

  function start(): void {
    if (running) {
      return
    }
    running = true
    connected = false
    resetRetries()
    phase = 'stopped'
    setPhase('connecting')
    startOfflineTimers()
    ensureSocket().connect()
  }

  function stop(): void {
    running = false
    connected = false
    phase = 'stopped'
    clearRetry()
    clearOfflineTimers()
    socket?.disconnect()
  }

  function retryNow(): void {
    if (!running || connected) {
      return
    }
    resetRetries()
    clearRetry()
    const current = ensureSocket()
    current.disconnect()
    current.connect()
  }

  return {
    start,
    stop,
    retryNow,
    isRunning: () => running,
    isConnected: () => connected,
  }
}
