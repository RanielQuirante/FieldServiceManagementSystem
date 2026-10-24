import { readFileSync } from 'node:fs'

import { flushPromises } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, onMounted, type App } from 'vue'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'

import { listSourceFiles, readSource } from '@/app/styles/__tests__/stylesheet-source'
import {
  appVersionPatternText,
  buildClientErrorReport,
  clientAppVersion,
  clientErrorRoute,
  correlationIdPatternText,
  isJsonParseFailure,
  messageMaximumLength,
  reportError,
  reportLimit,
  reportWindowMilliseconds,
  rememberedReports,
  routeMaximumLength,
  routePatternText,
  scrubClientErrorText,
  stackFrames,
  stackMaximumLength,
  thrownStringMessage,
  unreadableResponseMessage,
  withoutQuotedText,
  type ClientErrorReport,
  type ErrorSource,
} from '@/features/diagnostics/error-reporter'
import { useJobsStore } from '@/features/jobs/jobs-store'
import { signedIn } from '@/features/realtime/__tests__/realtime-fixtures'
import { ApiError } from '@/shared/api/api-error'
import { errorToastBody, errorToastTitle, useToasts } from '@/shared/composables/use-toasts'
import { contractFile, contractProblems } from '@/shared/testing/contract-schema'
import {
  deferred,
  networkFailure,
  problem,
  stubFetch,
  type FetchStub,
  type RecordedRequest,
  type Reply,
} from '@/shared/testing/fetch-stub'

import { version as packageVersion } from '../../../package.json'
import { installErrorHandling, type RemoveErrorHandling } from '../error-handling'

vi.mock('@/features/diagnostics/error-reporter', async (importOriginal) => {
  const original = await importOriginal<typeof import('@/features/diagnostics/error-reporter')>()
  return { ...original, reportError: vi.fn(original.reportError) }
})

const EmptyPage = defineComponent({ render: () => h('div') })

function createTestRouter(failure: Error): Router {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: EmptyPage },
      { path: '/jobs', component: EmptyPage },
      { path: '/broken', component: () => Promise.reject(failure) },
    ],
  })
}

function unhandledRejection(reason: unknown): Event {
  const event = new Event('unhandledrejection')
  Object.defineProperty(event, 'reason', { value: reason })
  return event
}

describe('installErrorHandling', () => {
  let removeErrorHandling: RemoveErrorHandling | undefined

  beforeEach(() => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined)
  })

  afterEach(() => {
    removeErrorHandling?.()
    removeErrorHandling = undefined
  })

  it('reports an error thrown inside a component through app.config.errorHandler', async () => {
    const failure = new Error('Mounted hook failed')
    const router = createTestRouter(new Error('unused'))
    await router.push('/jobs')
    const FailingComponent = defineComponent({
      setup() {
        onMounted(() => {
          throw failure
        })
        return () => h('div')
      },
    })
    const app = createApp(FailingComponent)
    removeErrorHandling = installErrorHandling(app, router)

    app.mount(document.createElement('div'))
    app.unmount()

    expect(reportError).toHaveBeenCalledTimes(1)
    expect(reportError).toHaveBeenCalledWith(failure, 'app.errorHandler', '/jobs')
  })

  it('reports an unhandled promise rejection through the window unhandledrejection event', () => {
    const failure = new Error('Request promise rejected')
    const router = createTestRouter(new Error('unused'))
    removeErrorHandling = installErrorHandling(createApp(EmptyPage), router)

    window.dispatchEvent(unhandledRejection(failure))

    expect(reportError).toHaveBeenCalledTimes(1)
    expect(reportError).toHaveBeenCalledWith(failure, 'window.unhandledrejection', '/')
  })

  it('reports a failed navigation through router.onError with the target route path', async () => {
    const failure = new Error('Route component failed to load')
    const router = createTestRouter(failure)
    removeErrorHandling = installErrorHandling(createApp(EmptyPage), router)

    await expect(router.push('/broken?tab=history#notes')).rejects.toBe(failure)

    expect(reportError).toHaveBeenCalledTimes(1)
    expect(reportError).toHaveBeenCalledWith(failure, 'router.onError', '/broken')
  })

  it('builds a report with the message, source, route path, stack and time of the error', () => {
    const failure = new Error('Request promise rejected')
    const router = createTestRouter(new Error('unused'))
    removeErrorHandling = installErrorHandling(createApp(EmptyPage), router)

    window.dispatchEvent(unhandledRejection(failure))

    const report = vi.mocked(reportError).mock.results[0]?.value
    expect(report).toEqual({
      message: 'Request promise rejected',
      source: 'window.unhandledrejection',
      route: '/',
      correlationId: null,
      stack: expect.stringMatching(/^at /),
      occurredAt: expect.stringMatching(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/),
      appVersion: packageVersion,
    })
    expect(report?.stack).not.toContain('Request promise rejected')
  })

  it('logs only the source and the error in development, never the report text', () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined)
    const router = createTestRouter(new Error('unused'))
    removeErrorHandling = installErrorHandling(createApp(EmptyPage), router)
    const failure = new Error('Failed for dana.whitfield@fieldservice.example')

    window.dispatchEvent(unhandledRejection(failure))

    expect(consoleError).toHaveBeenCalledTimes(1)
    expect(consoleError).toHaveBeenCalledWith('[window.unhandledrejection]', failure)
    const report = vi.mocked(reportError).mock.results[0]?.value as ClientErrorReport
    expect(report.message).toBe('Failed for [redacted]')
    expect(consoleError.mock.calls.flat()).not.toContain(report.message)
  })

  const raiseThrough: Readonly<
    Record<ErrorSource, (app: App, router: Router, failure: Error) => Promise<unknown>>
  > = {
    'app.errorHandler': async (app, _router, failure) =>
      app.config.errorHandler?.(failure, null, 'mounted hook'),
    'window.unhandledrejection': async (_app, _router, failure) =>
      window.dispatchEvent(unhandledRejection(failure)),
    'router.onError': (_app, router) => router.push('/broken').catch(() => undefined),
  }

  it.each(['app.errorHandler', 'window.unhandledrejection', 'router.onError'] as const)(
    'shows the error toast for an error reported through %s',
    async (source) => {
      const failure = new Error('Route component failed to load')
      const router = createTestRouter(failure)
      const app = createApp(EmptyPage)
      removeErrorHandling = installErrorHandling(app, router)

      await raiseThrough[source](app, router, failure)

      expect(reportError).toHaveBeenCalledWith(failure, source, expect.any(String))
      expect(useToasts().toasts).toEqual([
        expect.objectContaining({
          kind: 'error',
          title: errorToastTitle,
          body: errorToastBody,
          reference: null,
          durationMs: null,
        }),
      ])
    },
  )

  it('shows the correlation reference of a failed API call in the toast and the report', () => {
    const correlationId = 'f0766650-82a1-49f8-b5cc-f3103d346544'
    const failure = new ApiError({
      status: 500,
      code: 'server.error',
      title: 'An unexpected error occurred.',
      detail: null,
      fieldErrors: {},
      correlationId,
      retryAfterSeconds: null,
    })
    const router = createTestRouter(new Error('unused'))
    removeErrorHandling = installErrorHandling(createApp(EmptyPage), router)

    window.dispatchEvent(unhandledRejection(failure))

    expect(vi.mocked(reportError).mock.results[0]?.value).toMatchObject({ correlationId })
    expect(useToasts().toasts).toEqual([expect.objectContaining({ reference: correlationId })])
  })

  it('shows one toast for the same failure reported twice', () => {
    const router = createTestRouter(new Error('unused'))
    removeErrorHandling = installErrorHandling(createApp(EmptyPage), router)

    window.dispatchEvent(unhandledRejection(new Error('Late rejection')))
    window.dispatchEvent(unhandledRejection(new Error('Late rejection')))

    expect(reportError).toHaveBeenCalledTimes(2)
    expect(useToasts().toasts).toHaveLength(1)
  })

  it('describes rejection reasons that are not Error objects by their kind, never by their text', () => {
    const router = createTestRouter(new Error('unused'))
    removeErrorHandling = installErrorHandling(createApp(EmptyPage), router)

    window.dispatchEvent(unhandledRejection('Socket ticket request failed'))
    window.dispatchEvent(unhandledRejection(undefined))
    window.dispatchEvent(unhandledRejection({ customerName: 'Pinecrest Public Library' }))

    const reports = vi.mocked(reportError).mock.results.map((result) => result.value)
    expect(reports).toMatchObject([
      { message: 'A string was thrown instead of an Error', stack: null },
      { message: 'Unknown error', stack: null },
      { message: 'A value was thrown instead of an Error', stack: null },
    ])
  })

  it('stops reporting once the returned cleanup has run', async () => {
    const routerWarning = vi.spyOn(console, 'warn').mockImplementation(() => undefined)
    const failure = new Error('Route component failed to load')
    const router = createTestRouter(failure)
    const app = createApp(EmptyPage)
    installErrorHandling(app, router)()

    window.dispatchEvent(unhandledRejection(new Error('Late rejection')))
    await expect(router.push('/broken')).rejects.toBe(failure)

    expect(reportError).not.toHaveBeenCalled()
    expect(app.config.errorHandler).toBeUndefined()
    expect(routerWarning).toHaveBeenCalled()
  })
})

const clientErrorsRoute = 'POST /api/client-errors'

const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/

const reportMembers = [
  'appVersion',
  'correlationId',
  'message',
  'occurredAt',
  'route',
  'source',
  'stack',
]

interface ContractProperty {
  readonly maxLength?: number
  readonly pattern?: string
}

interface ContractDocument {
  readonly components: {
    readonly schemas: {
      readonly ReportClientErrorRequest: {
        readonly properties: Readonly<Record<string, ContractProperty>>
      }
    }
  }
}

function reportSchema(): Readonly<Record<string, ContractProperty>> {
  const document = JSON.parse(readFileSync(contractFile, 'utf8')) as ContractDocument
  return document.components.schemas.ReportClientErrorRequest.properties
}

function acceptedResponse(correlationId: string | null): Response {
  return new Response(null, {
    status: 202,
    headers: {
      'Content-Length': '0',
      'Cache-Control': 'no-store',
      ...(correlationId === null ? {} : { 'X-Correlation-ID': correlationId }),
    },
  })
}

function accepted(): Reply {
  return (request) => acceptedResponse(request.headers.get('X-Correlation-ID'))
}

function sentReports(stub: FetchStub): RecordedRequest[] {
  return stub.requestsTo(clientErrorsRoute)
}

function bodyOf(request: RecordedRequest | undefined): Record<string, unknown> {
  return JSON.parse(request?.body ?? '{}') as Record<string, unknown>
}

function toastReferences(): (string | null)[] {
  return useToasts().toasts.map((toast) => toast.reference)
}

function randomBase64Url(bytes: number): string {
  return Buffer.from(crypto.getRandomValues(new Uint8Array(bytes))).toString('base64url')
}

interface SentinelValues {
  readonly jwt: string
  readonly bearer: string
  readonly webhookSecret: string
  readonly refreshToken: string
  readonly email: string
  readonly query: string
}

function lastOf<Item>(items: readonly Item[]): Item | undefined {
  return items[items.length - 1]
}

function sentinelValues(): SentinelValues {
  const marker = crypto.randomUUID().replace(/-/g, '')
  const claims = `eyJzdWIiOi${randomBase64Url(24)}`
  return {
    jwt: ['eyJhbGciOiJIUzI1NiJ9', claims, randomBase64Url(32)].join('.'),
    bearer: `Bearer opaque${marker}`,
    webhookSecret: `whsec_${Buffer.from(marker).toString('base64')}`,
    refreshToken: randomBase64Url(32),
    email: `sentinel.${marker}@fieldservice.example`,
    query: `?ticket=${marker}&page=2`,
  }
}

function errorWithStack(message: string, stack: string): Error {
  const failure = new Error(message)
  failure.stack = stack
  return failure
}

function apiFailure(correlationId: string): ApiError {
  return new ApiError({
    status: 500,
    code: 'server.error',
    title: null,
    detail: null,
    fieldErrors: {},
    correlationId,
    retryAfterSeconds: null,
  })
}

function repeatedFailure(): Error {
  return new Error('Cannot read properties of undefined')
}

describe('the client error report', () => {
  const startedAt = new Date(2026, 9, 19, 10, 42)

  beforeEach(() => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined)
  })

  it('shows the toast at once and adds the reference line only when the API answers 202', async () => {
    const reply = deferred()
    const stub = await signedIn({ [clientErrorsRoute]: reply.reply })

    reportError(new Error('demo'), 'window.unhandledrejection', '/jobs')
    const request = await reply.requested

    expect(useToasts().toasts).toEqual([
      expect.objectContaining({ kind: 'error', title: errorToastTitle, reference: null }),
    ])
    const sentId = request.headers.get('X-Correlation-ID')
    expect(sentId).toMatch(uuidPattern)

    reply.resolve(acceptedResponse(sentId))
    await flushPromises()

    expect(toastReferences()).toEqual([sentId])
    expect(sentReports(stub)).toHaveLength(1)
  })

  it('shows the reference of a rejection raised in the page once the report is accepted', async () => {
    const stub = await signedIn({ [clientErrorsRoute]: accepted() })
    const router = createTestRouter(new Error('unused'))
    const remove = installErrorHandling(createApp(EmptyPage), router)

    window.dispatchEvent(unhandledRejection(new Error('demo')))
    await flushPromises()
    remove()

    const [request] = sentReports(stub)
    expect(bodyOf(request)).toMatchObject({
      message: 'demo',
      source: 'window.unhandledrejection',
      route: '/',
    })
    expect(toastReferences()).toEqual([request?.headers.get('X-Correlation-ID')])
  })

  it('posts exactly the seven members of the fixed schema through the signed-in session', async () => {
    const stub = await signedIn({ [clientErrorsRoute]: accepted() })

    reportError(new Error('Mounted hook failed'), 'app.errorHandler', '/jobs/new')
    await flushPromises()

    const [request] = sentReports(stub)
    const body = bodyOf(request)
    expect(Object.keys(body).sort()).toEqual(reportMembers)
    expect(contractProblems('ReportClientErrorRequest', body)).toEqual([])
    expect(body).toMatchObject({
      message: 'Mounted hook failed',
      source: 'app.errorHandler',
      route: '/jobs/new',
      correlationId: null,
      appVersion: packageVersion,
    })
    expect(body.occurredAt).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/)
    expect(request?.headers.get('Authorization')).toMatch(/^Bearer access-/)
    expect(request?.headers.get('Content-Type')).toContain('application/json')
  })

  it.each([
    ['500', problem(500, 'server.error')],
    ['400', problem(400, 'request.validation-failed', { errors: { route: ['Invalid.'] } })],
    ['413', problem(413, 'request.too-large')],
    ['429', problem(429, 'rate-limit.exceeded', {}, { 'Retry-After': '42' })],
    ['no response', networkFailure()],
    ['a 202 without a correlation id', (): Response => acceptedResponse(null)],
  ] as const)(
    'sends once and leaves the reference line out after %s',
    async (_name, reply: Reply) => {
      const stub = await signedIn({ [clientErrorsRoute]: reply })
      const failure = repeatedFailure()

      for (let round = 0; round < 2; round += 1) {
        reportError(failure, 'window.unhandledrejection', '/jobs')
        await flushPromises()
      }

      expect(sentReports(stub)).toHaveLength(1)
      expect(toastReferences()).toEqual([null])
    },
  )

  it('sends nothing when signed out and shows the toast without a reference line', async () => {
    const stub = stubFetch({})

    reportError(new Error('demo'), 'window.unhandledrejection', '/jobs')
    await flushPromises()

    expect(stub.requests).toHaveLength(0)
    expect(useToasts().toasts).toEqual([
      expect.objectContaining({ kind: 'error', body: errorToastBody, reference: null }),
    ])
  })

  it("shows an API error's own reference at once, and keeps it after the report is accepted", async () => {
    const failedCall = crypto.randomUUID()
    const reply = deferred()
    const stub = await signedIn({ [clientErrorsRoute]: reply.reply })

    reportError(apiFailure(failedCall), 'window.unhandledrejection', '/jobs')
    const request = await reply.requested

    expect(toastReferences()).toEqual([failedCall])
    expect(bodyOf(request).correlationId).toBe(failedCall)

    reply.resolve(acceptedResponse(request.headers.get('X-Correlation-ID')))
    await flushPromises()

    expect(toastReferences()).toEqual([failedCall])
    expect(sentReports(stub)).toHaveLength(1)
  })

  it("shows an API error's own reference when signed out, without a report", async () => {
    const failedCall = crypto.randomUUID()
    const stub = stubFetch({})

    reportError(apiFailure(failedCall), 'app.errorHandler', '/jobs')
    await flushPromises()

    expect(stub.requests).toHaveLength(0)
    expect(toastReferences()).toEqual([failedCall])
  })

  it('sends a repeated error once and adds no toast for the repeat, even after its toast was dismissed', async () => {
    const stub = await signedIn({ [clientErrorsRoute]: accepted() })
    const toasts = useToasts()
    const sameFailure = (): Error =>
      errorWithStack('Cannot read properties of undefined', 'at render (JobsPage.vue:42:18)')

    reportError(sameFailure(), 'app.errorHandler', '/jobs')
    reportError(sameFailure(), 'app.errorHandler', '/jobs')
    await flushPromises()

    expect(toastReferences()).toEqual([sentReports(stub)[0]?.headers.get('X-Correlation-ID')])
    toasts.dismiss(toasts.toasts[0]?.id ?? 0)
    reportError(sameFailure(), 'app.errorHandler', '/jobs')
    await flushPromises()

    expect(sentReports(stub)).toHaveLength(1)
    expect(toasts.toasts).toHaveLength(0)
  })

  it('shows one toast for a repeat while its toast is shown when signed out, and a new one once it was dismissed', () => {
    const stub = stubFetch({})
    const toasts = useToasts()
    const failure = repeatedFailure()

    reportError(failure, 'window.unhandledrejection', '/jobs')
    reportError(failure, 'window.unhandledrejection', '/jobs')
    expect(toasts.toasts).toHaveLength(1)

    const first = toasts.toasts[0]?.id ?? 0
    toasts.dismiss(first)
    reportError(failure, 'window.unhandledrejection', '/jobs')

    expect(toasts.toasts).toEqual([expect.objectContaining({ kind: 'error', reference: null })])
    expect(toasts.toasts[0]?.id).not.toBe(first)
    expect(stub.requests).toHaveLength(0)
  })

  it('stops at 10 reports a minute and sends again once the minute has passed', async () => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(startedAt)
    const stub = await signedIn({ [clientErrorsRoute]: accepted() })

    for (let index = 0; index < 12; index += 1) {
      reportError(new Error(`Failure ${index}`), 'app.errorHandler', '/jobs')
    }
    await flushPromises()
    expect(reportLimit).toBe(10)
    expect(sentReports(stub).map((request) => bodyOf(request).message)).toEqual(
      Array.from({ length: 10 }, (_, index) => `Failure ${index}`),
    )

    vi.setSystemTime(startedAt.getTime() + reportWindowMilliseconds - 1)
    reportError(new Error('Failure 12'), 'app.errorHandler', '/jobs')
    await flushPromises()
    expect(sentReports(stub)).toHaveLength(10)

    vi.setSystemTime(startedAt.getTime() + reportWindowMilliseconds)
    reportError(new Error('Failure 13'), 'app.errorHandler', '/jobs')
    await flushPromises()
    expect(reportWindowMilliseconds).toBe(60_000)
    expect(sentReports(stub).map((request) => bodyOf(request).message)).toContain('Failure 13')
    expect(sentReports(stub)).toHaveLength(11)
  })

  it('counts the limit over any 60 seconds, not per calendar minute', async () => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(startedAt)
    const stub = await signedIn({ [clientErrorsRoute]: accepted() })

    for (let index = 0; index < 10; index += 1) {
      vi.setSystemTime(startedAt.getTime() + index * 5_000)
      reportError(new Error(`Spread ${index}`), 'app.errorHandler', '/jobs')
      await flushPromises()
    }
    vi.setSystemTime(startedAt.getTime() + 60_000)
    reportError(new Error('Spread 10'), 'app.errorHandler', '/jobs')
    reportError(new Error('Spread 11'), 'app.errorHandler', '/jobs')
    await flushPromises()

    expect(sentReports(stub)).toHaveLength(11)
  })

  it('never retries a 429 and sends nothing more until Retry-After has passed', async () => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(startedAt)
    const stub = await signedIn({
      [clientErrorsRoute]: [
        problem(429, 'rate-limit.exceeded', {}, { 'Retry-After': '30' }),
        accepted(),
      ],
    })

    const failure = repeatedFailure()

    reportError(failure, 'app.errorHandler', '/jobs')
    await flushPromises()
    vi.setSystemTime(startedAt.getTime() + 29_999)
    reportError(new Error('Another failure'), 'app.errorHandler', '/jobs')
    reportError(failure, 'app.errorHandler', '/jobs')
    await flushPromises()
    expect(sentReports(stub)).toHaveLength(1)

    vi.setSystemTime(startedAt.getTime() + 30_000)
    reportError(new Error('A later failure'), 'app.errorHandler', '/jobs')
    reportError(failure, 'app.errorHandler', '/jobs')
    await flushPromises()

    expect(sentReports(stub).map((request) => bodyOf(request).message)).toEqual([
      'Cannot read properties of undefined',
      'A later failure',
    ])
  })

  it('waits a minute after a 429 without Retry-After', async () => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(startedAt)
    const tooMany: Reply = (request) =>
      new Response(JSON.stringify({ status: 429, code: 'rate-limit.exceeded' }), {
        status: 429,
        headers: {
          'Content-Type': 'application/problem+json',
          'X-Correlation-ID': request.headers.get('X-Correlation-ID') ?? '',
        },
      })
    const stub = await signedIn({ [clientErrorsRoute]: [tooMany, accepted()] })

    reportError(new Error('First'), 'app.errorHandler', '/jobs')
    await flushPromises()
    vi.setSystemTime(startedAt.getTime() + 59_999)
    reportError(new Error('Second'), 'app.errorHandler', '/jobs')
    await flushPromises()
    vi.setSystemTime(startedAt.getTime() + 60_000)
    reportError(new Error('Third'), 'app.errorHandler', '/jobs')
    await flushPromises()

    expect(sentReports(stub).map((request) => bodyOf(request).message)).toEqual(['First', 'Third'])
  })

  it('sends a report the limit stopped when it repeats later, and its shown toast gains the reference', async () => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(startedAt)
    const stub = await signedIn({ [clientErrorsRoute]: accepted() })
    for (let index = 0; index < reportLimit; index += 1) {
      reportError(apiFailure(crypto.randomUUID()), 'app.errorHandler', '/jobs')
    }
    await flushPromises()
    const failure = repeatedFailure()

    reportError(failure, 'app.errorHandler', '/jobs')
    await flushPromises()
    const stopped = useToasts().toasts.find((toast) => toast.reference === null)
    expect(stopped).toBeDefined()
    expect(sentReports(stub)).toHaveLength(reportLimit)

    vi.setSystemTime(startedAt.getTime() + reportWindowMilliseconds)
    reportError(failure, 'app.errorHandler', '/jobs')
    await flushPromises()

    const last = sentReports(stub)[reportLimit]
    expect(bodyOf(last).message).toBe('Cannot read properties of undefined')
    expect(useToasts().toasts).toHaveLength(reportLimit + 1)
    expect(useToasts().toasts.find((toast) => toast.id === stopped?.id)?.reference).toBe(
      last?.headers.get('X-Correlation-ID'),
    )
  })

  it('adds no reference when the toast was dismissed before the report was accepted', async () => {
    const reply = deferred()
    await signedIn({ [clientErrorsRoute]: reply.reply })
    const toasts = useToasts()

    reportError(new Error('demo'), 'window.unhandledrejection', '/jobs')
    const request = await reply.requested
    toasts.dismiss(toasts.toasts[0]?.id ?? 0)
    reply.resolve(acceptedResponse(request.headers.get('X-Correlation-ID')))
    await flushPromises()

    expect(toasts.toasts).toEqual([])
  })

  it('forgets the oldest of more than 100 reports, so that one can be sent again', async () => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(startedAt)
    const stub = await signedIn({ [clientErrorsRoute]: accepted() })

    const distinct = (index: number): Error => errorWithStack(`Distinct ${index}`, 'at render')

    for (let index = 0; index <= rememberedReports; index += 1) {
      vi.setSystemTime(startedAt.getTime() + index * 6_000)
      reportError(distinct(index), 'app.errorHandler', '/jobs')
      await flushPromises()
    }
    vi.setSystemTime(startedAt.getTime() + (rememberedReports + 1) * 6_000)
    reportError(distinct(1), 'app.errorHandler', '/jobs')
    reportError(distinct(0), 'app.errorHandler', '/jobs')
    await flushPromises()

    expect(rememberedReports).toBe(100)
    expect(sentReports(stub)).toHaveLength(rememberedReports + 2)
    expect(bodyOf(lastOf(sentReports(stub))).message).toBe('Distinct 0')
  })

  it.each([
    ['/jobs/01a15488-9680-7989-92af-ed07fc34a890', '/jobs/01a15488-9680-7989-92af-ed07fc34a890'],
    ['/jobs?search=dana.whitfield%40fieldservice.example&page=2', '/jobs'],
    ['/jobs/new#technician', '/jobs/new'],
    ['/jobs/01a15488/inspection?tab=readings#item-3', '/jobs/01a15488/inspection'],
    ['/a%20b/~c_d.e-f', '/a%20b/~c_d.e-f'],
    ['/', '/'],
    ['jobs', null],
    ['', null],
    ['/jobs/<script>', null],
    ['/jobs/a b', null],
    ['/jobs/dana@fieldservice.example', null],
    ['/jobs/é', null],
    ['/jobs\n', null],
    [null, null],
  ] as const)('sends %j as the route %j', (route, expected) => {
    expect(clientErrorRoute(route)).toBe(expected)
  })

  it('sends a route of up to 200 characters and none that is longer, never truncating it', () => {
    const longest = '/jobs'.repeat(routeMaximumLength / '/jobs'.length)

    expect(longest).toHaveLength(routeMaximumLength)
    expect(clientErrorRoute(longest)).toBe(longest)
    expect(clientErrorRoute(`${longest}b`)).toBeNull()
  })

  it('sends no route that carries a token-like value', () => {
    const values = sentinelValues()

    expect(clientErrorRoute(`/reset/${values.refreshToken}`)).toBeNull()
    expect(clientErrorRoute(`/session/${values.jwt}`)).toBeNull()
  })

  it('sends the route path alone from every source through the signed-in session', async () => {
    const values = sentinelValues()
    const stub = await signedIn({ [clientErrorsRoute]: accepted() })

    reportError(new Error('One'), 'router.onError', `/jobs/new${values.query}#notes`)
    reportError(new Error('Two'), 'app.errorHandler', `/jobs?search=${values.email}`)
    reportError(new Error('Three'), 'window.unhandledrejection', `/reset/${values.refreshToken}`)
    await flushPromises()

    const pattern = new RegExp(reportSchema().route?.pattern ?? '$^')
    expect(sentReports(stub).map((request) => bodyOf(request).route)).toEqual([
      '/jobs/new',
      '/jobs',
      null,
    ])
    for (const request of sentReports(stub)) {
      const route = bodyOf(request).route
      expect(route === null || pattern.test(String(route))).toBe(true)
    }
  })

  it('removes tokens, secrets, emails and query strings from the message and the stack before sending', async () => {
    const values = sentinelValues()
    const stub = await signedIn({ [clientErrorsRoute]: accepted() })
    const message = [
      `Token ${values.jwt} rejected`,
      `header ${values.bearer}`,
      `secret ${values.webhookSecret}`,
      `refresh ${values.refreshToken}.`,
      `user ${values.email}`,
      `url https://localhost:5173/api/jobs${values.query}`,
    ].join(' ')
    const stack = [
      `Error: ${message}`,
      `    at load (http://127.0.0.1:5173/src/features/jobs/jobs-api.ts${values.query}:42:18)`,
      `    at token (${values.jwt})`,
    ].join('\n')

    reportError(errorWithStack(message, stack), 'window.unhandledrejection', '/jobs')
    await flushPromises()

    const [request] = sentReports(stub)
    for (const value of Object.values(values)) {
      expect(request?.body).not.toContain(value)
    }
    expect(request?.body).not.toContain('ticket=')
    expect(bodyOf(request).message).toBe(
      'Token [redacted] rejected header [redacted] secret [redacted] refresh [redacted]. ' +
        'user [redacted] url https://localhost:5173/api/jobs?[redacted]',
    )
    expect(bodyOf(request).stack).toContain(
      'at load (http://127.0.0.1:5173/src/features/jobs/jobs-api.ts?[redacted]',
    )
  })

  it('replaces each pattern of the API scrubber and leaves identifiers intact', () => {
    const values = sentinelValues()

    expect(scrubClientErrorText(values.jwt)).toBe('[redacted]')
    expect(scrubClientErrorText(`bearer ${values.refreshToken}`)).toBe('[redacted]')
    expect(scrubClientErrorText(values.webhookSecret)).toBe('[redacted]')
    expect(scrubClientErrorText(`(${values.refreshToken})`)).toBe('([redacted])')
    expect(scrubClientErrorText(`<${values.email}>`)).toBe('<[redacted]>')
    expect(scrubClientErrorText('GET /api/jobs?page=2 failed')).toBe(
      'GET /api/jobs?[redacted] failed',
    )
    expect(scrubClientErrorText('JOB-1042 01a15488-9680-7989-92af-ed07fc34a890')).toBe(
      'JOB-1042 01a15488-9680-7989-92af-ed07fc34a890',
    )
  })

  it('removes an email address before a refresh-shaped run, as the API does, so no part of a long address is sent', async () => {
    const address = 'customer_account_notifications_department_x@pinecrest-library.example'
    const stub = await signedIn({ [clientErrorsRoute]: accepted() })

    reportError(new Error(`Failed for ${address}`), 'window.unhandledrejection', '/jobs')
    await flushPromises()

    expect(address.split('@')[0]).toHaveLength(43)
    expect(scrubClientErrorText(`Failed for ${address}`)).toBe('Failed for [redacted]')
    expect(scrubClientErrorText(`<${'a'.repeat(50)}@fieldservice.example>`)).toBe('<[redacted]>')
    const [request] = sentReports(stub)
    expect(request?.body).not.toContain('pinecrest-library')
    expect(bodyOf(request).message).toBe('Failed for [redacted]')
  })

  it('cuts the message to 500 and the stack to 2000 characters after removing tokens, leaving no part of one', () => {
    const values = sentinelValues()
    const message = `${'lorem '.repeat(80)}${values.jwt} ${'ipsum '.repeat(100)}`
    const stack = `${'frame '.repeat(332)}${values.refreshToken}`

    const report = buildClientErrorReport(errorWithStack(message, stack), 'app.errorHandler', '/')

    expect(report.message).toBe(
      `${'lorem '.repeat(80)}[redacted] ${'ipsum '.repeat(100)}`.slice(0, messageMaximumLength),
    )
    expect(report.message).toHaveLength(messageMaximumLength)
    expect(report.message).not.toContain('eyJ')
    expect(report.stack).toBe(`${'frame '.repeat(332)}[redacted]`.slice(0, stackMaximumLength))
    expect(report.stack).toHaveLength(stackMaximumLength)
    expect(report.stack).not.toContain(values.refreshToken.slice(0, 8))
  })

  it('keeps the body under the 8 KB limit when every member is as long as allowed', async () => {
    const stub = await signedIn({ [clientErrorsRoute]: accepted() })
    const wide = '€'
    const failure = errorWithStack(wide.repeat(800), `${wide.repeat(3000)}\n"\\`)
    const failedCall = 'c'.repeat(64)

    reportError(
      Object.assign(apiFailure(failedCall), { message: failure.message, stack: failure.stack }),
      'window.unhandledrejection',
      '/jobs'.repeat(routeMaximumLength / '/jobs'.length),
    )
    await flushPromises()

    const [request] = sentReports(stub)
    const body = bodyOf(request)
    expect(String(body.message)).toHaveLength(messageMaximumLength)
    expect(String(body.stack)).toHaveLength(stackMaximumLength)
    expect(String(body.route)).toHaveLength(routeMaximumLength)
    expect(body.correlationId).toBe(failedCall)
    expect(new TextEncoder().encode(request?.body).length).toBeLessThan(8 * 1024)
  })

  it('sends only line feeds, carriage returns and tabs among control characters, as well-formed text', () => {
    const smile = '\u{1F600}'
    const message = `a\u0000b\u0007c\td\ne\r\nf\u007Fg\u0085h\uD800i${smile}`

    const report = buildClientErrorReport(new Error(message), 'app.errorHandler', '/')

    expect(report.message).toBe(`a b c\td\ne\r\nf g h�i${smile}`)
  })

  it('never splits a surrogate pair at the length limit', () => {
    const filler = `${'abcd '.repeat(99)}abcd`
    const message = `${filler}\u{1F600}`

    const report = buildClientErrorReport(new Error(message), 'app.errorHandler', '/')

    expect(filler).toHaveLength(messageMaximumLength - 1)
    expect(report.message).toBe(filler)
  })

  it('names a blank message Unknown error and sends no blank stack', () => {
    const report = buildClientErrorReport(
      errorWithStack(' \n\t ', '   '),
      'window.unhandledrejection',
      '/',
    )

    expect(report.message).toBe('Unknown error')
    expect(report.stack).toBeNull()
  })

  it('drops a failed call reference that the correlation pattern refuses', () => {
    const report = buildClientErrorReport(apiFailure('not a reference'), 'app.errorHandler', '/')

    expect(report.correlationId).toBeNull()
  })

  it('follows the limits and patterns of the approved contract', () => {
    const schema = reportSchema()

    expect(schema.message?.maxLength).toBe(messageMaximumLength)
    expect(schema.route?.maxLength).toBe(routeMaximumLength)
    expect(schema.stack?.maxLength).toBe(stackMaximumLength)
    expect(schema.route?.pattern).toBe(routePatternText)
    expect(schema.correlationId?.pattern).toBe(correlationIdPatternText)
    expect(schema.appVersion?.pattern).toBe(appVersionPatternText)
  })

  it('sends the package version when it matches the version pattern, and none otherwise', () => {
    expect(clientAppVersion()).toBe(packageVersion)
    expect(clientAppVersion('1.0.0')).toBe('1.0.0')
    expect(clientAppVersion('1.0.0+build.7')).toBe('1.0.0+build.7')
    expect(clientAppVersion('1.0.0 beta')).toBeNull()
    expect(clientAppVersion('')).toBeNull()
    expect(clientAppVersion('1'.repeat(33))).toBeNull()
  })

  it('writes nothing to the console while reporting, whatever the answer', async () => {
    const consoleCalls = (['log', 'info', 'warn', 'error', 'debug'] as const).map((method) =>
      vi.spyOn(console, method).mockImplementation(() => undefined),
    )
    await signedIn({
      [clientErrorsRoute]: [problem(500, 'server.error'), networkFailure(), accepted()],
    })

    for (const message of ['First', 'Second', 'Third']) {
      reportError(new Error(message), 'app.errorHandler', '/jobs')
    }
    await flushPromises()

    for (const spy of consoleCalls) {
      expect(spy).not.toHaveBeenCalled()
    }
  })
})

describe('personal data in the client error report', () => {
  const jobId = '01a15488-9680-7989-92af-ed07fc34a890'

  const customerName = 'Pinecrest Public Library'

  const phoneNumber = '(555) 014-3307'

  const siteAddress = '300 Pinecrest Road'

  const careHomeName = "St. Anne's Care Home"

  const careHomeSite = '75 Willow Lane'

  const sentinels = [
    customerName,
    'Pinecrest',
    'Public Library',
    phoneNumber,
    '014-3307',
    siteAddress,
  ]

  let removeErrorHandling: RemoveErrorHandling | undefined

  beforeEach(() => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined)
  })

  afterEach(() => {
    removeErrorHandling?.()
    removeErrorHandling = undefined
  })

  function unreadable(body: string): Reply {
    return (request) =>
      new Response(body, {
        status: 200,
        headers: {
          'Content-Type': 'application/json; charset=utf-8',
          'X-Correlation-ID': request.headers.get('X-Correlation-ID') ?? crypto.randomUUID(),
        },
      })
  }

  function expectNoSentinel(text: string | undefined): void {
    for (const value of sentinels) {
      expect(text).not.toContain(value)
    }
  }

  it.each([
    ['a customer name', `${customerName}, ${phoneNumber}, ${siteAddress}`, '"Pinecrest "'],
    ['a phone number', `${phoneNumber} for ${customerName} at ${siteAddress}`, '"(555) 014-"'],
    ['an address', `at ${siteAddress}, ${customerName}, ${phoneNumber}`, '"at 300 Pin"'],
  ])(
    'sends a fixed message and no stack when a response holding %s cannot be read as JSON',
    async (_holding, body, quoted) => {
      const stub = await signedIn({
        [`GET /api/jobs/${jobId}`]: unreadable(body),
        [clientErrorsRoute]: accepted(),
      })
      const router = createTestRouter(new Error('unused'))
      await router.push('/jobs')
      removeErrorHandling = installErrorHandling(createApp(EmptyPage), router)

      const failure: unknown = await useJobsStore()
        .openJob(jobId)
        .then(
          () => null,
          (error: unknown) => error,
        )
      window.dispatchEvent(unhandledRejection(failure))
      await flushPromises()

      expect(failure).toBeInstanceOf(SyntaxError)
      expect(failure instanceof Error ? failure.message : '').toContain(quoted)
      const [request] = sentReports(stub)
      expectNoSentinel(request?.body)
      expect(bodyOf(request)).toMatchObject({
        message: unreadableResponseMessage,
        stack: null,
        source: 'window.unhandledrejection',
        route: '/jobs',
      })
    },
  )

  it.each([
    'Unexpected token \'P\', "Pinecrest "... is not valid JSON',
    'Unexpected end of JSON input',
    "Expected ',' or '}' after property value in JSON at position 43 (line 1 column 44)",
    'JSON.parse: unexpected character at line 1 column 1 of the JSON data',
    "JSON Parse error: Unrecognized token 'P'",
  ])('reads the JSON parse failure "%s" as one fixed message without a stack', (message) => {
    const failure = new SyntaxError(message)
    failure.stack = `SyntaxError: ${message}\n    at JSON.parse (<anonymous>)\n    at load (http://127.0.0.1:5173/assets/index.js:1:200)`

    const report = buildClientErrorReport(failure, 'window.unhandledrejection', '/jobs')

    expect(isJsonParseFailure(failure)).toBe(true)
    expect(report.message).toBe(unreadableResponseMessage)
    expect(report.stack).toBeNull()
  })

  it('removes a value that an engine message quotes, such as the string a TypeError names', () => {
    const value: unknown = `${customerName}, ${phoneNumber}, ${siteAddress}`
    let failure: unknown = null
    try {
      ;(value as Record<string, unknown>).label = 'x'
    } catch (error) {
      failure = error
    }

    const report = buildClientErrorReport(failure, 'app.errorHandler', '/jobs')

    expect(failure).toBeInstanceOf(TypeError)
    expect(failure instanceof Error ? failure.message : '').toContain(customerName)
    expect(report.message).toBe("Cannot create property '[redacted]'")
    expectNoSentinel(report.message)
    expectNoSentinel(report.stack ?? '')
  })

  it.each([
    ['an apostrophe', `${careHomeName}, ${careHomeSite}`],
    ['a line break', `${siteAddress}\n${customerName}`],
    ['an apostrophe and a line break', `${careHomeName}\n${careHomeSite}`],
  ])(
    'removes a quoted value holding %s, from its opening quote to its last closing quote',
    (_holding, value) => {
      expect(withoutQuotedText(`Cannot create property 'label' on string '${value}'`)).toBe(
        "Cannot create property '[redacted]'",
      )
    },
  )

  it.each([
    ['"', '"'],
    ['`', '`'],
    ['\u{2018}', '\u{2019}'],
    ['\u{201c}', '\u{201d}'],
  ])(
    'removes a value between %s and %s that holds its own closing quote and a line break',
    (open, close) => {
      const value = `${open}St. Anne${close}s Care Home\n${careHomeSite}${close}`

      expect(withoutQuotedText(`Invalid ${value} for the site`)).toBe(
        `Invalid ${open}[redacted]${close} for the site`,
      )
    },
  )

  it("keeps an apostrophe outside quotes, as in can't, and an opening quote that is never closed", () => {
    expect(withoutQuotedText(`Cannot read '${careHomeName}' because it can't be read`)).toBe(
      "Cannot read '[redacted]' because it can't be read",
    )
    expect(withoutQuotedText("The job can't start")).toBe("The job can't start")
    expect(withoutQuotedText(`The job can't start at '${careHomeName}'`)).toBe(
      "The job can't start at '[redacted]'",
    )
    expect(withoutQuotedText("Unexpected 'token")).toBe("Unexpected 'token")
    expect(withoutQuotedText("A lone ' stays")).toBe("A lone ' stays")
    expect(withoutQuotedText('A \u{2018}draft can\u{2019}t close')).toBe(
      'A \u{2018}draft can\u{2019}t close',
    )
  })

  it('redacts the same text as the greedy quote patterns of the security review', () => {
    const reference: readonly (readonly [RegExp, string])[] = [
      [/(^|[^\p{L}\p{N}])'[\s\S]*'(?![\p{L}\p{N}])/gu, "$1'[redacted]'"],
      [/"[\s\S]*"/g, '"[redacted]"'],
      [/`[\s\S]*`/g, '`[redacted]`'],
      [/\u{2018}[\s\S]*\u{2019}(?![\p{L}\p{N}])/gu, '\u{2018}[redacted]\u{2019}'],
      [/\u{201c}[\s\S]*\u{201d}/gu, '\u{201c}[redacted]\u{201d}'],
    ]
    const alphabet = [
      ...["'", '"', '`', '\u{2018}', '\u{2019}', '\u{201c}', '\u{201d}'],
      ...['a', '\u{e9}', '1', '\u{1d400}', ' ', '\n', '.', '\u{1f600}'],
    ]
    let seed = 20_261_024
    const nextIndex = (size: number): number => {
      seed = (seed * 48_271) % 2_147_483_647
      return seed % size
    }

    const mismatches: string[] = []
    for (let round = 0; round < 3_000; round += 1) {
      const length = 1 + nextIndex(24)
      const text = Array.from({ length }, () => alphabet[nextIndex(alphabet.length)]).join('')
      const expected = reference.reduce(
        (current, [pattern, replacement]) => current.replace(pattern, replacement),
        text,
      )
      if (withoutQuotedText(text) !== expected) {
        mismatches.push(text)
      }
    }

    expect(mismatches).toEqual([])
  })

  it('reads a long text whose quotes never close in linear time and leaves it unchanged', () => {
    const text = " 'a".repeat(40_000)

    const started = performance.now()
    const redacted = withoutQuotedText(text)
    const elapsed = performance.now() - started

    expect(redacted).toBe(text)
    expect(elapsed).toBeLessThan(250)
  })

  it('sends the property an undefined value was read for as the API documentation shows it', () => {
    const readNumber = (job: { readonly number: number } | undefined): number =>
      (job as { readonly number: number }).number
    let failure: unknown = null
    try {
      readNumber(undefined)
    } catch (error) {
      failure = error
    }

    const report = buildClientErrorReport(failure, 'app.errorHandler', '/jobs')

    expect(failure instanceof Error ? failure.message : '').toBe(
      "Cannot read properties of undefined (reading 'number')",
    )
    expect(report.message).toBe("Cannot read properties of undefined (reading '[redacted]')")
  })

  it('sends no part of a quoted value whose second line reads like a stack frame', async () => {
    const stub = await signedIn({ [clientErrorsRoute]: accepted() })
    const value: unknown = `${careHomeName}\n    at ${careHomeSite}`
    let failure: unknown = null
    try {
      ;(value as Record<string, unknown>).label = 'x'
    } catch (error) {
      failure = error
    }

    reportError(failure, 'app.errorHandler', '/jobs')
    await flushPromises()

    const [request] = sentReports(stub)
    expect(failure instanceof Error ? failure.stack : '').toContain(`    at ${careHomeSite}'`)
    for (const part of ['Anne', 'Care Home', 'Willow']) {
      expect(request?.body).not.toContain(part)
    }
    expect(bodyOf(request).message).toBe("Cannot create property '[redacted]'")
    expect(String(bodyOf(request).stack)).toMatch(/^at /)
  })

  it('removes every quoted segment from a message, keeping apostrophes inside words', () => {
    expect(
      withoutQuotedText(
        `Invalid value "${customerName}" for ‘${siteAddress}’, “${phoneNumber}” and \`${customerName}\` can't pass`,
      ),
    ).toBe('Invalid value "[redacted]" for ‘[redacted]’, “[redacted]” and `[redacted]` can\'t pass')
  })

  it('sends the stack frames only, never the lines that repeat the message', () => {
    const message = `Lookup failed for "${customerName}"\nnear ${siteAddress}`
    const failure = errorWithStack(
      message,
      [
        `Error: ${message}`,
        '    at lookup (http://127.0.0.1:5173/assets/JobsPage.js:12:40)',
        '    at run (http://127.0.0.1:5173/assets/index.js:1:20)',
      ].join('\n'),
    )

    const report = buildClientErrorReport(failure, 'app.errorHandler', '/jobs')

    expect(report.stack).toBe(
      [
        '    at lookup (http://127.0.0.1:5173/assets/JobsPage.js:12:40)',
        '    at run (http://127.0.0.1:5173/assets/index.js:1:20)',
      ]
        .join('\n')
        .trim(),
    )
    expect(stackFrames('lookup@http://127.0.0.1:5173/assets/JobsPage.js:12:40')).toBe(
      'lookup@http://127.0.0.1:5173/assets/JobsPage.js:12:40',
    )
    expectNoSentinel(report.stack ?? '')
  })

  it('sends a fixed message for a thrown string, whatever text it holds', async () => {
    const stub = await signedIn({ [clientErrorsRoute]: accepted() })

    reportError(`${customerName} ${phoneNumber} ${siteAddress}`, 'window.unhandledrejection', '/')
    await flushPromises()

    const [request] = sentReports(stub)
    expectNoSentinel(request?.body)
    expect(bodyOf(request)).toMatchObject({ message: thrownStringMessage, stack: null })
  })

  it('keeps the text of the errors the app raises itself, which hold no data', () => {
    expect(
      buildClientErrorReport(new Error('demo'), 'window.unhandledrejection', '/').message,
    ).toBe('demo')
    expect(
      buildClientErrorReport(new Error('No job is open to cancel'), 'app.errorHandler', '/')
        .message,
    ).toBe('No job is open to cancel')
    expect(
      buildClientErrorReport(apiFailure('c'.repeat(64)), 'app.errorHandler', '/').message,
    ).toBe('Request failed with status 500 (server.error)')
  })

  it('builds every application error from fixed text, never from data', () => {
    const thrown = [...listSourceFiles('.ts'), ...listSourceFiles('.vue')]
      .filter((file) => !file.includes('__tests__/') && !file.startsWith('shared/testing/'))
      .flatMap((file) =>
        Array.from(
          readSource(file).matchAll(/new (?!ApiError)(?:[A-Z]\w*)?Error\(([^)]*)\)/g),
          (match) => [file, match[1] ?? ''] as const,
        ),
      )

    expect(thrown.length).toBeGreaterThan(0)
    expect(thrown.filter(([, argument]) => !/^'[^'`$]*'$/.test(argument))).toEqual([])
    expect(readSource('shared/api/api-error.ts')).toContain('super(summarize(details))')
  })
})
