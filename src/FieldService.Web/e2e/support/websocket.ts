import type { Page, WebSocketRoute } from '@playwright/test'

export const realtimeSocketPattern = /\/socket\.io\//

export const handshakeQueryKeys: readonly string[] = ['EIO', 'transport']

export const refusalFrame = 'e2e-refused'

const normalClosure = 1000

const printableKeyName = /^[A-Za-z][A-Za-z0-9_-]{0,31}$/

export interface HandshakeChecker {
  checkSocketUrl(url: string): void
}

interface OpenConnection {
  readonly page: WebSocketRoute
  readonly server: WebSocketRoute
}

export interface GateCounts {
  readonly passed: number
  readonly refused: number
}

function keyLabel(key: string): string {
  return printableKeyName.test(key) ? key : `a key of ${key.length} characters`
}

export function isRealtimeHandshake(url: string): boolean {
  return realtimeSocketPattern.test(new URL(url).pathname)
}

export function handshakeUrlProblem(url: string): string | null {
  const parsed = new URL(url)
  if (!realtimeSocketPattern.test(parsed.pathname)) {
    return null
  }
  const keys = [...parsed.searchParams.keys()]
  const extraKeys = keys.filter((key) => !handshakeQueryKeys.includes(key))
  const repeatedKeys = keys.length !== new Set(keys).size
  const extras = [
    ...(parsed.hash === '' ? [] : ['a fragment']),
    ...(parsed.username === '' && parsed.password === '' ? [] : ['user information']),
  ]
  if (extraKeys.length === 0 && !repeatedKeys && extras.length === 0) {
    return null
  }
  const keyList =
    keys.length === 0 ? 'no query keys' : `the query keys ${keys.map(keyLabel).join(', ')}`
  const extraText = extras.length === 0 ? '' : ` and ${extras.join(' and ')}`
  return `A Socket.IO handshake URL carries ${keyList}${extraText}; only ${handshakeQueryKeys.join(' and ')}, each once, are allowed.`
}

export class SocketGate {
  private allowingFrom: number | null = 0
  private passed = 0
  private refused = 0
  private readonly open = new Set<OpenConnection>()

  async install(page: Page, checker: HandshakeChecker): Promise<void> {
    await page.routeWebSocket(realtimeSocketPattern, (route) => {
      checker.checkSocketUrl(route.url())
      this.handle(route)
    })
  }

  counts(): GateCounts {
    return { passed: this.passed, refused: this.refused }
  }

  async drop(): Promise<number> {
    this.allowingFrom = null
    const closing = [...this.open]
    this.open.clear()
    for (const connection of closing) {
      await connection.server.close({ code: normalClosure, reason: 'e2e drop' })
      await connection.page.close({ code: normalClosure, reason: 'e2e drop' })
    }
    return closing.length
  }

  allow(): void {
    this.allowingFrom = 0
  }

  allowFrom(at: number): void {
    this.allowingFrom = at
  }

  refuse(): void {
    this.allowingFrom = null
  }

  private handle(route: WebSocketRoute): void {
    if (this.allowingFrom === null || Date.now() < this.allowingFrom) {
      this.refused += 1
      route.send(refusalFrame)
      return
    }
    this.passed += 1
    const connection: OpenConnection = { page: route, server: route.connectToServer() }
    this.open.add(connection)
  }
}
