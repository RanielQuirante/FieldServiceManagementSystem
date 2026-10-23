import { mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { sessionResponse, accessToken } from '@/features/auth/__tests__/session-fixtures'
import { useSessionStore } from '@/features/auth/session-store'
import {
  dana,
  demoToday,
  historyEntry,
  marco,
  onDemoDay,
  tom,
} from '@/features/jobs/__tests__/job-fixtures'
import { eventToastMilliseconds, useToasts } from '@/shared/composables/use-toasts'
import { fakeSockets, lastFakeSocket } from '@/shared/testing/fake-socket'
import {
  deferred,
  json,
  networkFailure,
  noContent,
  problem,
  type FetchStub,
  type RouteTable,
} from '@/shared/testing/fetch-stub'
import ToastHost from '@/shared/ui/ToastHost.vue'

import {
  createLiveRefetch,
  eventRequestLimit,
  jobFetchIntervalMilliseconds,
  listDebounceMilliseconds,
  rememberedEventIds,
} from '../live-refetch'
import {
  liveToastBody,
  liveToastFor,
  liveToastNote,
  liveToastTitle,
  shortenNote,
  toastNoteLength,
} from '../live-toast'
import {
  backoffDelay,
  backoffInitialMilliseconds,
  backoffJitter,
  backoffMaximumMilliseconds,
  connectionErrorCode,
  realtimePath,
} from '../realtime-client'
import { backOnlineMilliseconds, rowFlashMilliseconds, useRealtimeStore } from '../realtime-store'
import {
  apiFailure,
  flush,
  generatorJob,
  generatorJobId,
  jobEvent,
  jobRoute,
  recordingView,
  refreshRoute,
  signedIn,
  signOutRoute,
  ticketReply,
  ticketRoute,
} from './realtime-fixtures'

beforeEach(() => {
  vi.useFakeTimers({ now: demoToday })
  vi.spyOn(Math, 'random').mockReturnValue(0.5)
})

afterEach(() => {
  useRealtimeStore().stop()
})

async function started(routes: RouteTable = {}) {
  const stub = await signedIn(routes)
  const realtime = useRealtimeStore()
  const view = recordingView()
  realtime.register(view.registration)
  realtime.start()
  return { stub, realtime, view, socket: lastFakeSocket() }
}

async function live(routes: RouteTable = {}) {
  const context = await started(routes)
  await context.socket.simulateConnect()
  await flush()
  return context
}

function jobRequests(stub: FetchStub): number {
  return stub.requests.filter((request) => request.path.startsWith('/api/jobs/')).length
}

describe('the socket', () => {
  it('is created on start for the page origin, WebSocket only, without auto-connect', async () => {
    await signedIn()
    const realtime = useRealtimeStore()

    expect(fakeSockets).toHaveLength(0)

    realtime.start()
    realtime.start()

    const socket = lastFakeSocket()
    expect(fakeSockets).toHaveLength(1)
    expect(socket.uri).toBe(window.location.origin)
    expect(socket.options).toMatchObject({
      path: realtimePath,
      transports: ['websocket'],
      autoConnect: false,
    })
    expect(realtimePath).toBe('/socket.io')
    expect(typeof socket.options.auth).toBe('function')
    expect(socket.connectCalls).toBe(1)
    expect(realtime.state).toBe('connecting')
  })

  it('leaves transport reconnection to Socket.IO at 1 to 5 s with randomization 0.5', async () => {
    const { socket } = await started()
    const transportRetry = {
      reconnectionDelay: 1_000,
      reconnectionDelayMax: 5_000,
      randomizationFactor: 0.5,
    }

    expect(socket.options).toMatchObject(transportRetry)
    expect(socket.options).not.toHaveProperty('reconnection')
    expect(socket.options).not.toHaveProperty('reconnectionAttempts')
    expect([backoffInitialMilliseconds, backoffMaximumMilliseconds, backoffJitter]).toEqual([
      1_000, 30_000, 0.25,
    ])

    const { Manager } = await vi.importActual<typeof import('socket.io-client')>('socket.io-client')
    const manager = new Manager(socket.uri, { ...transportRetry, autoConnect: false })

    expect(manager.reconnection()).toBe(true)
    expect(manager.reconnectionAttempts()).toBe(Infinity)
    expect([
      manager.reconnectionDelay(),
      manager.reconnectionDelayMax(),
      manager.randomizationFactor(),
    ]).toEqual([1_000, 5_000, 0.5])
  })

  it('fetches a fresh ticket for every connect and sends only the ticket', async () => {
    const { stub, socket } = await started()

    await socket.simulateConnect()
    socket.simulateDisconnect('transport close')
    await socket.simulateConnect()

    const requests = stub.requestsTo(ticketRoute)
    expect(requests).toHaveLength(2)
    expect(requests.every((request) => request.body === '')).toBe(true)
    expect(
      requests.every((request) => request.headers.get('Authorization')?.startsWith('Bearer ')),
    ).toBe(true)
    expect(socket.handshakes.map((data) => Object.keys(data))).toEqual([['ticket'], ['ticket']])
    const tickets = socket.handshakes.map((data) => (data as { ticket: string }).ticket)
    expect(new Set(tickets).size).toBe(2)
  })

  it.each([
    ['the API cannot be reached', networkFailure()],
    ['the API fails', problem(500, 'server.error')],
    ['the request is rate limited', problem(429, 'rate-limit.exceeded')],
  ])('still completes the handshake with an empty auth object when %s', async (_label, reply) => {
    const { socket } = await started({ [ticketRoute]: reply })

    const data = await socket.simulateConnectError('ticket_missing')

    expect(data).toEqual({})
  })
})

describe('refetching', () => {
  it('refetches every registered view on every connect, the first included', async () => {
    const { realtime, view, socket } = await started()

    await socket.simulateConnect()
    await flush()

    expect(view.calls.refetch).toBe(1)
    expect(realtime.state).toBe('live')
    expect(realtime.refreshedAt).toEqual(demoToday)

    socket.simulateDisconnect('transport close')
    await vi.advanceTimersByTimeAsync(2_000)
    await socket.simulateConnect()
    await flush()

    expect(view.calls.refetch).toBe(2)
    expect(realtime.refreshedAt?.getTime()).toBe(demoToday.getTime() + 2_000)
    expect(realtime.notice).toBeNull()
  })

  it('keeps the pill time when a refetch only skips', async () => {
    const { realtime, view, socket } = await started()
    view.respondWith('skipped')

    await socket.simulateConnect()
    await flush()

    expect(realtime.state).toBe('live')
    expect(realtime.refreshedAt).toBeNull()
    expect(realtime.notice).toBeNull()
  })

  it('refetches the job of each event, and the lists once for events within 300 ms', async () => {
    const jobs = [generatorJobId, crypto.randomUUID(), crypto.randomUUID()]
    const routes = Object.fromEntries(
      jobs.map((jobId) => [jobRoute(jobId), json(generatorJob({ id: jobId }))]),
    )
    const { stub, view, socket } = await live(routes)

    for (const jobId of jobs) {
      socket.emitServerEvent('job.status-changed', jobEvent({ jobId }))
      await vi.advanceTimersByTimeAsync(100)
    }

    expect(jobs.map((jobId) => stub.requestsTo(jobRoute(jobId)).length)).toEqual([1, 1, 1])
    expect(view.calls.applied.map((job) => job.id)).toEqual(jobs)
    expect(view.calls.lists).toBe(0)

    await vi.advanceTimersByTimeAsync(listDebounceMilliseconds - 100 - 1)
    expect(view.calls.lists).toBe(0)

    await vi.advanceTimersByTimeAsync(1)
    await flush()
    expect(listDebounceMilliseconds).toBe(300)
    expect(view.calls.lists).toBe(1)
    expect(view.calls.refetch).toBe(1)
  })

  it('keeps one GetJobById in flight per job and starts at most one a second', async () => {
    const first = deferred()
    const second = deferred()
    const { stub, socket } = await live({
      [jobRoute(generatorJobId)]: [first.reply, second.reply, json(generatorJob())],
    })

    for (let index = 0; index < 5; index += 1) {
      socket.emitServerEvent('job.status-changed', jobEvent())
    }
    await flush()
    expect(stub.requestsTo(jobRoute(generatorJobId))).toHaveLength(1)

    await vi.advanceTimersByTimeAsync(400)
    first.resolve(
      new Response(JSON.stringify(generatorJob()), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    await flush()
    expect(stub.requestsTo(jobRoute(generatorJobId))).toHaveLength(1)

    await vi.advanceTimersByTimeAsync(jobFetchIntervalMilliseconds - 400 - 1)
    expect(stub.requestsTo(jobRoute(generatorJobId))).toHaveLength(1)

    await vi.advanceTimersByTimeAsync(1)
    await flush()
    expect(stub.requestsTo(jobRoute(generatorJobId))).toHaveLength(2)

    socket.emitServerEvent('job.status-changed', jobEvent())
    socket.emitServerEvent('job.status-changed', jobEvent())
    await vi.advanceTimersByTimeAsync(5_000)
    expect(stub.requestsTo(jobRoute(generatorJobId))).toHaveLength(2)

    second.resolve(
      new Response(JSON.stringify(generatorJob()), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    await flush()
    expect(stub.requestsTo(jobRoute(generatorJobId))).toHaveLength(3)
  })

  it('caps 500 events in one second at 60 event-driven requests and one debounced full refetch', async () => {
    const jobIds = Array.from({ length: 500 }, () => crypto.randomUUID())
    const routes = Object.fromEntries(
      jobIds.map((jobId) => [jobRoute(jobId), json(generatorJob({ id: jobId }))]),
    )
    const { stub, view, socket } = await live(routes)
    const show = vi.spyOn(useToasts(), 'show')

    for (const jobId of jobIds) {
      socket.emitServerEvent('job.status-changed', jobEvent({ jobId }))
      await vi.advanceTimersByTimeAsync(2)
    }
    await flush()

    expect(eventRequestLimit).toBe(60)
    expect(jobRequests(stub)).toBe(60)
    expect(show).toHaveBeenCalledTimes(60)
    expect(view.calls.refetch).toBe(1)

    await vi.advanceTimersByTimeAsync(listDebounceMilliseconds)
    await flush()
    expect(view.calls.refetch).toBe(2)
    expect(view.calls.lists).toBe(0)
    expect(show).toHaveBeenCalledTimes(60)

    await vi.advanceTimersByTimeAsync(58_000)
    await flush()
    expect(jobRequests(stub)).toBe(60)
    expect(view.calls.refetch).toBe(2)

    await vi.advanceTimersByTimeAsync(2_000)
    socket.emitServerEvent('job.status-changed', jobEvent({ jobId: jobIds[0] }))
    await flush()
    expect(jobRequests(stub)).toBe(61)
  })

  it('spaces full refetches above the cap at least a minute apart', async () => {
    const jobIds = Array.from({ length: 70 }, () => crypto.randomUUID())
    const routes = Object.fromEntries(
      jobIds.map((jobId) => [jobRoute(jobId), json(generatorJob({ id: jobId }))]),
    )
    const { view, socket } = await live(routes)

    for (const jobId of jobIds) {
      socket.emitServerEvent('job.status-changed', jobEvent({ jobId }))
    }
    await vi.advanceTimersByTimeAsync(listDebounceMilliseconds)
    await flush()
    expect(view.calls.refetch).toBe(2)

    for (let round = 0; round < 10; round += 1) {
      socket.emitServerEvent('job.status-changed', jobEvent({ jobId: jobIds[69] }))
      await vi.advanceTimersByTimeAsync(1_000)
    }
    await flush()
    expect(view.calls.refetch).toBe(2)

    await vi.advanceTimersByTimeAsync(60_000)
    await flush()
    expect(view.calls.refetch).toBe(3)
  })

  it('drops a payload that fails the type guard without a request', async () => {
    const { stub, view, socket } = await live()
    const before = stub.requests.length
    const withoutNumber: Record<string, unknown> = { ...jobEvent() }
    Reflect.deleteProperty(withoutNumber, 'jobNumber')

    socket.emitServerEvent('job.status-changed', withoutNumber)
    socket.emitServerEvent('job.status-changed', { ...jobEvent(), jobNumber: '1049' })
    socket.emitServerEvent('job.status-changed', null)
    await vi.advanceTimersByTimeAsync(2_000)

    expect(stub.requests.length).toBe(before)
    expect(view.calls.lists).toBe(0)
  })

  it('drops an event id among the last 200 handled', async () => {
    const other = crypto.randomUUID()
    const { stub, socket } = await live({
      [jobRoute(generatorJobId)]: json(generatorJob()),
      [jobRoute(other)]: json(generatorJob({ id: other })),
    })
    const repeated = jobEvent()

    socket.emitServerEvent('job.status-changed', repeated)
    socket.emitServerEvent('job.status-changed', { ...repeated, jobId: other })
    await vi.advanceTimersByTimeAsync(2_000)

    expect(stub.requestsTo(jobRoute(other))).toHaveLength(0)
  })

  it('remembers exactly the last 200 event ids', () => {
    const coordinator = createLiveRefetch({
      getJob: () => Promise.resolve(generatorJob()),
      onJobFetched: () => undefined,
      onRefetched: () => undefined,
    })
    const first = jobEvent()

    expect(rememberedEventIds).toBe(200)
    expect(coordinator.handleEvent(first)).toBe('accepted')
    expect(coordinator.handleEvent(first)).toBe('duplicate')
    for (let index = 1; index < rememberedEventIds; index += 1) {
      expect(coordinator.handleEvent(jobEvent())).toBe('accepted')
    }
    expect(coordinator.handleEvent(first)).toBe('duplicate')

    expect(coordinator.handleEvent(jobEvent())).toBe('accepted')
    expect(coordinator.handleEvent(first)).toBe('accepted')
    expect(coordinator.handleEvent({ ...first, status: 'Paused' })).toBe('invalid')
    coordinator.reset()
  })

  it('never patches a view from the payload, only from the refetched job', async () => {
    const { view, socket } = await live({
      [jobRoute(generatorJobId)]: json(generatorJob({ status: 'OnHold' })),
    })

    socket.emitServerEvent('job.status-changed', jobEvent({ status: 'Completed' }))
    await flush()

    expect(view.calls.applied.map((job) => job.status)).toEqual(['OnHold'])
  })
})

describe('connection errors', () => {
  it('refreshes the session once after ticket_invalid and connects again, then backs off', async () => {
    const { stub, socket } = await started({ [refreshRoute]: json(sessionResponse()) })

    await socket.simulateConnectError('ticket_invalid')
    await flush()

    expect(stub.requestsTo(refreshRoute)).toHaveLength(1)
    expect(socket.connectCalls).toBe(2)

    await socket.simulateConnectError('ticket_invalid')
    await flush()
    expect(stub.requestsTo(refreshRoute)).toHaveLength(1)
    expect(socket.connectCalls).toBe(2)

    await vi.advanceTimersByTimeAsync(backoffInitialMilliseconds - 1)
    expect(socket.connectCalls).toBe(2)
    await vi.advanceTimersByTimeAsync(1)
    expect(socket.connectCalls).toBe(3)
  })

  it('refreshes once after forbidden; a second forbidden pauses without retrying until Refresh now', async () => {
    const { stub, realtime, view, socket } = await started({
      [refreshRoute]: json(sessionResponse()),
    })

    await socket.simulateConnectError('forbidden')
    await flush()
    expect(stub.requestsTo(refreshRoute)).toHaveLength(1)
    expect(socket.connectCalls).toBe(2)

    await socket.simulateConnectError('forbidden')
    await flush()
    expect(realtime.state).toBe('paused')
    expect(realtime.notice).toBe('paused')

    await vi.advanceTimersByTimeAsync(10 * 60_000)
    expect(socket.connectCalls).toBe(2)
    expect(stub.requestsTo(refreshRoute)).toHaveLength(1)

    realtime.refreshNow()
    await flush()
    expect(view.calls.refetch).toBe(1)
    expect(socket.connectCalls).toBe(3)

    await socket.simulateConnect()
    await flush()
    expect(realtime.state).toBe('live')
    expect(realtime.notice).toBe('back-online')
  })

  it.each([
    ['ticket_missing', 'ticket_missing'],
    ['a refusal without a code', null],
  ])('backs off from 1 s, doubling to 30 s, after %s', async (_label, code) => {
    const { socket } = await started()
    const expected = [1_000, 2_000, 4_000, 8_000, 16_000, 30_000, 30_000]

    for (const [attempt, delay] of expected.entries()) {
      await socket.simulateConnectError(code)
      await flush()
      const calls = socket.connectCalls
      await vi.advanceTimersByTimeAsync(delay - 1)
      expect(socket.connectCalls, `attempt ${attempt}`).toBe(calls)
      await vi.advanceTimersByTimeAsync(1)
      expect(socket.connectCalls, `attempt ${attempt}`).toBe(calls + 1)
    }
  })

  it('adds jitter around each backoff step within 1 s and 30 s', () => {
    expect(backoffDelay(0, () => 0)).toBe(backoffInitialMilliseconds)
    expect(backoffDelay(0, () => 1)).toBe(1_250)
    expect(backoffDelay(3, () => 0)).toBe(6_000)
    expect(backoffDelay(3, () => 1)).toBe(10_000)
    expect(backoffDelay(4, () => 0)).toBe(12_000)
    expect(backoffDelay(5, () => 0)).toBe(22_500)
    expect(backoffDelay(5, () => 1)).toBe(backoffMaximumMilliseconds)
    expect(backoffDelay(40, () => 0.5)).toBe(backoffMaximumMilliseconds)
  })

  it('reads the code from the refusal data only', () => {
    expect(
      connectionErrorCode(Object.assign(new Error('x'), { data: { code: 'forbidden' } })),
    ).toBe('forbidden')
    expect(connectionErrorCode(new Error('websocket error'))).toBeNull()
    expect(connectionErrorCode(Object.assign(new Error('x'), { data: { code: 7 } }))).toBeNull()
    expect(connectionErrorCode(null)).toBeNull()
  })

  it('leaves transport failures to the Socket.IO reconnection', async () => {
    const { stub, socket } = await started()

    socket.simulateTransportError()
    await vi.advanceTimersByTimeAsync(5 * 60_000)

    expect(socket.connectCalls).toBe(1)
    expect(stub.requestsTo(ticketRoute)).toHaveLength(0)
  })

  it('connects again after io server disconnect while signed in', async () => {
    const { realtime, view, socket } = await live()

    socket.simulateDisconnect('io server disconnect')

    expect(socket.connectCalls).toBe(2)
    expect(realtime.state).toBe('reconnecting')

    await socket.simulateConnect()
    await flush()
    expect(view.calls.refetch).toBe(2)
    expect(realtime.state).toBe('live')
  })

  it('leaves transport close, ping timeout and parse error to the Socket.IO reconnection', async () => {
    const { realtime, socket } = await live()

    for (const reason of ['transport close', 'transport error', 'ping timeout', 'parse error']) {
      socket.simulateDisconnect(reason)
    }
    await vi.advanceTimersByTimeAsync(60_000)

    expect(socket.connectCalls).toBe(1)
    expect(realtime.state).toBe('paused')
  })
})

describe('the ticket request', () => {
  it('refreshes once and retries after 401 auth.unauthenticated', async () => {
    const renewed = accessToken()
    const { stub, socket } = await started({
      [ticketRoute]: [problem(401, 'auth.unauthenticated'), ticketReply()],
      [refreshRoute]: json(sessionResponse(renewed)),
    })

    const data = await socket.simulateConnect()

    expect(Object.keys(data)).toEqual(['ticket'])
    expect(stub.requestsTo(refreshRoute)).toHaveLength(1)
    const requests = stub.requestsTo(ticketRoute)
    expect(requests).toHaveLength(2)
    expect(requests[1]?.headers.get('Authorization')).toBe(`Bearer ${renewed}`)
  })

  it.each([
    ['the refresh also ends the session', problem(401, 'auth.session-expired')],
    ['the refresh cannot reach the API', networkFailure()],
  ])('clears the session on 401 auth.session-expired when %s', async (_label, refreshReply) => {
    const { realtime, socket } = await started({
      [ticketRoute]: problem(401, 'auth.session-expired'),
      [refreshRoute]: refreshReply,
    })
    const session = useSessionStore()

    await socket.simulateConnectError('ticket_missing')
    await flush()

    expect(session.status).toBe('signed-out')
    expect(session.endReason).toBe('expired')
    expect(realtime.state).toBe('idle')
    expect(socket.disconnectCalls).toBe(1)
    await vi.advanceTimersByTimeAsync(5 * 60_000)
    expect(socket.connectCalls).toBe(1)
  })

  it('waits for Retry-After after a 429 before the next attempt', async () => {
    const { socket } = await started({
      [ticketRoute]: [
        problem(429, 'rate-limit.exceeded', {}, { 'Retry-After': '45' }),
        ticketReply(),
      ],
    })

    await socket.simulateConnectError('ticket_missing')
    await flush()
    await vi.advanceTimersByTimeAsync(45_000 - 1)
    expect(socket.connectCalls).toBe(1)

    await vi.advanceTimersByTimeAsync(1)
    expect(socket.connectCalls).toBe(2)
  })
})

describe('sign-out', () => {
  it('disconnects the socket and stops refetching', async () => {
    const { stub, realtime, view, socket } = await live({ [signOutRoute]: noContent() })

    await useSessionStore().signOut()

    expect(socket.disconnectCalls).toBe(1)
    expect(socket.connected).toBe(false)
    expect(realtime.state).toBe('idle')
    expect(realtime.refreshedAt).toBeNull()

    const before = stub.requests.length
    socket.emitServerEvent('job.status-changed', jobEvent())
    await vi.advanceTimersByTimeAsync(60_000)
    expect(stub.requests.length).toBe(before)
    expect(view.calls.refetch).toBe(1)
  })

  it('disconnects when the session ends in another way', async () => {
    const { realtime, socket } = await live()

    useSessionStore().expireSession()

    expect(socket.disconnectCalls).toBe(1)
    expect(realtime.state).toBe('idle')
  })
})

describe('event toasts and row flashes', () => {
  it('shows the newest history entry as a toast, then flashes the row when the list lands', async () => {
    const { realtime, socket } = await live({ [jobRoute(generatorJobId)]: json(generatorJob()) })
    const toasts = useToasts()

    socket.emitServerEvent('job.status-changed', jobEvent())
    await flush()

    expect(
      toasts.toasts.map(({ kind, title, body, note, meta, metaDetail, announcement }) => ({
        kind,
        title,
        body,
        note,
        meta,
        metaDetail,
        announcement,
      })),
    ).toEqual([
      {
        kind: 'event',
        title: 'Priya Natarajan started work',
        body: 'Standby generator load test is now In progress.',
        note: null,
        meta: 'JOB-1049',
        metaDetail: 'just now',
        announcement:
          'JOB-1049. Priya Natarajan started work. Standby generator load test is now In progress.',
      },
    ])
    expect([...realtime.flashedJobIds]).toEqual([])

    await vi.advanceTimersByTimeAsync(listDebounceMilliseconds)
    await flush()
    expect([...realtime.flashedJobIds]).toEqual([generatorJobId])

    await vi.advanceTimersByTimeAsync(rowFlashMilliseconds - 1)
    expect([...realtime.flashedJobIds]).toEqual([generatorJobId])
    await vi.advanceTimersByTimeAsync(1)
    expect([...realtime.flashedJobIds]).toEqual([])
  })

  it('keeps the row flashed when its toast closes, for 4 more seconds', async () => {
    const host = mount(ToastHost, { props: { placement: 'shell' }, attachTo: document.body })
    const { realtime, socket } = await live({ [jobRoute(generatorJobId)]: json(generatorJob()) })

    socket.emitServerEvent('job.status-changed', jobEvent())
    await vi.advanceTimersByTimeAsync(listDebounceMilliseconds)
    await flush()
    expect(host.findAll('.toast')).toHaveLength(1)
    expect([...realtime.flashedJobIds]).toEqual([generatorJobId])

    await vi.advanceTimersByTimeAsync(eventToastMilliseconds)
    await flush()
    expect(useToasts().toasts).toEqual([])
    expect(host.findAll('.toast')).toHaveLength(0)
    expect([...realtime.flashedJobIds]).toEqual([generatorJobId])

    await vi.advanceTimersByTimeAsync(rowFlashMilliseconds - eventToastMilliseconds - 1)
    expect([...realtime.flashedJobIds]).toEqual([generatorJobId])
    await vi.advanceTimersByTimeAsync(1)
    expect([...realtime.flashedJobIds]).toEqual([])
    expect([rowFlashMilliseconds, eventToastMilliseconds, backOnlineMilliseconds]).toEqual([
      12_000, 8_000, 8_000,
    ])
    host.unmount()
  })

  it('counts the flash only while the page is visible, and restarts it on a repeat event', async () => {
    let visibility: DocumentVisibilityState = 'visible'
    vi.spyOn(document, 'visibilityState', 'get').mockImplementation(() => visibility)
    const { realtime, socket } = await live({ [jobRoute(generatorJobId)]: json(generatorJob()) })

    socket.emitServerEvent('job.status-changed', jobEvent())
    await vi.advanceTimersByTimeAsync(listDebounceMilliseconds)
    await flush()
    await vi.advanceTimersByTimeAsync(5_000)

    visibility = 'hidden'
    document.dispatchEvent(new Event('visibilitychange'))
    await vi.advanceTimersByTimeAsync(60_000)
    expect([...realtime.flashedJobIds]).toEqual([generatorJobId])

    visibility = 'visible'
    document.dispatchEvent(new Event('visibilitychange'))
    socket.emitServerEvent('job.status-changed', jobEvent())
    await vi.advanceTimersByTimeAsync(listDebounceMilliseconds)
    await flush()
    await vi.advanceTimersByTimeAsync(rowFlashMilliseconds - 1)
    expect([...realtime.flashedJobIds]).toEqual([generatorJobId])
    await vi.advanceTimersByTimeAsync(1)
    expect([...realtime.flashedJobIds]).toEqual([])
  })

  it('flashes nothing after a connect refetch or a failed list refetch', async () => {
    const { realtime, view, socket } = await live({
      [jobRoute(generatorJobId)]: json(generatorJob()),
    })

    socket.simulateDisconnect('transport close')
    await socket.simulateConnect()
    await flush()
    expect([...realtime.flashedJobIds]).toEqual([])

    view.respondWith(apiFailure(500, 'server.error'))
    socket.emitServerEvent('job.status-changed', jobEvent())
    await vi.advanceTimersByTimeAsync(listDebounceMilliseconds)
    await flush()

    expect([...realtime.flashedJobIds]).toEqual([])
    expect(realtime.notice).toBeNull()
  })

  it('shows no event toast on a page without live notices', async () => {
    const stub = await signedIn({ [jobRoute(generatorJobId)]: json(generatorJob()) })
    const realtime = useRealtimeStore()
    const form = recordingView({ page: null })
    realtime.register(form.registration)
    realtime.start()
    const socket = lastFakeSocket()
    await socket.simulateConnect()
    await flush()

    socket.emitServerEvent('job.status-changed', jobEvent())
    await flush()

    expect(stub.requestsTo(jobRoute(generatorJobId))).toHaveLength(1)
    expect(useToasts().toasts).toEqual([])
  })

  it('shows one toast per history entry', async () => {
    const { socket } = await live({ [jobRoute(generatorJobId)]: json(generatorJob()) })

    socket.emitServerEvent('job.status-changed', jobEvent())
    await flush()
    await vi.advanceTimersByTimeAsync(2_000)
    socket.emitServerEvent('job.status-changed', jobEvent())
    await flush()

    expect(useToasts().toasts).toHaveLength(1)
  })

  it('shows no toast for a change the signed-in administrator made, but flashes the row', async () => {
    const own = generatorJob({
      status: 'Cancelled',
      history: [historyEntry({ action: 'Cancelled', actor: dana, toStatus: 'Cancelled' })],
    })
    const { realtime, socket } = await live({ [jobRoute(generatorJobId)]: json(own) })

    socket.emitServerEvent('job.status-changed', jobEvent({ status: 'Cancelled' }))
    await vi.advanceTimersByTimeAsync(listDebounceMilliseconds)
    await flush()

    expect(useToasts().toasts).toEqual([])
    expect([...realtime.flashedJobIds]).toEqual([generatorJobId])
  })

  it('ignores a 404 for the event job', async () => {
    const { view, socket } = await live({
      [jobRoute(generatorJobId)]: problem(404, 'job.not-found'),
    })

    socket.emitServerEvent('job.status-changed', jobEvent())
    await flush()

    expect(useToasts().toasts).toEqual([])
    expect(view.calls.applied).toEqual([])
  })

  it('writes the §6 title for each history action, never repeating the job number', () => {
    const person = (displayName: string) => ({ id: crypto.randomUUID(), displayName })
    const cases = [
      [historyEntry(), 'Dana Whitfield created a job'],
      [
        historyEntry({
          action: 'Assigned',
          technician: { id: marco.id, displayName: marco.displayName },
        }),
        'Dana Whitfield assigned a job to Marco Reyes',
      ],
      [historyEntry({ action: 'Assigned', technician: null }), 'Dana Whitfield assigned a job'],
      [
        historyEntry({ action: 'Started', actor: person('Priya Natarajan') }),
        'Priya Natarajan started work',
      ],
      [
        historyEntry({ action: 'PutOnHold', actor: person(tom.displayName) }),
        'Tom Becker put a job on hold',
      ],
      [
        historyEntry({ action: 'Resumed', actor: person(tom.displayName) }),
        'Tom Becker resumed work',
      ],
      [
        historyEntry({ action: 'Completed', actor: person('Aisha Karim') }),
        'Aisha Karim completed a job',
      ],
      [historyEntry({ action: 'Cancelled' }), 'Dana Whitfield cancelled a job'],
      [historyEntry({ action: 'Archived' as never }), 'Dana Whitfield updated a job'],
    ] as const

    for (const [entry, title] of cases) {
      expect(liveToastTitle(entry)).toBe(title)
      expect(liveToastTitle(entry)).not.toMatch(/JOB-/)
    }
  })

  it('writes the body with the status label', () => {
    expect(liveToastBody(generatorJob())).toBe('Standby generator load test is now In progress.')
    expect(
      liveToastBody(generatorJob({ title: 'Water heater leaking at base', status: 'OnHold' })),
    ).toBe('Water heater leaking at base is now On hold.')
  })

  it('adds the note line to hold and cancel entries only, cut at 80 characters at a space', () => {
    const hold = historyEntry({ action: 'PutOnHold', note: '  Waiting for replacement gas valve ' })
    const long =
      'Waiting for the replacement fan motor quote to be approved by the facilities manager today.'
    const unbroken = 'x'.repeat(120)

    expect(liveToastNote(hold)).toBe('“Waiting for replacement gas valve”')
    expect(
      liveToastNote(historyEntry({ action: 'Cancelled', note: 'Customer called to cancel' })),
    ).toBe('“Customer called to cancel”')
    expect(liveToastNote(historyEntry({ action: 'Started', note: 'ignored' }))).toBeNull()
    expect(liveToastNote(historyEntry({ action: 'PutOnHold', note: null }))).toBeNull()
    expect(liveToastNote(historyEntry({ action: 'PutOnHold', note: '   ' }))).toBeNull()
    expect(toastNoteLength).toBe(80)
    expect(shortenNote(long)).toBe(
      'Waiting for the replacement fan motor quote to be approved by the facilities…',
    )
    expect(shortenNote(long).length).toBeLessThanOrEqual(80)
    expect(shortenNote(unbroken)).toBe(`${'x'.repeat(79)}…`)
    expect(shortenNote('Line one\nline two')).toBe('Line one line two')
  })

  it('announces the job number, title, body and note as sentences', () => {
    const job = generatorJob({
      number: 1044,
      title: 'Water heater leaking at base',
      status: 'OnHold',
    })
    const entry = historyEntry({
      action: 'PutOnHold',
      actor: { id: tom.id, displayName: tom.displayName },
      note: 'Waiting for replacement gas valve',
      occurredAt: onDemoDay(0, 10, 31),
    })

    const toast = liveToastFor(job, entry, demoToday)

    expect(toast.announcement).toBe(
      'JOB-1044. Tom Becker put a job on hold. Water heater leaking at base is now On hold. “Waiting for replacement gas valve”',
    )
    expect(toast.meta).toBe('JOB-1044')
    expect(toast.metaDetail).toBe('10:31 AM')
  })

  it('shows just now under a minute, and the moment of an older entry', () => {
    const recent = historyEntry({
      occurredAt: new Date(demoToday.getTime() - 59_000).toISOString(),
    })

    expect(liveToastFor(generatorJob(), recent, demoToday).metaDetail).toBe('just now')
    expect(
      liveToastFor(generatorJob(), historyEntry({ occurredAt: onDemoDay(-1, 16, 5) }), demoToday)
        .metaDetail,
    ).toBe('Sun 18 Oct, 4:05 PM')
  })
})
