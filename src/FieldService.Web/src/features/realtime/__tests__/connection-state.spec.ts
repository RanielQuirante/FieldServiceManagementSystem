import { mount, type VueWrapper } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, type Router } from 'vue-router'

import App from '@/app/App.vue'
import { createAppRouter, installNavigationGuards } from '@/app/router'
import { sessionResponse } from '@/features/auth/__tests__/session-fixtures'
import { useSessionStore } from '@/features/auth/session-store'
import { demoSummary } from '@/features/dashboard/__tests__/dashboard-fixtures'
import {
  demoSummaries,
  demoTechnicians,
  demoToday,
  jobList,
} from '@/features/jobs/__tests__/job-fixtures'
import { useToasts } from '@/shared/composables/use-toasts'
import { lastFakeSocket, type FakeSocket } from '@/shared/testing/fake-socket'
import { json, noContent, type FetchStub, type RouteTable } from '@/shared/testing/fetch-stub'
import { kept } from '@/shared/testing/kept-text'
import ConnectionBanner from '@/shared/ui/ConnectionBanner.vue'
import LivePill from '@/shared/ui/LivePill.vue'

import { listDebounceMilliseconds } from '../live-refetch'
import { offlineNoticeAfterMilliseconds, pausedAfterMilliseconds } from '../realtime-client'
import { backOnlineMilliseconds, useRealtimeStore } from '../realtime-store'
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
  type RecordingView,
} from './realtime-fixtures'

let mounted: VueWrapper | null = null

beforeEach(() => {
  vi.useFakeTimers({ now: demoToday })
  vi.spyOn(Math, 'random').mockReturnValue(0.5)
})

afterEach(() => {
  mounted?.unmount()
  mounted = null
  useRealtimeStore().stop()
})

interface Started {
  readonly stub: FetchStub
  readonly realtime: ReturnType<typeof useRealtimeStore>
  readonly view: RecordingView
  readonly socket: FakeSocket
}

async function started(routes: RouteTable = {}): Promise<Started> {
  const stub = await signedIn(routes)
  const realtime = useRealtimeStore()
  const view = recordingView()
  realtime.register(view.registration)
  realtime.start()
  return { stub, realtime, view, socket: lastFakeSocket() }
}

async function live(routes: RouteTable = {}): Promise<Started> {
  const context = await started(routes)
  await context.socket.simulateConnect()
  await flush()
  return context
}

describe('connection states', () => {
  it('shows nothing while connecting, then Live once the connect refetch lands', async () => {
    const { realtime, socket } = await started()

    expect(realtime.state).toBe('connecting')
    expect(realtime.notice).toBeNull()

    await vi.advanceTimersByTimeAsync(10_000)
    expect(realtime.state).toBe('connecting')
    expect(realtime.notice).toBeNull()

    await socket.simulateConnect()
    await flush()
    expect(realtime.state).toBe('live')
    expect(realtime.notice).toBeNull()
  })

  it('changes the pill at once and shows the Reconnecting banner only after 3 seconds', async () => {
    const { realtime, socket } = await live()

    socket.simulateDisconnect('transport close')
    expect(realtime.state).toBe('reconnecting')
    expect(realtime.notice).toBeNull()

    await vi.advanceTimersByTimeAsync(offlineNoticeAfterMilliseconds - 1)
    expect(realtime.notice).toBeNull()

    await vi.advanceTimersByTimeAsync(1)
    expect(offlineNoticeAfterMilliseconds).toBe(3_000)
    expect(realtime.notice).toBe('reconnecting')
  })

  it('keeps a reconnect inside 3 seconds quiet: no banner and no Back online', async () => {
    const { realtime, view, socket } = await live()

    socket.simulateDisconnect('io server disconnect')
    await vi.advanceTimersByTimeAsync(2_500)
    await socket.simulateConnect()
    await flush()

    expect(view.calls.refetch).toBe(2)
    expect(realtime.state).toBe('live')
    expect(realtime.notice).toBeNull()
    expect(realtime.refreshedAt?.getTime()).toBe(demoToday.getTime() + 2_500)
  })

  it('shows Back online with the refetch time for 8 seconds after a shown banner', async () => {
    const { realtime, socket } = await live()

    socket.simulateDisconnect('transport close')
    await vi.advanceTimersByTimeAsync(5_000)
    await socket.simulateConnect()
    await flush()

    expect(realtime.state).toBe('live')
    expect(realtime.notice).toBe('back-online')
    expect(realtime.noticeTime).toBe(kept('10:42~AM'))

    await vi.advanceTimersByTimeAsync(backOnlineMilliseconds - 1)
    expect(realtime.notice).toBe('back-online')
    await vi.advanceTimersByTimeAsync(1)
    expect(realtime.notice).toBeNull()
  })

  it('names what the open page refreshed in Back online, from the copy the page registered', async () => {
    const stub = await signedIn()
    const realtime = useRealtimeStore()
    const view = recordingView({
      page: {
        refreshFailedTitle: 'The dashboard could not be refreshed.',
        refreshedLabel: 'The dashboard was refreshed at',
        showingFrom: (time) => `Showing data from ${time}.`,
      },
    })
    const unregister = realtime.register(view.registration)
    realtime.start()
    const socket = lastFakeSocket()
    await socket.simulateConnect()
    await flush()

    expect(realtime.refreshedLabel).toBe('The dashboard was refreshed at')
    socket.simulateDisconnect('transport close')
    await vi.advanceTimersByTimeAsync(5_000)
    await socket.simulateConnect()
    await flush()

    expect(realtime.notice).toBe('back-online')
    expect(realtime.refreshedLabel).toBe('The dashboard was refreshed at')
    unregister()
    expect(realtime.refreshedLabel).toBe('Jobs refreshed at')
    expect(stub.requestsTo(ticketRoute).length).toBeGreaterThan(0)
  })

  it('removes Back online at once when the connection drops again', async () => {
    const { realtime, socket } = await live()
    socket.simulateDisconnect('transport close')
    await vi.advanceTimersByTimeAsync(5_000)
    await socket.simulateConnect()
    await flush()

    socket.simulateDisconnect('transport close')

    expect(realtime.notice).toBeNull()
    await vi.advanceTimersByTimeAsync(offlineNoticeAfterMilliseconds)
    expect(realtime.notice).toBe('reconnecting')
  })

  it('counts the 3 seconds from page load when the first attempts fail', async () => {
    const { realtime, socket } = await started()

    await vi.advanceTimersByTimeAsync(1_000)
    await socket.simulateConnectError('ticket_missing')
    expect(realtime.state).toBe('reconnecting')
    expect(realtime.notice).toBeNull()

    await vi.advanceTimersByTimeAsync(2_000)
    expect(realtime.notice).toBe('reconnecting')
  })

  it('shows the Reconnecting banner at once for a first failure after 3 seconds', async () => {
    const { realtime, socket } = await started()

    await vi.advanceTimersByTimeAsync(5_000)
    expect(realtime.notice).toBeNull()
    socket.simulateTransportError()

    expect(realtime.notice).toBe('reconnecting')
  })

  it('pauses live updates after 30 seconds without a connection while retries continue', async () => {
    const { realtime, socket } = await live()

    socket.simulateDisconnect('transport close')
    await vi.advanceTimersByTimeAsync(pausedAfterMilliseconds - 1)
    expect(realtime.state).toBe('reconnecting')

    await vi.advanceTimersByTimeAsync(1)
    expect(pausedAfterMilliseconds).toBe(30_000)
    expect(realtime.state).toBe('paused')
    expect(realtime.notice).toBe('paused')
    expect(realtime.noticeTime).toBe(kept('10:42~AM'))

    await socket.simulateConnect()
    await flush()
    expect(realtime.state).toBe('live')
    expect(realtime.notice).toBe('back-online')
  })

  it('refetches with Refresh now and keeps Paused with the new time while still offline', async () => {
    const { realtime, view, socket } = await live()
    socket.simulateDisconnect('transport close')
    await vi.advanceTimersByTimeAsync(pausedAfterMilliseconds)
    const connects = socket.connectCalls

    const refreshing = realtime.refreshNow()
    expect(realtime.refreshing).toBe(true)
    await refreshing
    await flush()

    expect(view.calls.refetch).toBe(2)
    expect(socket.connectCalls).toBe(connects + 1)
    expect(realtime.refreshing).toBe(false)
    expect(realtime.notice).toBe('paused')
    expect(realtime.noticeTime).toBe(kept('10:42~AM'))
    expect(realtime.refreshedAt?.getTime()).toBe(demoToday.getTime() + pausedAfterMilliseconds)
  })
})

describe('Refresh failed', () => {
  it('shows when the connect refetch fails and keeps the pill time', async () => {
    const { realtime, view, socket } = await live()
    socket.simulateDisconnect('transport close')
    view.respondWith(apiFailure(503, 'server.unavailable'))
    await socket.simulateConnect()
    await flush()

    expect(realtime.state).toBe('live')
    expect(realtime.notice).toBe('refresh-failed')
    expect(realtime.refreshedAt).toEqual(demoToday)
    expect(realtime.refreshFailure).toEqual({
      title: 'Jobs could not be refreshed.',
      message: kept('Showing jobs from 10:42~AM.'),
      reference: expect.any(String),
    })
  })

  it('reads "Try again in N seconds." without a reference after a 429', async () => {
    const { realtime, view, socket } = await live()
    socket.simulateDisconnect('transport close')
    view.respondWith(apiFailure(429, 'rate-limit.exceeded', 12))
    await socket.simulateConnect()
    await flush()

    expect(realtime.refreshFailure).toEqual({
      title: 'Jobs could not be refreshed.',
      message: 'Try again in 12 seconds.',
      reference: null,
    })
  })

  it('has no reference when the API could not be reached', async () => {
    const { realtime, view, socket } = await live()
    socket.simulateDisconnect('transport close')
    view.respondWith(apiFailure(0, ''))
    await socket.simulateConnect()
    await flush()

    expect(realtime.refreshFailure?.reference).toBeNull()
  })

  it('leaves when an event list refetch succeeds', async () => {
    const { realtime, view, socket } = await live({
      [jobRoute(generatorJobId)]: json(generatorJob()),
    })
    socket.simulateDisconnect('transport close')
    view.respondWith(apiFailure(500, 'server.error'))
    await socket.simulateConnect()
    await flush()
    expect(realtime.notice).toBe('refresh-failed')

    view.respondWith('refreshed')
    socket.emitServerEvent('job.status-changed', jobEvent())
    await vi.advanceTimersByTimeAsync(listDebounceMilliseconds)
    await flush()

    expect(realtime.notice).toBeNull()
  })

  it('leaves when Try again succeeds, and stays when it fails again', async () => {
    const { realtime, view, socket } = await live()
    socket.simulateDisconnect('transport close')
    view.respondWith(apiFailure(500, 'server.error'))
    await socket.simulateConnect()
    await flush()

    await realtime.refreshNow()
    expect(realtime.notice).toBe('refresh-failed')

    view.respondWith('refreshed')
    await realtime.refreshNow()
    expect(realtime.notice).toBeNull()
  })

  it('is replaced by the Reconnecting banner when its timer fires', async () => {
    const { realtime, view, socket } = await live()
    socket.simulateDisconnect('transport close')
    view.respondWith(apiFailure(500, 'server.error'))
    await socket.simulateConnect()
    await flush()

    socket.simulateDisconnect('transport close')
    expect(realtime.notice).toBe('refresh-failed')

    await vi.advanceTimersByTimeAsync(offlineNoticeAfterMilliseconds)
    expect(realtime.notice).toBe('reconnecting')
  })
})

describe('the banner slot and toasts by page', () => {
  it('shows no banner on a page without live notices', async () => {
    const stub = await signedIn()
    const realtime = useRealtimeStore()
    realtime.register(recordingView({ page: null }).registration)
    realtime.start()

    await vi.advanceTimersByTimeAsync(pausedAfterMilliseconds)

    expect(stub.requests.length).toBeGreaterThan(0)
    expect(realtime.state).toBe('paused')
    expect(realtime.notice).toBeNull()
  })

  it('empties the slot and removes every toast on sign-out', async () => {
    const { realtime, socket } = await live({ [signOutRoute]: noContent() })
    useToasts().show({ title: 'Priya Natarajan started work' })
    useToasts().showError(null)
    socket.simulateDisconnect('transport close')
    await vi.advanceTimersByTimeAsync(5_000)
    expect(realtime.notice).toBe('reconnecting')

    await useSessionStore().signOut()

    expect(realtime.notice).toBeNull()
    expect(useToasts().toasts).toEqual([])
  })
})

describe('LivePill', () => {
  it.each([
    ['connecting', 'Connecting…', false],
    ['reconnecting', 'Reconnecting…', true],
    ['paused', 'Live updates paused', false],
  ] as const)('reads %s as "%s"', (state, text, amber) => {
    const pill = mount(LivePill, { props: { state, updatedAt: demoToday } })

    expect(pill.text()).toBe(text)
    expect(pill.attributes('role')).toBeUndefined()
    expect(pill.attributes('data-state')).toBe(state)
    expect(pill.classes('live-pill--amber')).toBe(amber)
    pill.unmount()
  })

  it('reads "Live, updated 10:42 AM" with the separator hidden', () => {
    const pill = mount(LivePill, { props: { state: 'live', updatedAt: demoToday } })
    const text = pill.find('.live-pill__text')

    expect(text.text()).toBe(kept('Live~· , updated 10:42 AM'))
    expect(text.find('[aria-hidden="true"]').text()).toBe('·')
    expect(text.find('.visually-hidden').text()).toBe(',')
    expect(pill.find('.live-pill__dot').attributes('aria-hidden')).toBe('true')
    expect(pill.findAll('[role]')).toHaveLength(0)
    pill.unmount()
  })
})

describe('ConnectionBanner', () => {
  it('is one polite status element that holds at most one banner without a role', () => {
    const slot = mount(ConnectionBanner, { props: { notice: null } })

    expect(slot.attributes('role')).toBe('status')
    expect(slot.element.children).toHaveLength(0)
    expect(slot.classes('connection-banner--shown')).toBe(false)
    slot.unmount()
  })

  it.each([
    ['reconnecting', 'Reconnecting to live updates. Job statuses on this page may be out of date.'],
    ['back-online', 'Back online. Jobs refreshed at 10:44 AM.'],
    ['paused', 'Live updates are paused. Showing data from 10:44 AM.Refresh now'],
  ] as const)('writes the %s copy', (notice, text) => {
    const slot = mount(ConnectionBanner, { props: { notice, time: '10:44 AM' } })

    expect(slot.text()).toBe(text)
    expect(slot.findAll('.connection-banner__banner')).toHaveLength(1)
    expect(slot.find('.connection-banner__banner').attributes('role')).toBeUndefined()
    expect(slot.classes('connection-banner--shown')).toBe(true)
    slot.unmount()
  })

  it.each([
    ['Jobs refreshed at', 'Back online. Jobs refreshed at 10:44 AM.'],
    ['This job was refreshed at', 'Back online. This job was refreshed at 10:44 AM.'],
    ['The report was refreshed at', 'Back online. The report was refreshed at 10:44 AM.'],
    ['The dashboard was refreshed at', 'Back online. The dashboard was refreshed at 10:44 AM.'],
    ['Technicians refreshed at', 'Back online. Technicians refreshed at 10:44 AM.'],
  ] as const)('names the page in Back online: %s', (refreshedLabel, text) => {
    const slot = mount(ConnectionBanner, {
      props: { notice: 'back-online', time: '10:44 AM', refreshedLabel },
    })

    expect(slot.text()).toBe(text)
    expect(slot.find('.connection-banner__time').text()).toBe('10:44 AM')
    slot.unmount()
  })

  it('writes Refresh failed with the reference and Try again, busy as Refreshing…', async () => {
    const slot = mount(ConnectionBanner, {
      props: {
        notice: 'refresh-failed',
        failure: {
          title: 'The job could not be refreshed.',
          message: 'Showing this job as of 10:31 AM.',
          reference: 'dd85f06c-7606-4dfc-a702-fa39647047d8',
        },
      },
    })

    expect(slot.find('.connection-banner__text').text()).toBe(
      'The job could not be refreshed. Showing this job as of 10:31 AM.',
    )
    expect(slot.find('.connection-banner__reference').text()).toBe(
      'Reference dd85f06c-7606-4dfc-a702-fa39647047d8',
    )
    expect(slot.find('button').text()).toBe('Try again')

    await slot.find('button').trigger('click')
    expect(slot.emitted('refresh')).toHaveLength(1)

    await slot.setProps({ busy: true })
    expect(slot.find('button').text()).toBe('Refreshing…')
    expect(slot.find('button').attributes('aria-disabled')).toBe('true')
    slot.unmount()
  })

  it('moves focus from Refresh now to Try again, and to the page heading when the slot empties', async () => {
    document.body.innerHTML = '<main><h1 tabindex="-1">Jobs</h1></main><div id="slot"></div>'
    const slot = mount(ConnectionBanner, {
      props: { notice: 'paused', time: '10:31 AM' },
      attachTo: '#slot',
    })
    slot.find<HTMLButtonElement>('button').element.focus()

    await slot.setProps({
      notice: 'refresh-failed',
      failure: {
        title: 'Jobs could not be refreshed.',
        message: 'Showing jobs from 10:31 AM.',
        reference: null,
      },
    })
    await flush(1)
    expect(document.activeElement?.textContent).toBe('Try again')

    await slot.setProps({ notice: null })
    await flush(1)
    expect(document.activeElement?.tagName).toBe('H1')
    slot.unmount()
    document.body.innerHTML = ''
  })
})

describe('the shell', () => {
  async function openApp(
    path: string,
    routes: RouteTable = {},
  ): Promise<{
    wrapper: VueWrapper
    router: Router
    stub: FetchStub
    socket: FakeSocket
  }> {
    const { stubFetch } = await import('@/shared/testing/fetch-stub')
    const stub = stubFetch({
      [refreshRoute]: json(sessionResponse()),
      [ticketRoute]: ticketReply(),
      'GET /api/jobs': json(jobList(demoSummaries(12))),
      'GET /api/technicians': json({ items: demoTechnicians }),
      'GET /api/checklist-templates': json({ items: [] }),
      ...routes,
    })
    const router = createAppRouter(createMemoryHistory())
    installNavigationGuards(router)
    await router.push(path)
    const wrapper = mount(App, { global: { plugins: [router] }, attachTo: document.body })
    mounted = wrapper
    await flush()
    return { wrapper, router, stub, socket: lastFakeSocket() }
  }

  it('puts the pill in the top bar, the slot first in main and the toast host last', async () => {
    const { wrapper } = await openApp('/jobs')

    const pill = wrapper.find('header .app-shell__status .live-pill')
    expect(pill.text()).toBe('Connecting…')
    const main = wrapper.find('main#main')
    expect(main.element.firstElementChild?.getAttribute('role')).toBe('status')
    expect(main.element.firstElementChild?.classList.contains('connection-banner')).toBe(true)
    const shell = wrapper.find('.app-shell').element
    expect(shell.lastElementChild?.getAttribute('aria-label')).toBe('Notifications')
    expect(shell.lastElementChild?.getAttribute('aria-live')).toBeNull()
  })

  it.each([
    ['/', 'The dashboard was refreshed at'],
    ['/jobs', 'Jobs refreshed at'],
    [`/jobs/${generatorJobId}`, 'This job was refreshed at'],
    [`/jobs/${generatorJobId}/inspection`, 'The report was refreshed at'],
    ['/technicians', 'Technicians refreshed at'],
  ])('registers the Back online sentence of the page at %s', async (path, refreshedLabel) => {
    const { wrapper } = await openApp(path, {
      'GET /api/dashboard/summary': json(demoSummary()),
      [jobRoute(generatorJobId)]: json(generatorJob()),
      [`GET /api/jobs/${generatorJobId}/inspection`]: noContent(),
    })

    expect(useRealtimeStore().refreshedLabel).toBe(refreshedLabel)
    expect(wrapper.find('main h1').exists()).toBe(true)
  })

  it('shows the Reconnecting banner on Jobs but not on New job', async () => {
    const { wrapper, router, socket } = await openApp('/jobs')
    await socket.simulateConnect()
    await flush()
    expect(wrapper.find('.live-pill').text()).toBe(kept('Live~· , updated 10:42 AM'))

    socket.simulateDisconnect('transport close')
    await flush(1)
    expect(wrapper.find('.live-pill').text()).toBe('Reconnecting…')
    expect(wrapper.find('.connection-banner__banner').exists()).toBe(false)

    await vi.advanceTimersByTimeAsync(offlineNoticeAfterMilliseconds)
    expect(wrapper.find('.connection-banner__banner').text()).toContain(
      'Reconnecting to live updates.',
    )

    await router.push('/jobs/new')
    await flush()
    expect(wrapper.find('.live-pill').text()).toBe('Reconnecting…')
    expect(wrapper.find('.connection-banner').exists()).toBe(true)
    expect(wrapper.find('.connection-banner__banner').exists()).toBe(false)
  })

  it('announces each event toast once through the hidden status, with no role on the toast', async () => {
    const { wrapper, socket } = await openApp('/jobs', {
      [jobRoute(generatorJobId)]: json(generatorJob()),
    })
    await socket.simulateConnect()
    await flush()

    socket.emitServerEvent('job.status-changed', jobEvent())
    await flush()

    const region = wrapper.find('[aria-label="Notifications"]')
    const status = region.find('[role="status"]')
    expect(status.attributes('aria-atomic')).toBe('false')
    expect(status.classes('visually-hidden')).toBe(true)
    expect(status.text()).toBe(
      'JOB-1049. Priya Natarajan started work. Standby generator load test is now In progress.',
    )
    const toast = region.find('.toast')
    expect(toast.attributes('role')).toBeUndefined()
    expect(toast.find('.toast__meta').text()).toBe(kept('JOB-1049~· , just now'))
    expect(toast.find('.toast__title').text()).toBe('Priya Natarajan started work')
  })
})
