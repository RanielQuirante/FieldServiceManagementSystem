import { afterEach, describe, expect, it, vi } from 'vitest'
import { effectScope, type EffectScope } from 'vue'

import { sessionResponse } from '@/features/auth/__tests__/session-fixtures'
import { demoTechnicians, demoToday } from '@/features/jobs/__tests__/job-fixtures'
import {
  flush,
  generatorJob,
  generatorJobId,
  jobEvent,
  jobRoute,
  refreshRoute,
  signedIn,
} from '@/features/realtime/__tests__/realtime-fixtures'
import {
  eventRequestLimit,
  listDebounceMilliseconds,
  type LivePageCopy,
} from '@/features/realtime/live-refetch'
import { useRealtimeStore } from '@/features/realtime/realtime-store'
import { contractProblems } from '@/shared/testing/contract-schema'
import { lastFakeSocket } from '@/shared/testing/fake-socket'
import {
  deferred,
  json,
  problem,
  stubFetch,
  type FetchStub,
  type RecordedRequest,
  type RouteTable,
} from '@/shared/testing/fetch-stub'
import { kept } from '@/shared/testing/kept-text'

import { useDashboardData, type DashboardData } from '../composables/use-dashboard-data'
import { newActivityMilliseconds, useDashboardStore } from '../dashboard-store'
import {
  dashboardWindow,
  maximumWindowMilliseconds,
  nextLocalMidnight,
  startOfLocalDay,
} from '../dashboard-window'
import { demoActivity, demoSummary, startedEntry, withNewActivity } from './dashboard-fixtures'

const summaryRoute = 'GET /api/dashboard/summary'

const techniciansRoute = 'GET /api/technicians'

const dashboardCopy: LivePageCopy = {
  refreshFailedTitle: 'The dashboard could not be refreshed.',
  refreshedLabel: 'The dashboard was refreshed at',
  showingFrom: (time) => `Showing data from ${time}.`,
}

const hour = 60 * 60 * 1_000

let scope: EffectScope | null = null

afterEach(() => {
  scope?.stop()
  scope = null
  useRealtimeStore().stop()
})

function queryOf(request: RecordedRequest | undefined): Record<string, string> {
  return Object.fromEntries(new URL(request?.url ?? 'http://localhost/').searchParams)
}

function windowsAsked(stub: FetchStub): Record<string, string>[] {
  return stub.requestsTo(summaryRoute).map(queryOf)
}

function lastOf<Item>(items: readonly Item[]): Item | undefined {
  return items[items.length - 1]
}

function jsonResponse(body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}

function inTimeZone<Result>(zone: string, run: () => Result): Result {
  const original = process.env.TZ
  process.env.TZ = zone
  try {
    return run()
  } finally {
    if (original === undefined) {
      delete process.env.TZ
    } else {
      process.env.TZ = original
    }
  }
}

describe('the dashboard window', () => {
  it('runs from local midnight to the next local midnight, as UTC instants with milliseconds', () => {
    const period = dashboardWindow(demoToday)

    expect(period).toEqual({
      from: new Date(2026, 9, 19).toISOString(),
      to: new Date(2026, 9, 20).toISOString(),
    })
    expect(period.from).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/)
    expect(period.to).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/)
    expect(dashboardWindow(new Date(2026, 9, 19, 0, 0, 0, 0))).toEqual(period)
    expect(dashboardWindow(new Date(2026, 9, 19, 23, 59, 59, 999))).toEqual(period)
  })

  it.each([
    ['America/New_York', '2026-03-08', '2026-03-08T05:00:00.000Z', '2026-03-09T04:00:00.000Z', 23],
    ['America/New_York', '2026-11-01', '2026-11-01T04:00:00.000Z', '2026-11-02T05:00:00.000Z', 25],
    ['Europe/London', '2026-03-29', '2026-03-29T00:00:00.000Z', '2026-03-29T23:00:00.000Z', 23],
    ['Europe/London', '2026-10-25', '2026-10-24T23:00:00.000Z', '2026-10-26T00:00:00.000Z', 25],
    [
      'Australia/Lord_Howe',
      '2026-10-04',
      '2026-10-03T13:30:00.000Z',
      '2026-10-04T13:00:00.000Z',
      23.5,
    ],
    [
      'Australia/Lord_Howe',
      '2026-04-05',
      '2026-04-04T13:00:00.000Z',
      '2026-04-05T13:30:00.000Z',
      24.5,
    ],
    ['America/Sao_Paulo', '2018-11-04', '2018-11-04T03:00:00.000Z', '2018-11-05T02:00:00.000Z', 23],
  ] as const)(
    'covers the whole local day in %s on %s, a clock-change day, within the 31 × 24 h rule',
    (zone, date, from, to, hours) => {
      inTimeZone(zone, () => {
        const period = dashboardWindow(new Date(`${date}T12:00:00`))
        const span = Date.parse(period.to) - Date.parse(period.from)

        expect(period).toEqual({ from, to })
        expect(span).toBe(hours * hour)
        expect(span).toBeGreaterThan(0)
        expect(span).toBeLessThanOrEqual(maximumWindowMilliseconds)
      })
    },
  )

  it('starts a day whose midnight the clock skips at its first instant', () => {
    inTimeZone('America/Sao_Paulo', () => {
      const start = startOfLocalDay(new Date(2018, 10, 4, 12))

      expect(start.getDate()).toBe(4)
      expect([start.getHours(), start.getMinutes()]).toEqual([1, 0])
      expect(start.getTime() - 1).toBeLessThan(start.getTime())
      expect(new Date(start.getTime() - 1).getDate()).toBe(3)
    })
  })

  it('ends each day where the next one begins, across a clock change', () => {
    inTimeZone('America/New_York', () => {
      const days = [6, 7, 8, 9, 10].map((day) => dashboardWindow(new Date(2026, 2, day, 9)))

      for (let index = 1; index < days.length; index += 1) {
        expect(days[index]?.from).toBe(days[index - 1]?.to)
      }
      expect(nextLocalMidnight(new Date(2026, 2, 8, 23, 59)).toISOString()).toBe(
        '2026-03-09T04:00:00.000Z',
      )
    })
  })

  it('holds the API limit of exactly 31 days of 24 hours', () => {
    expect(maximumWindowMilliseconds).toBe(31 * 24 * hour)
  })
})

describe('the dashboard store', () => {
  function useDateOnlyClock(now: Date = demoToday): void {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(now)
  }

  it('asks for the local day and for the technicians in parallel, and keeps both in the API order', async () => {
    useDateOnlyClock()
    const summary = deferred()
    const technicians = deferred()
    const stub = stubFetch({ [summaryRoute]: summary.reply, [techniciansRoute]: technicians.reply })
    const store = useDashboardStore()

    const loading = store.load()
    await Promise.all([summary.requested, technicians.requested])

    expect(store.summaryStatus).toBe('loading')
    expect(store.workloadStatus).toBe('loading')
    expect(windowsAsked(stub)).toEqual([dashboardWindow(demoToday)])

    const reversed = [...demoTechnicians].reverse()
    technicians.resolve(jsonResponse({ items: reversed }))
    summary.resolve(jsonResponse(demoSummary()))
    await loading

    expect(store.summaryStatus).toBe('ready')
    expect(store.summary).toEqual(demoSummary())
    expect(store.workloadStatus).toBe('ready')
    expect(store.workload).toEqual(reversed)
  })

  it('reads a summary and technicians that match the contract', () => {
    expect(contractProblems('DashboardSummaryResponse', demoSummary())).toEqual([])
    expect(contractProblems('GetTechniciansResponse', { items: demoTechnicians })).toEqual([])
  })

  it('keeps every activity entry the API returns, in its order, never trimming', async () => {
    useDateOnlyClock()
    const fewer = demoSummary({ recentActivity: demoActivity.slice(0, 2) })
    stubFetch({
      [summaryRoute]: [json(demoSummary()), json(fewer), json(demoSummary({ recentActivity: [] }))],
      [techniciansRoute]: json({ items: demoTechnicians }),
    })
    const store = useDashboardStore()

    await store.load()
    expect(store.summary?.recentActivity).toEqual(demoActivity)
    expect(store.summary?.recentActivity).toHaveLength(6)

    await store.refreshSummary()
    expect(store.summary?.recentActivity).toEqual(demoActivity.slice(0, 2))

    await store.refreshSummary()
    expect(store.summaryStatus).toBe('ready')
    expect(store.summary?.recentActivity).toEqual([])
  })

  it('loads each part on its own: a failed summary keeps the technicians, with its reference', async () => {
    useDateOnlyClock()
    const stub = stubFetch({
      [summaryRoute]: [problem(500, 'server.error'), json(demoSummary())],
      [techniciansRoute]: json({ items: demoTechnicians }),
    })
    const store = useDashboardStore()

    await store.load()

    expect(store.summaryStatus).toBe('error')
    expect(store.summaryError?.correlationId).toBe(
      stub.requestsTo(summaryRoute)[0]?.headers.get('X-Correlation-ID'),
    )
    expect(store.workloadStatus).toBe('ready')

    await store.loadSummary()

    expect(store.summaryStatus).toBe('ready')
    expect(store.summaryError).toBeNull()
    expect(stub.requestsTo(techniciansRoute)).toHaveLength(1)
  })

  it('loads each part on its own: failed technicians keep the summary, and Try again reloads them alone', async () => {
    useDateOnlyClock()
    const stub = stubFetch({
      [summaryRoute]: json(demoSummary()),
      [techniciansRoute]: [
        problem(429, 'rate-limit.exceeded', {}, { 'Retry-After': '9' }),
        json({ items: demoTechnicians }),
      ],
    })
    const store = useDashboardStore()

    await store.load()

    expect(store.summaryStatus).toBe('ready')
    expect(store.workloadStatus).toBe('error')
    expect(store.workloadError?.retryAfterSeconds).toBe(9)

    await store.loadWorkload()

    expect(store.workloadStatus).toBe('ready')
    expect(stub.requestsTo(summaryRoute)).toHaveLength(1)
  })

  it('asks for the new day once the clock has passed midnight', async () => {
    useDateOnlyClock(new Date(2026, 9, 19, 23, 59, 59, 999))
    const stub = stubFetch({
      [summaryRoute]: json(demoSummary()),
      [techniciansRoute]: json({ items: demoTechnicians }),
    })
    const store = useDashboardStore()
    await store.load()

    vi.setSystemTime(new Date(2026, 9, 20, 0, 0, 0, 0))
    await store.refreshSummary()

    expect(windowsAsked(stub)).toEqual([
      dashboardWindow(new Date(2026, 9, 19)),
      dashboardWindow(new Date(2026, 9, 20)),
    ])
  })

  it('refreshes in the background only once loaded, and skips a part whose first load failed', async () => {
    useDateOnlyClock()
    const stub = stubFetch({
      [summaryRoute]: problem(500, 'server.error'),
      [techniciansRoute]: json({ items: demoTechnicians }),
    })
    const store = useDashboardStore()

    expect(await store.refreshSummary()).toBe(false)
    expect(await store.refreshWorkload()).toBe(false)
    expect(stub.requests).toHaveLength(0)

    await store.load()

    expect(await store.refreshSummary()).toBe(false)
    expect(store.summaryStatus).toBe('error')
    expect(await store.refreshWorkload()).toBe(true)
    expect(stub.requestsTo(summaryRoute)).toHaveLength(1)
  })

  it('waits for a first load still running before a background refresh', async () => {
    useDateOnlyClock()
    const first = deferred()
    const stub = stubFetch({
      [summaryRoute]: [first.reply, json(demoSummary({ technicians: { total: 6, onShift: 4 } }))],
      [techniciansRoute]: json({ items: demoTechnicians }),
    })
    const store = useDashboardStore()

    const loading = store.loadSummary()
    await first.requested
    const refreshing = store.refreshSummary()
    first.resolve(jsonResponse(demoSummary()))
    await loading

    expect(await refreshing).toBe(true)
    expect(store.summary?.technicians.onShift).toBe(4)
    expect(stub.requestsTo(summaryRoute)).toHaveLength(2)
  })

  it('drops a background summary that a newer refresh replaced', async () => {
    useDateOnlyClock()
    const older = deferred()
    const newer = demoSummary({ technicians: { total: 6, onShift: 6 } })
    stubFetch({
      [summaryRoute]: [json(demoSummary()), older.reply, json(newer)],
      [techniciansRoute]: json({ items: demoTechnicians }),
    })
    const store = useDashboardStore()
    await store.load()

    const first = store.refreshSummary()
    await older.requested
    expect(await store.refreshSummary()).toBe(true)
    older.resolve(jsonResponse(demoSummary({ technicians: { total: 6, onShift: 1 } })))

    expect(await first).toBe(false)
    expect(store.summary).toEqual(newer)
  })

  it('rejects a failed background refresh with its error and keeps the figures shown', async () => {
    useDateOnlyClock()
    stubFetch({
      [summaryRoute]: [json(demoSummary()), problem(500, 'server.error')],
      [techniciansRoute]: json({ items: demoTechnicians }),
    })
    const store = useDashboardStore()
    await store.load()

    await expect(store.refreshSummary()).rejects.toMatchObject({ status: 500 })
    expect(store.summary).toEqual(demoSummary())
    expect(store.summaryStatus).toBe('ready')
  })
})

describe('the live dashboard', () => {
  async function liveDashboard(
    routes: RouteTable = {},
    now: Date = demoToday,
  ): Promise<{
    readonly stub: FetchStub
    readonly realtime: ReturnType<typeof useRealtimeStore>
    readonly data: DashboardData
  }> {
    vi.useFakeTimers({ now })
    const stub = await signedIn({
      [refreshRoute]: json(sessionResponse()),
      [summaryRoute]: json(demoSummary()),
      [techniciansRoute]: json({ items: demoTechnicians }),
      [jobRoute(generatorJobId)]: json(generatorJob()),
      ...routes,
    })
    const realtime = useRealtimeStore()
    realtime.start()
    scope = effectScope()
    const data = scope.run(() => useDashboardData(dashboardCopy))
    if (data === undefined) {
      throw new Error('The dashboard data did not start')
    }
    await flush()
    return { stub, realtime, data }
  }

  async function connect(): Promise<void> {
    await lastFakeSocket().simulateConnect()
    await flush()
  }

  function counts(stub: FetchStub): readonly number[] {
    return [stub.requestsTo(summaryRoute).length, stub.requestsTo(techniciansRoute).length]
  }

  it('loads the summary and the technicians once, and marks the pill time', async () => {
    const { stub, realtime, data } = await liveDashboard()

    expect(counts(stub)).toEqual([1, 1])
    expect(data.store.summaryStatus).toBe('ready')
    expect(data.store.workloadStatus).toBe('ready')
    expect(realtime.refreshedAt).toEqual(demoToday)
    expect(data.today.value).toEqual(new Date(2026, 9, 19))
  })

  it('refetches the summary and the technicians on every connect, the first included', async () => {
    const { stub } = await liveDashboard()

    await connect()
    expect(counts(stub)).toEqual([2, 2])

    lastFakeSocket().simulateDisconnect('transport close')
    await vi.advanceTimersByTimeAsync(2_000)
    await connect()
    expect(counts(stub)).toEqual([3, 3])
  })

  it('refetches both once, debounced, after events within 300 ms', async () => {
    const jobIds = [generatorJobId, crypto.randomUUID(), crypto.randomUUID()]
    const { stub } = await liveDashboard(
      Object.fromEntries(
        jobIds.map((jobId) => [jobRoute(jobId), json(generatorJob({ id: jobId }))]),
      ),
    )
    await connect()

    for (const jobId of jobIds) {
      lastFakeSocket().emitServerEvent('job.status-changed', jobEvent({ jobId }))
      await vi.advanceTimersByTimeAsync(100)
    }
    expect(counts(stub)).toEqual([2, 2])

    await vi.advanceTimersByTimeAsync(listDebounceMilliseconds)
    await flush()

    expect(counts(stub)).toEqual([3, 3])
    expect(lastOf(windowsAsked(stub))).toEqual(dashboardWindow(demoToday))
    expect(jobIds.map((jobId) => stub.requestsTo(jobRoute(jobId)).length)).toEqual([1, 1, 1])
  })

  it('spends one slot of the 60-a-minute event budget on each of its two refetches', async () => {
    const jobIds = Array.from({ length: 21 }, () => crypto.randomUUID())
    const { stub } = await liveDashboard(
      Object.fromEntries(
        jobIds.map((jobId) => [jobRoute(jobId), json(generatorJob({ id: jobId }))]),
      ),
    )
    await connect()

    for (const jobId of jobIds) {
      lastFakeSocket().emitServerEvent('job.status-changed', jobEvent({ jobId }))
      await vi.advanceTimersByTimeAsync(400)
    }
    await flush()

    const jobFetches = jobIds.filter((jobId) => stub.requestsTo(jobRoute(jobId)).length > 0)
    expect(eventRequestLimit).toBe(60)
    expect(jobFetches).toHaveLength(20)
    expect(counts(stub)).toEqual([2 + 20 + 1, 2 + 20 + 1])
    expect(jobFetches.length + 20 + 20).toBe(eventRequestLimit)
  })

  it('caps a burst of 500 events at 60 event-driven requests and one full refetch', async () => {
    const jobIds = Array.from({ length: 500 }, () => crypto.randomUUID())
    const { stub } = await liveDashboard(
      Object.fromEntries(
        jobIds.map((jobId) => [jobRoute(jobId), json(generatorJob({ id: jobId }))]),
      ),
    )
    await connect()

    for (const jobId of jobIds) {
      lastFakeSocket().emitServerEvent('job.status-changed', jobEvent({ jobId }))
      await vi.advanceTimersByTimeAsync(2)
    }
    await vi.advanceTimersByTimeAsync(listDebounceMilliseconds)
    await flush()

    const jobFetches = stub.requests.filter((request) => request.path.startsWith('/api/jobs/'))
    expect(jobFetches).toHaveLength(eventRequestLimit)
    expect(counts(stub)).toEqual([3, 3])

    await vi.advanceTimersByTimeAsync(50_000)
    await flush()
    expect(counts(stub)).toEqual([3, 3])
  })

  it('marks entries an event refetch added as new for 12 seconds of visible time, and none from a load or a connect', async () => {
    let visibility: DocumentVisibilityState = 'visible'
    vi.spyOn(document, 'visibilityState', 'get').mockImplementation(() => visibility)
    const afterConnect = withNewActivity(demoSummary(), [startedEntry(1045, 43)])
    const afterEvent = withNewActivity(afterConnect, [
      startedEntry(1055, 45),
      startedEntry(1047, 44),
    ])
    const { data } = await liveDashboard({
      [summaryRoute]: [json(demoSummary()), json(afterConnect), json(afterEvent)],
    })
    expect([...data.store.newActivityIds]).toEqual([])

    await connect()
    expect(data.store.summary).toEqual(afterConnect)
    expect([...data.store.newActivityIds]).toEqual([])

    lastFakeSocket().emitServerEvent('job.status-changed', jobEvent())
    await vi.advanceTimersByTimeAsync(listDebounceMilliseconds)
    await flush()

    const added = [startedEntry(1055, 45).id, startedEntry(1047, 44).id]
    expect(data.store.summary).toEqual(afterEvent)
    expect([...data.store.newActivityIds].sort()).toEqual([...added].sort())

    await vi.advanceTimersByTimeAsync(5_000)
    visibility = 'hidden'
    document.dispatchEvent(new Event('visibilitychange'))
    await vi.advanceTimersByTimeAsync(60_000)
    expect([...data.store.newActivityIds].sort()).toEqual([...added].sort())

    visibility = 'visible'
    document.dispatchEvent(new Event('visibilitychange'))
    await vi.advanceTimersByTimeAsync(newActivityMilliseconds - 5_000 - 1)
    expect(data.store.newActivityIds.size).toBe(2)
    await vi.advanceTimersByTimeAsync(1)
    expect([...data.store.newActivityIds]).toEqual([])
    expect(newActivityMilliseconds).toBe(12_000)
  })

  it('keeps each batch of new entries for its own 12 seconds', async () => {
    vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible')
    const first = withNewActivity(demoSummary(), [startedEntry(1045, 43)])
    const second = withNewActivity(first, [startedEntry(1055, 45)])
    const { data } = await liveDashboard({
      [summaryRoute]: [json(demoSummary()), json(demoSummary()), json(first), json(second)],
    })
    await connect()

    lastFakeSocket().emitServerEvent('job.status-changed', jobEvent())
    await vi.advanceTimersByTimeAsync(listDebounceMilliseconds)
    await flush()
    await vi.advanceTimersByTimeAsync(6_000)
    lastFakeSocket().emitServerEvent('job.status-changed', jobEvent())
    await vi.advanceTimersByTimeAsync(listDebounceMilliseconds)
    await flush()
    expect(data.store.newActivityIds.size).toBe(2)

    await vi.advanceTimersByTimeAsync(newActivityMilliseconds - 6_000 - listDebounceMilliseconds)
    expect([...data.store.newActivityIds]).toEqual([startedEntry(1055, 45).id])
  })

  it('marks nothing when an event refetch brings the next day', async () => {
    const lastSecond = new Date(2026, 9, 19, 23, 59, 59)
    const nextDay = new Date(2026, 9, 20, 0, 0, 1)
    const { stub, data } = await liveDashboard(
      {
        [summaryRoute]: [
          json(demoSummary({}, lastSecond)),
          json(demoSummary({}, lastSecond)),
          problem(503, 'server.unavailable'),
          json(withNewActivity(demoSummary({}, nextDay), [startedEntry(1045, 43)])),
        ],
      },
      lastSecond,
    )
    await connect()

    await vi.advanceTimersByTimeAsync(1_000)
    await flush()
    expect(stub.requestsTo(summaryRoute)).toHaveLength(3)
    expect(data.store.summary?.range.from).toBe(dashboardWindow(lastSecond).from)

    lastFakeSocket().emitServerEvent('job.status-changed', jobEvent())
    await vi.advanceTimersByTimeAsync(listDebounceMilliseconds)
    await flush()

    expect(data.store.summary?.range.from).toBe(dashboardWindow(nextDay).from)
    expect([...data.store.newActivityIds]).toEqual([])
  })

  it.each([summaryRoute, techniciansRoute])(
    "shows the dashboard's Refresh failed copy with the reference of the failed call when %s fails on connect",
    async (route) => {
      const firstLoad =
        route === summaryRoute ? json(demoSummary()) : json({ items: demoTechnicians })
      const { stub, realtime, data } = await liveDashboard({
        [route]: [firstLoad, problem(500, 'server.error')],
      })

      await connect()

      expect(realtime.refreshFailure).toEqual({
        title: 'The dashboard could not be refreshed.',
        message: kept('Showing data from 10:42~AM.'),
        reference: stub.requestsTo(route)[1]?.headers.get('X-Correlation-ID'),
      })
      expect(data.store.summary).toEqual(demoSummary())
      expect(data.store.workload).toEqual(demoTechnicians)
    },
  )

  it('keeps the figures and shows no banner when an event refetch fails', async () => {
    const { realtime, data } = await liveDashboard({
      [summaryRoute]: [json(demoSummary()), json(demoSummary()), problem(500, 'server.error')],
    })
    await connect()

    lastFakeSocket().emitServerEvent('job.status-changed', jobEvent())
    await vi.advanceTimersByTimeAsync(listDebounceMilliseconds)
    await flush()

    expect(realtime.notice).toBeNull()
    expect(data.store.summaryStatus).toBe('ready')
    expect(data.store.summary).toEqual(demoSummary())
  })

  it('refetches the summary for the new day at each local midnight while the page is open', async () => {
    const lateEvening = new Date(2026, 9, 19, 23, 59, 30)
    const { stub, realtime, data } = await liveDashboard({}, lateEvening)

    await vi.advanceTimersByTimeAsync(30_000 - 1)
    expect(stub.requestsTo(summaryRoute)).toHaveLength(1)
    expect(data.today.value).toEqual(new Date(2026, 9, 19))

    await vi.advanceTimersByTimeAsync(1)
    await flush()

    expect(data.today.value).toEqual(new Date(2026, 9, 20))
    expect(windowsAsked(stub)).toEqual([
      dashboardWindow(new Date(2026, 9, 19)),
      dashboardWindow(new Date(2026, 9, 20)),
    ])
    expect(stub.requestsTo(techniciansRoute)).toHaveLength(1)
    expect(realtime.refreshedAt).toEqual(new Date(2026, 9, 20))

    await vi.advanceTimersByTimeAsync(24 * hour)
    await flush()

    expect(data.today.value).toEqual(new Date(2026, 9, 21))
    expect(lastOf(windowsAsked(stub))).toEqual(dashboardWindow(new Date(2026, 9, 21)))
  })

  it('marks the pill time after Try again succeeds', async () => {
    const { realtime, data } = await liveDashboard({
      [summaryRoute]: [problem(500, 'server.error'), json(demoSummary())],
      [techniciansRoute]: [problem(500, 'server.error'), json({ items: demoTechnicians })],
    })
    expect(realtime.refreshedAt).toBeNull()

    await vi.advanceTimersByTimeAsync(1_000)
    await data.retrySummary()
    expect(realtime.refreshedAt).toEqual(new Date(demoToday.getTime() + 1_000))

    await vi.advanceTimersByTimeAsync(1_000)
    await data.retryWorkload()
    expect(realtime.refreshedAt).toEqual(new Date(demoToday.getTime() + 2_000))
  })

  it('stops refetching, clears its figures and its midnight timer when the page closes', async () => {
    const { stub } = await liveDashboard()
    await connect()

    scope?.stop()
    scope = null
    lastFakeSocket().emitServerEvent('job.status-changed', jobEvent())
    await vi.advanceTimersByTimeAsync(24 * hour)
    await flush()

    const store = useDashboardStore()
    expect(counts(stub)).toEqual([2, 2])
    expect(store.summary).toBeNull()
    expect(store.summaryStatus).toBe('idle')
    expect(store.workload).toBeNull()
  })
})
