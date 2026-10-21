import { vi } from 'vitest'

export interface RecordedRequest {
  readonly method: string
  readonly url: string
  readonly path: string
  readonly headers: Headers
  readonly body: string
}

export type Reply = (request: RecordedRequest) => Response | Promise<Response>

export type RouteTable = Readonly<Record<string, Reply | readonly Reply[]>>

export interface FetchStub {
  readonly requests: RecordedRequest[]
  requestsTo(route: string): RecordedRequest[]
}

export interface Deferred {
  readonly reply: Reply
  readonly requested: Promise<RecordedRequest>
  resolve(response: Response): void
}

const typeUris: Readonly<Record<number, string>> = {
  400: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
  401: 'https://tools.ietf.org/html/rfc9110#section-15.5.2',
  403: 'https://tools.ietf.org/html/rfc9110#section-15.5.4',
  404: 'https://tools.ietf.org/html/rfc9110#section-15.5.5',
  409: 'https://tools.ietf.org/html/rfc9110#section-15.5.10',
  429: 'https://tools.ietf.org/html/rfc6585#section-4',
  500: 'https://tools.ietf.org/html/rfc9110#section-15.6.1',
}

function routeKey(method: string, path: string): string {
  return `${method.toUpperCase()} ${path}`
}

function correlationOf(request: RecordedRequest): string {
  return request.headers.get('X-Correlation-ID') ?? crypto.randomUUID()
}

export function json(body: unknown, status = 200, headers: Record<string, string> = {}): Reply {
  return (request) =>
    new Response(JSON.stringify(body), {
      status,
      headers: {
        'Content-Type': 'application/json; charset=utf-8',
        'X-Correlation-ID': correlationOf(request),
        ...headers,
      },
    })
}

export function problem(
  status: number,
  code: string,
  members: Record<string, unknown> = {},
  headers: Record<string, string> = {},
): Reply {
  return (request) => {
    const correlationId = correlationOf(request)
    const body = {
      type: typeUris[status] ?? typeUris[500],
      title: 'Problem title',
      status,
      instance: request.path,
      code,
      correlationId,
      traceId: `00-${crypto.randomUUID().replace(/-/g, '')}-0000000000000000-00`,
      ...members,
    }
    return new Response(JSON.stringify(body), {
      status,
      headers: {
        'Content-Type': 'application/problem+json',
        'X-Correlation-ID': correlationId,
        ...headers,
      },
    })
  }
}

export function text(body: string, status: number): Reply {
  return (request) =>
    new Response(body, {
      status,
      headers: { 'Content-Type': 'text/html', 'X-Correlation-ID': correlationOf(request) },
    })
}

export function noContent(): Reply {
  return (request) =>
    new Response(null, { status: 204, headers: { 'X-Correlation-ID': correlationOf(request) } })
}

export function networkFailure(): Reply {
  return () => {
    throw new TypeError('fetch failed')
  }
}

export function deferred(): Deferred {
  let release: (response: Response) => void = () => undefined
  let announce: (request: RecordedRequest) => void = () => undefined
  const response = new Promise<Response>((resolve) => {
    release = resolve
  })
  const requested = new Promise<RecordedRequest>((resolve) => {
    announce = resolve
  })
  return {
    reply: (request) => {
      announce(request)
      return response
    },
    requested,
    resolve: (value) => release(value),
  }
}

export function stubFetch(routes: RouteTable): FetchStub {
  const requests: RecordedRequest[] = []
  const calls = new Map<string, number>()

  const handler = vi.fn(async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
    const request =
      input instanceof Request && init === undefined ? input : new Request(input, init)
    const url = new URL(request.url)
    const recorded: RecordedRequest = {
      method: request.method,
      url: request.url,
      path: url.pathname,
      headers: new Headers(request.headers),
      body: await request.text(),
    }
    requests.push(recorded)
    const key = routeKey(recorded.method, recorded.path)
    const route = routes[key]
    if (route === undefined) {
      throw new Error(`No stubbed route for ${key}`)
    }
    const count = calls.get(key) ?? 0
    calls.set(key, count + 1)
    const replies: readonly Reply[] = typeof route === 'function' ? [route] : route
    const reply = replies[Math.min(count, replies.length - 1)]
    if (reply === undefined) {
      throw new Error(`No reply configured for ${key}`)
    }
    return reply(recorded)
  })

  vi.stubGlobal('fetch', handler)

  return {
    requests,
    requestsTo: (route) => {
      const [method = '', path = ''] = route.split(' ')
      return requests.filter(
        (request) => routeKey(request.method, request.path) === routeKey(method, path),
      )
    },
  }
}
