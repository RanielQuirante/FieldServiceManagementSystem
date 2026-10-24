import { flushPromises, mount, type DOMWrapper, type VueWrapper } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, effectScope, h, type EffectScope } from 'vue'
import {
  createMemoryHistory,
  createRouter,
  type LocationQuery,
  type LocationQueryRaw,
  type Router,
} from 'vue-router'

import App from '@/app/App.vue'
import { createAppRouter, installNavigationGuards } from '@/app/router'
import { ruleDeclarations } from '@/app/styles/__tests__/stylesheet-source'
import { sessionResponse } from '@/features/auth/__tests__/session-fixtures'
import {
  aishaTechnician,
  demoTechnicians,
  demoToday,
  lenaTechnician,
  marcoTechnician,
  priyaTechnician,
  samuelTechnician,
  technician,
  tomTechnician,
} from '@/features/jobs/__tests__/job-fixtures'
import {
  flush,
  generatorJob,
  generatorJobId,
  jobEvent,
  jobRoute,
  refreshRoute,
  signedIn,
  ticketReply,
  ticketRoute,
} from '@/features/realtime/__tests__/realtime-fixtures'
import { listDebounceMilliseconds, type LivePageCopy } from '@/features/realtime/live-refetch'
import { useRealtimeStore } from '@/features/realtime/realtime-store'
import { noBreakSpace } from '@/shared/format/date-time'
import { contractProblems } from '@/shared/testing/contract-schema'
import { lastFakeSocket } from '@/shared/testing/fake-socket'
import {
  deferred,
  json,
  problem,
  stubFetch,
  type FetchStub,
  type RouteTable,
} from '@/shared/testing/fetch-stub'

import {
  useTechnicianFilters,
  type TechnicianFilterControls,
} from '../composables/use-technician-filters'
import { useTechniciansData } from '../composables/use-technicians-data'
import {
  filterTechnicians,
  hasActiveTechnicianFilters,
  isOnShift,
  matchesShift,
  matchesTechnicianSearch,
  noTechnicianFilters,
  onShiftSummary,
  phoneSearchMinimumDigits,
  queryFromTechnicianFilters,
  shiftCounts,
  technicianFiltersFromQuery,
  technicianSearchMaximumLength,
  type TechnicianFilters,
} from '../technician-filters'
import type { Technician } from '../technicians-api'
import { useTechniciansStore } from '../technicians-store'

const techniciansRoute = 'GET /api/technicians'

const techniciansCopy: LivePageCopy = {
  refreshFailedTitle: 'Technicians could not be refreshed.',
  refreshedLabel: 'Technicians refreshed at',
  showingFrom: (time) => `Showing technicians from ${time}.`,
}

const inactiveOnShift = technician({
  id: '01a0f6b1-3e00-7000-8000-000000000007',
  displayName: 'Nadia Flores',
  email: 'nadia.flores@fieldservice.example',
  phoneNumber: '(555) 014-0007',
  trade: 'Plumbing',
  isActive: false,
  isOnShift: true,
  availableForAssignment: false,
  openJobCount: 0,
  currentJob: null,
  nextJob: null,
})

function names(technicians: readonly Technician[]): string[] {
  return technicians.map((entry) => entry.displayName)
}

function searchFor(query: string): string[] {
  return names(filterTechnicians(demoTechnicians, { shift: 'all', search: query }))
}

let scope: EffectScope | null = null

afterEach(() => {
  scope?.stop()
  scope = null
  useRealtimeStore().stop()
})

describe('the technician filters', () => {
  it('treats only active technicians on shift as On shift, and every other one as Off shift', () => {
    expect(isOnShift(aishaTechnician)).toBe(true)
    expect(isOnShift(samuelTechnician)).toBe(false)
    expect(isOnShift(inactiveOnShift)).toBe(false)
    expect(matchesShift(inactiveOnShift, 'off-shift')).toBe(true)
    expect(matchesShift(inactiveOnShift, 'on-shift')).toBe(false)
    expect(matchesShift(inactiveOnShift, 'all')).toBe(true)
  })

  it('lists On shift and Off shift in the API order', () => {
    const technicians = [...demoTechnicians, inactiveOnShift]

    expect(names(filterTechnicians(technicians, { shift: 'on-shift', search: '' }))).toEqual([
      'Aisha Karim',
      'Lena Park',
      'Marco Reyes',
      'Priya Natarajan',
      'Tom Becker',
    ])
    expect(names(filterTechnicians(technicians, { shift: 'off-shift', search: '' }))).toEqual([
      'Samuel Osei',
      'Nadia Flores',
    ])
  })

  it('counts every technician in All, On shift and Off shift, whatever the search', () => {
    expect(shiftCounts(demoTechnicians)).toEqual({ all: 6, onShift: 5, offShift: 1 })
    expect(shiftCounts([...demoTechnicians, inactiveOnShift])).toEqual({
      all: 7,
      onShift: 5,
      offShift: 2,
    })
    expect(searchFor('aisha')).toEqual(['Aisha Karim'])
    expect(shiftCounts(demoTechnicians)).toEqual({ all: 6, onShift: 5, offShift: 1 })
  })

  it('counts the header subline as active technicians on shift of active technicians', () => {
    expect(onShiftSummary(demoTechnicians)).toEqual({ onShift: 5, total: 6 })
    expect(onShiftSummary([...demoTechnicians, inactiveOnShift])).toEqual({ onShift: 5, total: 6 })
    expect(onShiftSummary([])).toEqual({ onShift: 0, total: 0 })
  })

  it('searches the name, trimmed and in any case', () => {
    expect(searchFor('marco')).toEqual(['Marco Reyes'])
    expect(searchFor('  PARK ')).toEqual(['Lena Park'])
    expect(searchFor('')).toEqual(names(demoTechnicians))
    expect(searchFor('   ')).toEqual(names(demoTechnicians))
  })

  it('searches the email and the trade label', () => {
    expect(searchFor('tom.becker@')).toEqual(['Tom Becker'])
    expect(searchFor('fire safety')).toEqual(['Lena Park'])
    expect(searchFor('HVAC')).toEqual(['Marco Reyes', 'Samuel Osei'])
    expect(searchFor('electrical')).toEqual(['Aisha Karim', 'Priya Natarajan'])
    expect(searchFor('FireSafety')).toEqual([])
  })

  it('searches the digits of the phone number once the query holds three digits', () => {
    expect(phoneSearchMinimumDigits).toBe(3)
    expect(searchFor('014-78')).toEqual(['Aisha Karim'])
    expect(searchFor('(555) 014-2231')).toEqual(['Marco Reyes'])
    expect(searchFor('7810')).toEqual(['Aisha Karim'])
    expect(searchFor('555')).toEqual(names(demoTechnicians))
    expect(searchFor('78')).toEqual([])
    expect(matchesTechnicianSearch(aishaTechnician, '1-4-7')).toBe(true)
    expect(matchesTechnicianSearch(aishaTechnician, '9-9-9')).toBe(false)
  })

  it('combines the shift filter and the search in the API order, never sorting names itself', () => {
    const reversed = [...demoTechnicians].reverse()

    expect(names(filterTechnicians(reversed, { shift: 'on-shift', search: 'electrical' }))).toEqual(
      ['Priya Natarajan', 'Aisha Karim'],
    )
    expect(names(filterTechnicians(reversed, noTechnicianFilters))).toEqual(names(reversed))
  })

  const addressReadings: readonly (readonly [LocationQuery, TechnicianFilters])[] = [
    [{}, { shift: 'all', search: '' }],
    [{ shift: 'on-shift' }, { shift: 'on-shift', search: '' }],
    [
      { shift: 'off-shift', search: 'sam' },
      { shift: 'off-shift', search: 'sam' },
    ],
    [{ shift: 'ON-SHIFT' }, { shift: 'all', search: '' }],
    [
      { shift: 'everyone', search: 'tom' },
      { shift: 'all', search: 'tom' },
    ],
    [{ shift: ['off-shift', 'on-shift'] }, { shift: 'off-shift', search: '' }],
    [{ search: ['lena', 'marco'] }, { shift: 'all', search: 'lena' }],
    [{ shift: null }, { shift: 'all', search: '' }],
  ]

  it.each(addressReadings)('reads %j from the address as %j', (query, expected) => {
    expect(technicianFiltersFromQuery(query)).toEqual(expected)
  })

  it('reads at most 100 characters of search from the address', () => {
    const filters = technicianFiltersFromQuery({ search: 'a'.repeat(150) })

    expect(filters.search).toHaveLength(technicianSearchMaximumLength)
    expect(technicianSearchMaximumLength).toBe(100)
  })

  const addressWritings: readonly (readonly [TechnicianFilters, LocationQueryRaw, boolean])[] = [
    [{ shift: 'all', search: '' }, {}, false],
    [{ shift: 'all', search: '   ' }, {}, false],
    [{ shift: 'on-shift', search: '' }, { shift: 'on-shift' }, true],
    [{ shift: 'off-shift', search: 'sam' }, { shift: 'off-shift', search: 'sam' }, true],
    [{ shift: 'all', search: 'tom' }, { search: 'tom' }, true],
  ]

  it.each(addressWritings)('writes %j to the address as %j', (filters, query, active) => {
    expect(queryFromTechnicianFilters(filters)).toEqual(query)
    expect(hasActiveTechnicianFilters(filters)).toBe(active)
  })
})

describe('the technician filters in the address', () => {
  let captured: TechnicianFilterControls | null = null

  const TechniciansProbe = defineComponent({
    setup() {
      captured = useTechnicianFilters()
      return () => h('div')
    },
  })

  async function probe(
    path: string,
  ): Promise<{ readonly router: Router; readonly controls: TechnicianFilterControls }> {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/technicians', name: 'technicians', component: TechniciansProbe }],
    })
    await router.push(path)
    mount(TechniciansProbe, { global: { plugins: [router] } })
    if (captured === null) {
      throw new Error('The probe did not set up its filters')
    }
    return { router, controls: captured }
  }

  it('reads the filters from the address and writes changes back to the technicians route', async () => {
    const { router, controls } = await probe('/technicians?shift=off-shift&search=sam')

    expect(controls.filters.value).toEqual({ shift: 'off-shift', search: 'sam' })

    await controls.update({ shift: 'all' })
    expect(router.currentRoute.value.fullPath).toBe('/technicians?search=sam')

    await controls.update({ search: '' }, { replace: true })
    expect(router.currentRoute.value.fullPath).toBe('/technicians')

    await controls.update({ shift: 'on-shift', search: 'electrical' })
    await controls.clear()
    expect(router.currentRoute.value.fullPath).toBe('/technicians')
    expect(controls.filters.value).toEqual(noTechnicianFilters)
  })
})

describe('the technicians store', () => {
  it('loads the technicians in the API order, then counts them', async () => {
    const reply = deferred()
    stubFetch({ [techniciansRoute]: reply.reply })
    const store = useTechniciansStore()

    const loading = store.load()
    await reply.requested
    expect(store.status).toBe('loading')
    expect(store.counts).toBeNull()

    reply.resolve(
      new Response(JSON.stringify({ items: [...demoTechnicians].reverse() }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    await loading

    expect(store.status).toBe('ready')
    expect(names(store.items ?? [])).toEqual(names([...demoTechnicians].reverse()))
    expect(store.counts).toEqual({ all: 6, onShift: 5, offShift: 1 })
    expect(store.onShift).toEqual({ onShift: 5, total: 6 })
  })

  it('reads technicians that match the contract', () => {
    expect(contractProblems('GetTechniciansResponse', { items: demoTechnicians })).toEqual([])
  })

  it('keeps a load failure with its reference, and Try again loads again', async () => {
    const stub = stubFetch({
      [techniciansRoute]: [problem(500, 'server.error'), json({ items: demoTechnicians })],
    })
    const store = useTechniciansStore()

    await store.load()
    expect(store.status).toBe('error')
    expect(store.error?.correlationId).toBe(
      stub.requestsTo(techniciansRoute)[0]?.headers.get('X-Correlation-ID'),
    )

    await store.load()
    expect(store.status).toBe('ready')
    expect(store.error).toBeNull()
  })

  it('refreshes in the background only once loaded, without a loading state', async () => {
    const changed = [{ ...aishaTechnician, openJobCount: 4 }, lenaTechnician]
    const stub = stubFetch({
      [techniciansRoute]: [json({ items: demoTechnicians }), json({ items: changed })],
    })
    const store = useTechniciansStore()

    expect(await store.refresh()).toBe(false)
    expect(stub.requests).toHaveLength(0)

    await store.load()
    const refreshing = store.refresh()
    expect(store.status).toBe('ready')
    expect(await refreshing).toBe(true)
    expect(store.items).toEqual(changed)
  })

  it('skips a background refresh while the first load has failed', async () => {
    const stub = stubFetch({ [techniciansRoute]: problem(500, 'server.error') })
    const store = useTechniciansStore()
    await store.load()

    expect(await store.refresh()).toBe(false)
    expect(stub.requestsTo(techniciansRoute)).toHaveLength(1)
    expect(store.status).toBe('error')
  })

  it('drops a background refresh that a newer one replaced', async () => {
    const older = deferred()
    const newer = [marcoTechnician, priyaTechnician]
    stubFetch({
      [techniciansRoute]: [json({ items: demoTechnicians }), older.reply, json({ items: newer })],
    })
    const store = useTechniciansStore()
    await store.load()

    const first = store.refresh()
    await older.requested
    const second = store.refresh()
    expect(await second).toBe(true)
    older.resolve(
      new Response(JSON.stringify({ items: [tomTechnician] }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )

    expect(await first).toBe(false)
    expect(store.items).toEqual(newer)
  })

  it('rejects a failed background refresh with its error and keeps the list', async () => {
    stubFetch({
      [techniciansRoute]: [json({ items: demoTechnicians }), problem(503, 'server.unavailable')],
    })
    const store = useTechniciansStore()
    await store.load()

    await expect(store.refresh()).rejects.toMatchObject({ status: 503 })
    expect(store.items).toEqual(demoTechnicians)
    expect(store.status).toBe('ready')
  })
})

describe('live technicians', () => {
  async function liveTechnicians(routes: Parameters<typeof signedIn>[0] = {}) {
    vi.useFakeTimers({ now: demoToday })
    const stub = await signedIn({
      [techniciansRoute]: json({ items: demoTechnicians }),
      [jobRoute(generatorJobId)]: json(generatorJob()),
      ...routes,
    })
    const realtime = useRealtimeStore()
    realtime.start()
    scope = effectScope()
    const data = scope.run(() => useTechniciansData(techniciansCopy))
    await flush()
    return { stub, realtime, data, socket: lastFakeSocket() }
  }

  it('loads once, marks the pill time, and refetches on every connect', async () => {
    const { stub, realtime, socket } = await liveTechnicians()

    expect(stub.requestsTo(techniciansRoute)).toHaveLength(1)
    expect(realtime.refreshedAt).toEqual(demoToday)

    await socket.simulateConnect()
    await flush()
    expect(stub.requestsTo(techniciansRoute)).toHaveLength(2)

    socket.simulateDisconnect('transport close')
    await vi.advanceTimersByTimeAsync(2_000)
    await socket.simulateConnect()
    await flush()
    expect(stub.requestsTo(techniciansRoute)).toHaveLength(3)
  })

  it('refetches once, debounced, for events within 300 ms', async () => {
    const { stub, socket } = await liveTechnicians()
    await socket.simulateConnect()
    await flush()

    for (let index = 0; index < 3; index += 1) {
      socket.emitServerEvent('job.status-changed', jobEvent())
      await vi.advanceTimersByTimeAsync(100)
    }
    expect(stub.requestsTo(techniciansRoute)).toHaveLength(2)

    await vi.advanceTimersByTimeAsync(listDebounceMilliseconds)
    await flush()
    expect(stub.requestsTo(techniciansRoute)).toHaveLength(3)
  })

  it("shows the page's Refresh failed copy with the failed call's reference when the connect refetch fails", async () => {
    const { stub, realtime, socket } = await liveTechnicians({
      [techniciansRoute]: [json({ items: demoTechnicians }), problem(500, 'server.error')],
    })

    await socket.simulateConnect()
    await flush()

    expect(realtime.refreshFailure).toEqual({
      title: 'Technicians could not be refreshed.',
      message: `Showing technicians from 10:42${noBreakSpace}AM.`,
      reference: stub.requestsTo(techniciansRoute)[1]?.headers.get('X-Correlation-ID'),
    })
    expect(useTechniciansStore().items).toEqual(demoTechnicians)
  })

  it('retries a failed first load and marks the pill time when it succeeds', async () => {
    const { realtime, data } = await liveTechnicians({
      [techniciansRoute]: [problem(500, 'server.error'), json({ items: demoTechnicians })],
    })
    expect(useTechniciansStore().status).toBe('error')
    expect(realtime.refreshedAt).toBeNull()

    await data?.retry()

    expect(useTechniciansStore().status).toBe('ready')
    expect(realtime.refreshedAt).toEqual(demoToday)
  })

  it('stops refetching and clears the list when its page closes', async () => {
    const { stub, socket } = await liveTechnicians()
    await socket.simulateConnect()
    await flush()

    scope?.stop()
    scope = null
    socket.emitServerEvent('job.status-changed', jobEvent())
    await vi.advanceTimersByTimeAsync(listDebounceMilliseconds)
    await flush()

    expect(stub.requestsTo(techniciansRoute)).toHaveLength(2)
    expect(useTechniciansStore().items).toBeNull()
    expect(useTechniciansStore().status).toBe('idle')
  })
})

describe('the technicians page', () => {
  let page: VueWrapper | null = null

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(demoToday)
  })

  afterEach(() => {
    page?.unmount()
    page = null
  })

  async function openPage(
    path = '/technicians',
    routes: RouteTable = {},
  ): Promise<{ readonly wrapper: VueWrapper; readonly router: Router; readonly stub: FetchStub }> {
    const stub = stubFetch({
      [refreshRoute]: json(sessionResponse()),
      [ticketRoute]: ticketReply(),
      [techniciansRoute]: json({ items: demoTechnicians }),
      ...routes,
    })
    const router = createAppRouter(createMemoryHistory())
    installNavigationGuards(router)
    await router.push(path)
    const wrapper = mount(App, { global: { plugins: [router] }, attachTo: document.body })
    page = wrapper
    await flushPromises()
    return { wrapper, router, stub }
  }

  function words(node: DOMWrapper<Element>): string {
    const copy = node.element.cloneNode(true) as Element
    copy.querySelectorAll('.visually-hidden').forEach((hidden) => {
      hidden.remove()
    })
    const walker = document.createTreeWalker(copy, NodeFilter.SHOW_TEXT)
    const parts: string[] = []
    for (let current = walker.nextNode(); current !== null; current = walker.nextNode()) {
      parts.push(current.textContent ?? '')
    }
    return parts.join(' ').split(noBreakSpace).join(' ').replace(/\s+/g, ' ').trim()
  }

  function tableNames(wrapper: VueWrapper): string[] {
    return wrapper.findAll('tbody .technicians-table__name').map((name) => name.text())
  }

  function listNames(wrapper: VueWrapper): string[] {
    return wrapper
      .findAll('ul[aria-label="Technicians"] .technicians-table__name')
      .map((name) => name.text())
  }

  function chips(wrapper: VueWrapper): (string | undefined)[] {
    return wrapper
      .findAll('[aria-label="Filter by shift"] button')
      .map((chip) => chip.attributes('aria-label') ?? chip.text())
  }

  function hiddenStatus(wrapper: VueWrapper): string {
    return wrapper.find('main .app-shell__content > p.visually-hidden[role="status"]').text()
  }

  function search(wrapper: VueWrapper): DOMWrapper<HTMLInputElement> {
    return wrapper.find<HTMLInputElement>('input[aria-label="Search technicians"]')
  }

  it('shows the header figure, the counted chips and the table in the API order', async () => {
    const { wrapper } = await openPage()

    expect(document.title).toBe('Technicians · FieldService Admin')
    expect(wrapper.find('h1').text()).toBe('Technicians')
    expect(wrapper.find('.page-header__subline').text()).toBe('5 of 6 on shift')
    expect(wrapper.find('form[role="search"]').attributes('aria-label')).toBe('Filter technicians')
    expect(chips(wrapper)).toEqual(['All, 6', 'On shift, 5', 'Off shift, 1'])
    expect(wrapper.find('[aria-pressed="true"]').attributes('aria-label')).toBe('All, 6')
    expect(wrapper.findAll('[aria-label="Filter by shift"] svg')).toHaveLength(0)
    expect(wrapper.find('[role="region"]').attributes('aria-label')).toBe('Technicians')
    expect(wrapper.find('table').classes()).toContain('data-table__table--min-900')
    expect(wrapper.findAll('th').map((cell) => cell.text())).toEqual([
      'Technician',
      'Trade',
      'Shift',
      'Open jobs',
      'Current or next job',
      'Phone',
    ])
    expect(
      wrapper.findAll('tbody tr').map((row) => row.findAll('td').map((cell) => words(cell))),
    ).toEqual([
      [
        'AK Aisha Karim aisha.karim @fieldservice.example',
        'Electrical',
        'On shift',
        '3',
        'Next: JOB-1055 at 1:00 PM',
        '(555) 014-7810',
      ],
      [
        'LP Lena Park lena.park @fieldservice.example',
        'Fire safety',
        'On shift',
        '1',
        'Next: JOB-1045 at 11:30 AM',
        '(555) 014-5521',
      ],
      [
        'MR Marco Reyes marco.reyes @fieldservice.example',
        'HVAC',
        'On shift',
        '3',
        'JOB-1042 In progress',
        '(555) 014-2231',
      ],
      [
        'PN Priya Natarajan priya.natarajan @fieldservice.example',
        'Electrical',
        'On shift',
        '2',
        'JOB-1049 In progress',
        '(555) 014-6604',
      ],
      [
        'SO Samuel Osei samuel.osei @fieldservice.example',
        'HVAC',
        'Off shift',
        '0',
        'None',
        '(555) 014-9148',
      ],
      [
        'TB Tom Becker tom.becker @fieldservice.example',
        'Plumbing',
        'On shift',
        '4',
        'JOB-1044 On hold',
        '(555) 014-3392',
      ],
    ])
    const email = wrapper.find('tbody .technicians-table__email')
    expect(Array.from(email.element.children, (child) => child.tagName.toLowerCase())).toEqual([
      'span',
      'wbr',
      'span',
    ])
    expect(email.findAll('.technicians-table__email-part').map((part) => part.text())).toEqual([
      'aisha.karim',
      '@fieldservice.example',
    ])
    expect(hiddenStatus(wrapper)).toBe('')
  })

  it('builds the people list from the same rows, with the phone as a tel: link named by person and number', async () => {
    const { wrapper } = await openPage()
    const rows = wrapper.findAll('ul[aria-label="Technicians"] > li')
    const calls = wrapper.findAll('a.technicians-table__call')

    expect(listNames(wrapper)).toEqual(tableNames(wrapper))
    expect(words(rows[0] as DOMWrapper<Element>)).toBe(
      'AK Aisha Karim aisha.karim @fieldservice.example On shift Electrical · 3 open jobs Next: JOB-1055 at 1:00 PM (555) 014-7810',
    )
    expect(words(rows[1] as DOMWrapper<Element>)).toContain('Fire safety · 1 open job ')
    expect(
      rows.map(
        (row) => row.find('.technicians-table__count > [aria-hidden="true"]').element.textContent,
      ),
    ).toEqual(rows.map(() => '\u{00a0}\u{00b7} '))
    expect(calls.map((call) => [call.attributes('aria-label'), call.attributes('href')])).toEqual([
      ['Call Aisha Karim, (555) 014-7810', 'tel:5550147810'],
      ['Call Lena Park, (555) 014-5521', 'tel:5550145521'],
      ['Call Marco Reyes, (555) 014-2231', 'tel:5550142231'],
      ['Call Priya Natarajan, (555) 014-6604', 'tel:5550146604'],
      ['Call Samuel Osei, (555) 014-9148', 'tel:5550149148'],
      ['Call Tom Becker, (555) 014-3392', 'tel:5550143392'],
    ])
    expect(wrapper.find('table a[href^="tel:"]').exists()).toBe(false)
  })

  it('filters as the user types, keeps the counts, then writes the address and announces the rows after a pause', async () => {
    vi.useFakeTimers({ toFake: ['Date', 'setTimeout', 'clearTimeout'], now: demoToday })
    const { wrapper, router } = await openPage()

    await search(wrapper).setValue('014-78')

    expect(tableNames(wrapper)).toEqual(['Aisha Karim'])
    expect(chips(wrapper)).toEqual(['All, 6', 'On shift, 5', 'Off shift, 1'])
    expect(router.currentRoute.value.fullPath).toBe('/technicians')
    expect(hiddenStatus(wrapper)).toBe('')

    await vi.advanceTimersByTimeAsync(300)
    expect(router.currentRoute.value.fullPath).toBe('/technicians?search=014-78')
    expect(hiddenStatus(wrapper)).toBe('')

    await vi.advanceTimersByTimeAsync(200)
    await flushPromises()
    expect(hiddenStatus(wrapper)).toBe('Showing 1 of 6 technicians')

    await search(wrapper).setValue('plumbing')
    await vi.advanceTimersByTimeAsync(500)
    await flushPromises()
    expect(tableNames(wrapper)).toEqual(['Tom Becker'])
    expect(hiddenStatus(wrapper)).toBe('Showing 1 of 6 technicians')
    expect(router.currentRoute.value.fullPath).toBe('/technicians?search=plumbing')
  })

  it('reads Off shift from the address as every technician not active and on shift, and counts active technicians in the subline', async () => {
    const inactive = technician({
      id: '01a0f6b1-3e00-7000-8000-000000000007',
      displayName: 'Nadia Flores',
      email: 'nadia.flores@fieldservice.example',
      isActive: false,
      isOnShift: true,
      availableForAssignment: false,
      currentJob: null,
      nextJob: null,
    })
    const { wrapper } = await openPage('/technicians?shift=off-shift', {
      [techniciansRoute]: json({ items: [...demoTechnicians, inactive] }),
    })

    expect(tableNames(wrapper)).toEqual(['Samuel Osei', 'Nadia Flores'])
    expect(chips(wrapper)).toEqual(['All, 7', 'On shift, 5', 'Off shift, 2'])
    expect(wrapper.find('[aria-pressed="true"]').attributes('aria-label')).toBe('Off shift, 2')
    expect(wrapper.find('.page-header__subline').text()).toBe('5 of 6 on shift')
  })

  it('shows Inactive in the Off shift look for an inactive technician, in the table and the people list, on shift or not', async () => {
    const inactiveSamuel = { ...samuelTechnician, isActive: false, isOnShift: false }
    const { wrapper } = await openPage('/technicians', {
      [techniciansRoute]: json({
        items: [
          ...demoTechnicians.map((item) =>
            item.id === samuelTechnician.id ? inactiveSamuel : item,
          ),
          inactiveOnShift,
        ],
      }),
    })
    const tableShifts = wrapper
      .findAll('tbody tr')
      .map((row) => [row.find('.technicians-table__name').text(), row.findAll('td')[2]?.text()])
    const listShifts = wrapper
      .findAll('ul[aria-label="Technicians"] > li')
      .map((row) => [
        row.find('.technicians-table__name').text(),
        row.find('.technicians-table__shift').text(),
      ])
    const expected = [
      ['Aisha Karim', 'On shift'],
      ['Lena Park', 'On shift'],
      ['Marco Reyes', 'On shift'],
      ['Priya Natarajan', 'On shift'],
      ['Samuel Osei', 'Inactive'],
      ['Tom Becker', 'On shift'],
      ['Nadia Flores', 'Inactive'],
    ]

    expect(tableShifts).toEqual(expected)
    expect(listShifts).toEqual(expected)
    const inactiveChips = wrapper
      .findAll('.status-chip--shift')
      .filter((chip) => chip.text() === 'Inactive')
    expect(inactiveChips).toHaveLength(4)
    expect(inactiveChips.every((chip) => chip.classes('status-chip--off-shift'))).toBe(true)
  })

  it('keeps an inactive technician under Off shift, with the counts and the subline unchanged', async () => {
    const inactiveSamuel = { ...samuelTechnician, isActive: false }
    const { wrapper } = await openPage('/technicians?shift=off-shift', {
      [techniciansRoute]: json({
        items: demoTechnicians.map((item) =>
          item.id === samuelTechnician.id ? inactiveSamuel : item,
        ),
      }),
    })

    expect(tableNames(wrapper)).toEqual(['Samuel Osei'])
    expect(listNames(wrapper)).toEqual(['Samuel Osei'])
    expect(wrapper.find('tbody .status-chip--shift').text()).toBe('Inactive')
    expect(chips(wrapper)).toEqual(['All, 6', 'On shift, 5', 'Off shift, 1'])
    expect(wrapper.find('[aria-pressed="true"]').attributes('aria-label')).toBe('Off shift, 1')
    expect(wrapper.find('.page-header__subline').text()).toBe('5 of 5 on shift')

    await wrapper.find('button[aria-label="On shift, 5"]').trigger('click')
    await flushPromises()
    expect(tableNames(wrapper)).not.toContain('Samuel Osei')
  })

  it('switches the shift filter with the chips and keeps the search and the filter in the address', async () => {
    const { wrapper, router } = await openPage('/technicians?shift=on-shift&search=lena')

    expect(search(wrapper).element.value).toBe('lena')
    expect(tableNames(wrapper)).toEqual(['Lena Park'])

    await wrapper.find('button[aria-label="Off shift, 1"]').trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.query).toEqual({ shift: 'off-shift', search: 'lena' })
    expect(tableNames(wrapper)).toEqual([])
  })

  it('offers Clear filters when nothing matches, clearing the address and moving focus to the search', async () => {
    const { wrapper, router } = await openPage('/technicians?shift=off-shift&search=lena')

    const headings = wrapper.findAll('h2').map((heading) => heading.text())
    expect(headings).toContain('No technicians match these filters')
    expect(wrapper.find('.empty-state__body').text()).toBe(
      'Change the search or the shift filter, or clear them to see every technician.',
    )

    const clear = wrapper.findAll('button').find((button) => button.text() === 'Clear filters')
    await clear?.trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.fullPath).toBe('/technicians')
    expect(search(wrapper).element.value).toBe('')
    expect(document.activeElement).toBe(search(wrapper).element)
    expect(tableNames(wrapper)).toHaveLength(6)
  })

  it('shows No technicians yet, without a button, when there are none', async () => {
    const { wrapper } = await openPage('/technicians', {
      [techniciansRoute]: json({ items: [] }),
    })

    expect(wrapper.findAll('h2').map((heading) => heading.text())).toEqual([
      'No technicians yet',
      'No technicians yet',
    ])
    expect(wrapper.find('.empty-state button').exists()).toBe(false)
    expect(wrapper.find('.page-header__subline').text()).toBe('0 of 0 on shift')
  })

  it('disables the chips without counts and the search while loading, with one hidden status', async () => {
    const reply = deferred()
    const { wrapper } = await openPage('/technicians', { [techniciansRoute]: reply.reply })

    expect(chips(wrapper)).toEqual(['All', 'On shift', 'Off shift'])
    expect(
      wrapper
        .findAll('[aria-label="Filter by shift"] button')
        .every((chip) => chip.attributes('disabled') !== undefined),
    ).toBe(true)
    expect(search(wrapper).attributes('disabled')).toBeDefined()
    expect(wrapper.find('.page-header__subline').exists()).toBe(false)
    expect(wrapper.find('table').attributes('aria-busy')).toBe('true')
    expect(wrapper.findAll('tbody tr[aria-hidden="true"]')).toHaveLength(5)
    expect(
      wrapper.findAll('.technicians-table__skeletons .technicians-table__person-row'),
    ).toHaveLength(3)
    expect(
      wrapper
        .findAll('main [role="status"]')
        .map((status) => status.text())
        .filter((text) => text.length > 0),
    ).toEqual(['Loading technicians'])

    reply.resolve(new Response(JSON.stringify({ items: demoTechnicians }), { status: 200 }))
    await flushPromises()
    expect(chips(wrapper)).toEqual(['All, 6', 'On shift, 5', 'Off shift, 1'])
  })

  it('shows the load failure in place of the table with the filters disabled, and focuses the table once Try again loads', async () => {
    const { wrapper } = await openPage('/technicians', {
      [techniciansRoute]: [problem(500, 'server.error'), json({ items: demoTechnicians })],
    })
    const banner = wrapper.find('main .banner--error')

    expect(words(banner.find('.banner__text'))).toBe(
      'Technicians could not be loaded. Check your connection, then try again.',
    )
    expect(banner.attributes('role')).toBe('status')
    expect(banner.find('.banner__reference').text()).toMatch(/^Reference [0-9a-f-]{36}$/)
    expect(wrapper.find('table').exists()).toBe(false)
    expect(search(wrapper).attributes('disabled')).toBeDefined()
    expect(wrapper.find('.page-header__subline').exists()).toBe(false)

    await banner.find('button').trigger('click')
    await flushPromises()

    expect(tableNames(wrapper)).toHaveLength(6)
    expect(document.activeElement?.getAttribute('aria-label')).toBe('Technicians')
  })

  it('switches to the people list below the table minimum, with the email kept whole on each side of "@"', () => {
    const table = 'shared/ui/DataTable.vue'
    const technicians = 'features/technicians/components/TechniciansTable.vue'

    expect(
      ruleDeclarations(
        table,
        '.data-table--list-below-900 > .data-table__scroll',
        '@container (width < 900px)',
      ),
    ).toEqual({ display: 'none' })
    expect(
      ruleDeclarations(
        table,
        '.data-table--list-below-900 > .data-table__list',
        '@container (width < 900px)',
      ),
    ).toEqual({ display: 'block' })
    expect(ruleDeclarations(table, '.data-table__list')).toEqual({ display: 'none' })
    expect(ruleDeclarations(technicians, '.technicians-table__email-part')).toEqual({
      'white-space': 'nowrap',
    })
    expect(ruleDeclarations(technicians, '.technicians-table__single')).toEqual({
      'white-space': 'nowrap',
    })
    expect(ruleDeclarations(technicians, '.technicians-table__details')).toMatchObject({
      'padding-left': '44px',
    })
  })

  it('lays the first line of a person out as a grid whose email line runs under the shift chip', () => {
    const technicians = 'features/technicians/components/TechniciansTable.vue'
    const firstLine = '.technicians-table__first-line'
    const both = (...selectors: string[]): string =>
      selectors.map((selector) => `${firstLine} ${selector}`).join(',\n')

    expect(ruleDeclarations(technicians, firstLine)).toEqual({
      display: 'grid',
      'grid-template-columns': '32px minmax(0, 1fr) auto',
      'column-gap': 'var(--spacing-fs-12)',
      'row-gap': 'var(--spacing-fs-2)',
      'align-items': 'start',
    })
    expect(ruleDeclarations(technicians, `${firstLine} > :first-child`)).toEqual({
      'grid-row': '1 / span 2',
    })
    expect(
      ruleDeclarations(technicians, `${firstLine} > .technicians-table__identity--grow`),
    ).toEqual({ display: 'contents' })
    expect(
      ruleDeclarations(
        technicians,
        both('.technicians-table__name', '.technicians-table__block--name'),
      ),
    ).toEqual({ 'grid-column': '2', 'grid-row': '1' })
    expect(
      ruleDeclarations(
        technicians,
        both('.technicians-table__shift', '.technicians-table__block--chip'),
      ),
    ).toEqual({ 'grid-column': '3', 'grid-row': '1' })
    expect(
      ruleDeclarations(
        technicians,
        both('.technicians-table__email', '.technicians-table__block--email'),
      ),
    ).toEqual({ 'grid-column': '2 / -1', 'grid-row': '2' })
    expect(ruleDeclarations(technicians, '.technicians-table__identity')).toMatchObject({
      display: 'flex',
      'flex-direction': 'column',
    })
  })

  it('puts the avatar first in each person’s first line, then the name over the email, then the chip, in the list and its skeleton rows', async () => {
    const shape = (line: DOMWrapper<Element>): string[][] =>
      Array.from(line.element.children, (child) =>
        child.classList.contains('technicians-table__identity--grow')
          ? Array.from(child.children, (part) => part.classList[0] ?? '')
          : [child.classList[0] ?? ''],
      )
    const reply = deferred()
    const { wrapper } = await openPage('/technicians', { [techniciansRoute]: reply.reply })

    const skeleton = wrapper.find('.technicians-table__skeletons .technicians-table__first-line')
    expect(shape(skeleton)).toEqual([
      ['technicians-table__block'],
      ['technicians-table__block', 'technicians-table__block'],
      ['technicians-table__block'],
    ])
    expect(Array.from(skeleton.element.children, (child) => child.classList[1])).toEqual([
      'technicians-table__block--avatar',
      'technicians-table__identity--grow',
      'technicians-table__block--chip',
    ])
    expect(
      Array.from(
        skeleton.find('.technicians-table__identity--grow').element.children,
        (child) => child.classList[1],
      ),
    ).toEqual(['technicians-table__block--name', 'technicians-table__block--email'])

    reply.resolve(new Response(JSON.stringify({ items: demoTechnicians }), { status: 200 }))
    await flushPromises()

    const line = wrapper.find('ul[aria-label="Technicians"] .technicians-table__first-line')
    expect(shape(line)).toEqual([
      ['avatar'],
      ['technicians-table__name', 'technicians-table__email'],
      ['status-chip'],
    ])
    expect(line.element.lastElementChild?.classList.contains('technicians-table__shift')).toBe(true)
  })
})
