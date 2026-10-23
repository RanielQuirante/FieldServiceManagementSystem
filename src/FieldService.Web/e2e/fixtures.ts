import {
  test as base,
  type APIRequestContext,
  type BrowserContext,
  type BrowserContextOptions,
  type FullProject,
  type Page,
  type Request,
  type Response,
  type Route,
} from '@playwright/test'

import {
  AdminApi,
  demoAccounts,
  readSession,
  refreshCookieFrom,
  refreshCookieName,
  refreshWithCookie,
  socketTicketsPath,
  signIn,
  signInTechnician,
  signOutWithCookie,
  type SentinelSink,
  type SessionUser,
  type TechnicianApi,
} from './support/api.ts'
import {
  budgetViolations,
  countsAgainstTheUserLimit,
  recordAdministratorRequest,
  budgetPauseFor,
  recordAuthCall,
  testReserve,
  waitForBudgetRoom,
  type AuthCall,
} from './support/budget.ts'
import { recordSessionEvent } from './support/sessions.ts'
import {
  handshakeUrlProblem,
  isRealtimeHandshake,
  type HandshakeChecker,
} from './support/websocket.ts'
import { currentRunId, resultsFile, writeJsonFile } from './support/run.ts'
import { demoAccountSentinels, SentinelCollector, sentinelFile } from './support/sentinels.ts'

export { expect } from '@playwright/test'

export interface CspViolation {
  readonly page: string
  readonly directive: string
  readonly blocked: string
  readonly source: string
}

export const seededJobNumbers = { first: 1036, last: 1055 } as const

const cspBinding = '__e2eReportCspViolation'

const stubbedRequests = new WeakSet<Request>()

function escapeForPattern(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\/]/g, '\\$&')
}

function failedResource(status: number, path: string): RegExp {
  return new RegExp(`status of ${status} \\(.*\\) @ \\S*${escapeForPattern(path)}`)
}

export const nothingExpected = /(?!)/

export function anyOf(...patterns: RegExp[]): RegExp {
  return new RegExp(patterns.map((pattern) => `(?:${pattern.source})`).join('|'))
}

export const consolePatterns = {
  anonymousRefresh: failedResource(401, '/api/auth/refresh'),
  signInUnauthorized: failedResource(401, '/api/auth/sign-in'),
  signInRefused: failedResource(403, '/api/auth/sign-in'),
  signInRateLimited: failedResource(429, '/api/auth/sign-in'),
  signInServerError: failedResource(500, '/api/auth/sign-in'),
  jobNotFound: failedResource(404, '/api/jobs/'),
  jobsServerError: failedResource(500, '/api/jobs'),
  createJobRejected: failedResource(400, '/api/jobs'),
  assignmentConflict: failedResource(409, '/assignment'),
  assignmentRejected: failedResource(400, '/assignment'),
  techniciansServerError: failedResource(500, '/api/technicians'),
  templatesServerError: failedResource(500, '/api/checklist-templates'),
  reportedError: (marker: string): RegExp => new RegExp(escapeForPattern(marker)),
} as const

function withoutSocketQueries(text: string): string {
  return text.replace(/(\/socket\.io\/)\?[^\s'"]*/g, '$1?<query not shown>')
}

export class PageGuards implements HandshakeChecker {
  readonly cspViolations: CspViolation[] = []
  private readonly foreignRequests: string[] = []
  private readonly pageErrors: string[] = []
  private readonly consoleErrors: string[] = []
  private readonly handshakeProblems: string[] = []
  private handshakes = 0
  private readonly allowedConsole: RegExp[]
  private readonly allowedPageErrors: RegExp[]
  private readonly watched = new WeakSet<Page>()

  constructor(
    private readonly origin: string,
    expectedConsole: RegExp,
    expectedPageErrors: RegExp,
  ) {
    this.allowedConsole = [expectedConsole]
    this.allowedPageErrors = [expectedPageErrors]
  }

  allowConsole(...patterns: RegExp[]): void {
    this.allowedConsole.push(...patterns)
  }

  allowPageErrors(...patterns: RegExp[]): void {
    this.allowedPageErrors.push(...patterns)
  }

  async watch(page: Page): Promise<void> {
    if (this.watched.has(page)) {
      return
    }
    this.watched.add(page)
    await page.exposeFunction(cspBinding, (violation: CspViolation) => {
      this.cspViolations.push(violation)
    })
    await page.addInitScript((binding) => {
      document.addEventListener('securitypolicyviolation', (event) => {
        const report = (window as unknown as Record<string, (violation: unknown) => void>)[binding]
        void report?.({
          page: window.location.href,
          directive: event.violatedDirective,
          blocked: event.blockedURI,
          source: `${event.sourceFile}:${event.lineNumber}`,
        })
      })
    }, cspBinding)
    page.on('request', (request) => this.checkOrigin(request))
    page.on('websocket', (socket) => this.checkSocketUrl(socket.url()))
    page.on('pageerror', (error) => {
      const text = withoutSocketQueries(`${error.name}: ${error.message}`)
      if (!this.allowedPageErrors.some((pattern) => pattern.test(text))) {
        this.pageErrors.push(text)
      }
    })
    page.on('console', (message) => {
      if (message.type() !== 'error') {
        return
      }
      const text = withoutSocketQueries(`${message.text()} @ ${message.location().url}`)
      if (!this.allowedConsole.some((pattern) => pattern.test(text))) {
        this.consoleErrors.push(text)
      }
    })
  }

  checkSocketUrl(url: string): void {
    if (!isRealtimeHandshake(url)) {
      return
    }
    this.handshakes += 1
    const problem = handshakeUrlProblem(url)
    if (problem !== null) {
      this.handshakeProblems.push(problem)
    }
  }

  handshakesChecked(): number {
    return this.handshakes
  }

  private checkOrigin(request: Request): void {
    const url = request.url()
    if (url.startsWith('data:') || url.startsWith('blob:')) {
      return
    }
    if (new URL(url).origin !== this.origin) {
      this.foreignRequests.push(`${request.method()} ${url}`)
    }
  }

  failures(): string[] {
    return [
      ...this.cspViolations.map(
        (violation) =>
          `CSP violation of ${violation.directive} on ${violation.page}: blocked ${violation.blocked} (${violation.source})`,
      ),
      ...this.foreignRequests.map((request) => `Request to another origin: ${request}`),
      ...this.pageErrors.map((error) => `Page error: ${error}`),
      ...this.consoleErrors.map((message) => `Console error: ${message}`),
      ...this.handshakeProblems,
    ]
  }
}

export interface StubResponse {
  readonly status: number
  readonly body?: unknown
  readonly headers?: Readonly<Record<string, string>>
}

export async function fulfilStub(
  page: Page,
  url: string | RegExp,
  response: StubResponse,
  times?: number,
): Promise<void> {
  await page.route(
    url,
    async (route: Route) => {
      stubbedRequests.add(route.request())
      await route.fulfill({
        status: response.status,
        contentType: response.status >= 400 ? 'application/problem+json' : 'application/json',
        headers: { 'Cache-Control': 'no-store', ...response.headers },
        body: response.body === undefined ? '' : JSON.stringify(response.body),
      })
    },
    times === undefined ? undefined : { times },
  )
}

export async function stubSignedOutRestore(page: Page): Promise<void> {
  await fulfilStub(page, '**/api/auth/refresh', {
    status: 401,
    body: problemBody(401, 'auth.session-expired', 'The session has expired.'),
  })
}

export function problemBody(
  status: number,
  code: string,
  title: string,
  extra: object = {},
): object {
  return {
    type: 'about:blank',
    title,
    status,
    code,
    instance: '/api',
    traceId: '00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01',
    correlationId: '0193f2d4-7c1a-7b2e-9a51-2f0c4d8e6a10',
    ...extra,
  }
}

function authCallOf(request: Request): AuthCall | null {
  if (request.method() !== 'POST') {
    return null
  }
  const path = new URL(request.url()).pathname
  if (path === '/api/auth/sign-in') {
    return 'sign-in'
  }
  if (path === socketTicketsPath) {
    return 'socket-ticket'
  }
  return path === '/api/auth/refresh' ? 'refresh' : null
}

function isSignOut(request: Request): boolean {
  return request.method() === 'POST' && new URL(request.url()).pathname === '/api/auth/sign-out'
}

async function observeSignOut(response: Response): Promise<void> {
  const clearsCookie = ((await response.headerValue('set-cookie')) ?? '').includes(
    `${refreshCookieName}=`,
  )
  if (response.status() === 204 && clearsCookie) {
    recordSessionEvent('signed-out', 'Web sign-out in the browser')
  }
}

async function observeAuthResponse(response: Response, sink: SentinelSink): Promise<void> {
  const request = response.request()
  if (isSignOut(request) && !stubbedRequests.has(request)) {
    await observeSignOut(response)
    return
  }
  const call = authCallOf(request)
  if (call === null || stubbedRequests.has(request)) {
    return
  }
  recordAuthCall(call)
  sink('refresh cookie', refreshCookieFrom([(await response.headerValue('set-cookie')) ?? '']))
  if (!response.ok()) {
    return
  }
  if (call === 'sign-in') {
    recordSessionEvent('opened', 'Web sign-in in the browser')
  }
  try {
    const body = (await response.json()) as {
      accessToken?: string
      refreshToken?: string | null
      ticket?: string
    }
    sink('access token', body.accessToken)
    sink('refresh token', body.refreshToken)
    sink('socket ticket', body.ticket)
    sink('socket ticket signature', body.ticket?.split('.')[2])
  } catch {
    return
  }
}

function instrumentContext(context: BrowserContext, sink: SentinelSink): void {
  context.on('requestfinished', (request) => {
    if (!stubbedRequests.has(request) && countsAgainstTheUserLimit(request.url())) {
      recordAdministratorRequest()
    }
  })
  context.on('response', (response) => {
    void observeAuthResponse(response, sink)
  })
}

async function collectCookies(context: BrowserContext, sink: SentinelSink): Promise<void> {
  for (const cookie of await context.cookies()) {
    sink('refresh cookie', cookie.name === refreshCookieName ? cookie.value : null)
  }
}

function errorName(reason: unknown): string {
  return reason instanceof Error ? reason.name : 'unknown error'
}

async function holdsRefreshCookie(context: BrowserContext): Promise<boolean> {
  return (await context.cookies()).some((cookie) => cookie.name === refreshCookieName)
}

async function signOutContext(context: BrowserContext): Promise<void> {
  if (!(await holdsRefreshCookie(context))) {
    return
  }
  const status = await signOutWithCookie(context.request)
  if (status !== 204 || (await holdsRefreshCookie(context))) {
    throw new Error(
      `The sign-out at teardown answered ${status} and the refresh cookie is ${
        (await holdsRefreshCookie(context)) ? 'still held' : 'gone'
      }.`,
    )
  }
}

export function contextOptionsOf(project: FullProject): BrowserContextOptions {
  const use = project.use
  return {
    baseURL: use.baseURL,
    locale: use.locale,
    timezoneId: use.timezoneId,
    viewport: use.viewport,
    serviceWorkers: use.serviceWorkers,
    userAgent: use.userAgent,
    deviceScaleFactor: use.deviceScaleFactor,
    isMobile: use.isMobile,
    hasTouch: use.hasTouch,
  }
}

export interface AdminSession {
  readonly user: SessionUser
  accessToken(): string
}

export type ContextOpener = () => Promise<BrowserContext>

export type TechnicianSessions = (email: string) => Promise<TechnicianApi>

interface TestFixtures {
  expectedConsole: RegExp
  expectedPageErrors: RegExp
  guards: PageGuards
  adminPage: Page
  openContext: ContextOpener
  anonymousPage: Page
}

interface WorkerFixtures {
  runId: string
  sentinels: SentinelCollector
  sentinelSink: SentinelSink
  cspLog: CspViolation[]
  adminContext: BrowserContext
  adminSession: AdminSession
  api: AdminApi
  technician: TechnicianSessions
  seededSentinels: void
}

export const test = base.extend<TestFixtures, WorkerFixtures>({
  runId: [
    async ({}, use) => {
      await use(currentRunId())
    },
    { scope: 'worker' },
  ],

  sentinels: [
    async ({ runId }, use, workerInfo) => {
      const collector = new SentinelCollector(sentinelFile(runId, workerInfo.workerIndex))
      for (const sentinel of demoAccountSentinels) {
        collector.add(sentinel.label, sentinel.value)
      }
      await use(collector)
      collector.flush()
    },
    { scope: 'worker' },
  ],

  sentinelSink: [
    async ({ sentinels }, use) => {
      await use((label, value) => sentinels.add(label, value))
    },
    { scope: 'worker' },
  ],

  cspLog: [
    async ({ runId }, use, workerInfo) => {
      const log: CspViolation[] = []
      await use(log)
      writeJsonFile(resultsFile('csp', `${runId}-worker-${workerInfo.workerIndex}.json`), log)
    },
    { scope: 'worker' },
  ],

  adminContext: [
    async ({ browser, sentinelSink }, use, workerInfo) => {
      const context = await browser.newContext(contextOptionsOf(workerInfo.project))
      instrumentContext(context, sentinelSink)
      await use(context)
      await collectCookies(context, sentinelSink)
      await signOutContext(context)
      await context.close()
    },
    { scope: 'worker' },
  ],

  adminSession: [
    async ({ adminContext, sentinelSink }, use) => {
      const response = await signIn(
        adminContext.request,
        demoAccounts.administrator,
        'Web',
        sentinelSink,
      )
      const session = await readSession(response, 'The administrator sign-in')
      await use({ user: session.user, accessToken: () => session.accessToken })
    },
    { scope: 'worker' },
  ],

  api: [
    async ({ adminContext, adminSession, sentinelSink }, use) => {
      let current = adminSession.accessToken()
      await use(
        new AdminApi(adminContext.request, {
          current: () => current,
          renew: async () => {
            const session = await readSession(
              await refreshWithCookie(adminContext.request, sentinelSink),
              'The administrator session refresh',
            )
            current = session.accessToken
            return current
          },
        }),
      )
    },
    { scope: 'worker' },
  ],

  technician: [
    async ({ playwright, sentinelSink }, use, workerInfo) => {
      const sessions = new Map<string, Promise<TechnicianApi>>()
      const contexts: APIRequestContext[] = []
      await use((email) => {
        const cached = sessions.get(email)
        if (cached !== undefined) {
          return cached
        }
        const session = (async () => {
          const request = await playwright.request.newContext({
            baseURL: workerInfo.project.use.baseURL,
          })
          contexts.push(request)
          return signInTechnician(request, email, sentinelSink)
        })()
        sessions.set(email, session)
        return session
      })
      const signedIn = await Promise.allSettled(sessions.values())
      const signOuts = await Promise.allSettled(
        signedIn.flatMap((outcome) =>
          outcome.status === 'fulfilled' ? [outcome.value.signOut()] : [],
        ),
      )
      for (const context of contexts) {
        await context.dispose()
      }
      const problems = [
        ...signedIn.flatMap((outcome) =>
          outcome.status === 'rejected'
            ? [`a sign-in was rejected (${errorName(outcome.reason)})`]
            : [],
        ),
        ...signOuts.flatMap((outcome) => {
          if (outcome.status === 'rejected') {
            return [`a sign-out was rejected (${errorName(outcome.reason)})`]
          }
          return outcome.value === 204 ? [] : [`a sign-out answered ${outcome.value}`]
        }),
      ]
      if (problems.length > 0) {
        throw new Error(`The technician sessions at teardown: ${problems.join('; ')}.`)
      }
    },
    { scope: 'worker' },
  ],

  seededSentinels: [
    async ({ api, sentinelSink, runId }, use, workerInfo) => {
      let page = 1
      let seen = 0
      let total = 1
      const earlierRuns: string[] = []
      while (seen < total) {
        const list = await api.getJobs({ page, pageSize: 100 })
        total = list.totalCount
        seen += list.items.length
        page += 1
        earlierRuns.push(
          ...list.items
            .filter((job) => job.title.startsWith('E2E '))
            .map((job) => `JOB-${job.number} ${job.title}`),
        )
        const seeded = list.items.filter(
          (job) => job.number >= seededJobNumbers.first && job.number <= seededJobNumbers.last,
        )
        for (const summary of seeded) {
          const job = await api.getJob(summary.id)
          sentinelSink('customer', job.customerName)
          sentinelSink('site address', job.siteAddress)
          sentinelSink('site access notes', job.siteAccessNotes)
          sentinelSink('contact name', job.contactName)
          sentinelSink('contact phone', job.contactPhone)
          sentinelSink('technician name', job.assignedTechnician?.displayName)
          sentinelSink('administrator name', job.createdBy.displayName)
        }
        if (list.items.length === 0) {
          break
        }
      }
      writeJsonFile(resultsFile('jobs', `${runId}-worker-${workerInfo.workerIndex}.json`), {
        jobsBeforeThisWorker: total,
        e2eJobsBeforeThisWorker: earlierRuns.sort(),
      })
      await use()
    },
    { scope: 'worker', auto: true },
  ],

  expectedConsole: [nothingExpected, { option: true }],

  expectedPageErrors: [nothingExpected, { option: true }],

  guards: [
    async ({ baseURL, expectedConsole, expectedPageErrors, cspLog }, use, testInfo) => {
      if (baseURL === undefined) {
        throw new Error('The project has no baseURL.')
      }
      const pause = budgetPauseFor(testReserve)
      if (pause > 0) {
        testInfo.setTimeout(testInfo.timeout + pause + 1_000)
        testInfo.annotations.push({
          type: 'budget pause',
          description: `${pause} ms before the test, to keep the run inside its per-minute budgets (R64, DEC-067 (d))`,
        })
        await waitForBudgetRoom(testReserve)
      }
      const guards = new PageGuards(new URL(baseURL).origin, expectedConsole, expectedPageErrors)
      await use(guards)
      cspLog.push(...guards.cspViolations)
      const problems = [...guards.failures(), ...budgetViolations()]
      if (problems.length > 0) {
        throw new Error(`The automatic guards failed this test:\n${problems.join('\n')}`)
      }
    },
    { auto: true },
  ],

  adminPage: async ({ adminContext, guards }, use) => {
    const page = await adminContext.newPage()
    await guards.watch(page)
    await use(page)
    await page.close()
  },

  openContext: async ({ browser, sentinelSink }, use, testInfo) => {
    const opened: BrowserContext[] = []
    await use(async () => {
      const context = await browser.newContext(contextOptionsOf(testInfo.project))
      instrumentContext(context, sentinelSink)
      opened.push(context)
      return context
    })
    for (const context of opened) {
      await collectCookies(context, sentinelSink)
      await signOutContext(context)
      await context.close()
    }
  },

  anonymousPage: async ({ openContext, guards }, use) => {
    const context = await openContext()
    const page = await context.newPage()
    await guards.watch(page)
    await use(page)
  },
})
