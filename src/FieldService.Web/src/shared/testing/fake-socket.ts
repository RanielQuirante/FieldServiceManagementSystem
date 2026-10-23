type Listener = (...args: unknown[]) => void

type TicketCallback = (data: object) => void

type AuthOption = Record<string, unknown> | ((callback: TicketCallback) => void)

export interface FakeSocketOptions {
  readonly path?: string
  readonly transports?: readonly string[]
  readonly autoConnect?: boolean
  readonly auth?: AuthOption
  readonly [option: string]: unknown
}

export interface RefusalError extends Error {
  data?: { code: string }
}

export class FakeSocket {
  readonly uri: string
  readonly options: FakeSocketOptions
  readonly handshakes: object[] = []
  connected = false
  active = false
  connectCalls = 0
  disconnectCalls = 0

  private readonly listeners = new Map<string, Listener[]>()

  constructor(uri: string, options: FakeSocketOptions) {
    this.uri = uri
    this.options = options
  }

  on(event: string, listener: Listener): this {
    this.listeners.set(event, [...(this.listeners.get(event) ?? []), listener])
    return this
  }

  off(event: string, listener: Listener): this {
    this.listeners.set(
      event,
      (this.listeners.get(event) ?? []).filter((candidate) => candidate !== listener),
    )
    return this
  }

  listenerCount(event: string): number {
    return this.listeners.get(event)?.length ?? 0
  }

  connect(): this {
    this.connectCalls += 1
    this.active = true
    return this
  }

  disconnect(): this {
    this.disconnectCalls += 1
    const wasConnected = this.connected
    this.connected = false
    this.active = false
    if (wasConnected) {
      this.dispatch('disconnect', 'io client disconnect')
    }
    return this
  }

  runAuth(): Promise<object> {
    const auth = this.options.auth
    if (typeof auth !== 'function') {
      const data = auth ?? {}
      this.handshakes.push(data)
      return Promise.resolve(data)
    }
    return new Promise((resolve) => {
      auth((data) => {
        this.handshakes.push(data)
        resolve(data)
      })
    })
  }

  async simulateConnect(): Promise<object> {
    const data = await this.runAuth()
    this.connected = true
    this.active = true
    this.dispatch('connect')
    return data
  }

  async simulateConnectError(code: string | null): Promise<object> {
    const data = await this.runAuth()
    const error: RefusalError = new Error(code ?? 'Invalid namespace')
    if (code !== null) {
      error.data = { code }
    }
    this.connected = false
    this.active = false
    this.dispatch('connect_error', error)
    return data
  }

  simulateTransportError(): void {
    this.connected = false
    this.active = true
    this.dispatch('connect_error', new Error('websocket error'))
  }

  simulateDisconnect(reason: string): void {
    this.connected = false
    this.active = reason !== 'io server disconnect' && reason !== 'io client disconnect'
    this.dispatch('disconnect', reason)
  }

  emitServerEvent(name: string, payload: unknown): void {
    this.dispatch(name, payload)
  }

  private dispatch(event: string, ...args: unknown[]): void {
    for (const listener of [...(this.listeners.get(event) ?? [])]) {
      listener(...args)
    }
  }
}

export const fakeSockets: FakeSocket[] = []

export function fakeIo(uri: string, options: FakeSocketOptions = {}): FakeSocket {
  const socket = new FakeSocket(uri, options)
  fakeSockets.push(socket)
  return socket
}

export function lastFakeSocket(): FakeSocket {
  const socket = fakeSockets[fakeSockets.length - 1]
  if (socket === undefined) {
    throw new Error('No socket was created')
  }
  return socket
}

export function resetFakeSockets(): void {
  fakeSockets.length = 0
}
