import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, type Router } from 'vue-router'

import App from '@/app/App.vue'
import { createAppRouter, installNavigationGuards } from '@/app/router'
import { ruleDeclarations as declarationsOf } from '@/app/styles/__tests__/stylesheet-source'
import { sessionResponse } from '@/features/auth/__tests__/session-fixtures'
import {
  deferred,
  json,
  networkFailure,
  problem,
  stubFetch,
  type FetchStub,
  type RecordedRequest,
  type RouteTable,
} from '@/shared/testing/fetch-stub'

import { noJobFilters } from '../composables/use-job-filters'
import { jobsPageSize, useJobsStore } from '../jobs-store'
import {
  assignedInProgressJob,
  demoStatusCounts,
  demoSummaries,
  demoTechnicians,
  demoToday,
  jobDetails,
  jobList,
  jobSummary,
  marco,
  onDemoDay,
  onHoldJob,
  priya,
} from './job-fixtures'

const listRoute = 'GET /api/jobs'

const techniciansRoute = 'GET /api/technicians'

const reference = 'dd85f06c-7606-4dfc-a702-fa39647047d8'

interface OpenApp {
  readonly wrapper: VueWrapper
  readonly router: Router
  readonly stub: FetchStub
}

let mounted: VueWrapper | null = null

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] })
  vi.setSystemTime(demoToday)
})

afterEach(() => {
  mounted?.unmount()
  mounted = null
})

function queryOf(request: RecordedRequest | undefined): Record<string, string> {
  return Object.fromEntries(new URL(request?.url ?? 'http://localhost/').searchParams)
}

function lastQuery(stub: FetchStub): Record<string, string> {
  const requests = stub.requestsTo(listRoute)
  return queryOf(requests[requests.length - 1])
}

async function openApp(path: string, routeTable: RouteTable): Promise<OpenApp> {
  const stub = stubFetch({
    'POST /api/auth/refresh': json(sessionResponse()),
    [techniciansRoute]: json({ items: demoTechnicians }),
    ...routeTable,
  })
  const router = createAppRouter(createMemoryHistory())
  installNavigationGuards(router)
  await router.push(path)
  const wrapper = mount(App, { global: { plugins: [router] }, attachTo: document.body })
  mounted = wrapper
  await flushPromises()
  return { wrapper, router, stub }
}

function firstPage(): ReturnType<typeof json> {
  return json(jobList(demoSummaries(12)))
}

function chipNames(wrapper: VueWrapper): (string | undefined)[] {
  return wrapper
    .findAll('[aria-label="Filter by status"] button')
    .map((chip) => chip.attributes('aria-label'))
}

function pressedChip(wrapper: VueWrapper): string | undefined {
  return wrapper
    .find('[aria-label="Filter by status"] button[aria-pressed="true"]')
    .attributes('aria-label')
}

function technicianFilter(wrapper: VueWrapper): HTMLSelectElement {
  return wrapper.find<HTMLSelectElement>('select[aria-label="Technician"]').element
}

function optionTexts(select: HTMLSelectElement): string[] {
  return Array.from(select.options, (option) => option.text.trim())
}

describe('the jobs store', () => {
  it('asks for 12 jobs a page and sends only the filters that are set', async () => {
    const stub = stubFetch({ [listRoute]: json(jobList([])) })
    const store = useJobsStore()

    await store.loadJobs(noJobFilters)
    await store.loadJobs({
      search: '  elm street  ',
      status: 'OnHold',
      technicianId: priya.id,
      priority: 'High',
      page: 2,
    })

    expect(jobsPageSize).toBe(12)
    expect(queryOf(stub.requests[0])).toEqual({ page: '1', pageSize: '12' })
    expect(queryOf(stub.requests[1])).toEqual({
      search: 'elm street',
      status: 'OnHold',
      technicianId: priya.id,
      priority: 'High',
      page: '2',
      pageSize: '12',
    })
  })

  it('keeps the six status counts and sums them for All', async () => {
    stubFetch({ [listRoute]: json(jobList([])) })
    const store = useJobsStore()

    await store.loadJobs(noJobFilters)

    expect(store.statusCounts).toEqual(demoStatusCounts)
    expect(store.statusTotal).toBe(20)
  })

  it('ignores a page that arrives after a newer request', async () => {
    const slow = deferred()
    stubFetch({ [listRoute]: [slow.reply, json(jobList([jobSummary()], { totalCount: 1 }))] })
    const store = useJobsStore()

    const first = store.loadJobs(noJobFilters)
    await slow.requested
    await store.loadJobs({ ...noJobFilters, status: 'Unassigned' })
    slow.resolve(new Response(JSON.stringify(jobList(demoSummaries(12))), { status: 200 }))
    await first

    expect(store.list?.totalCount).toBe(1)
    expect(store.listFilters?.status).toBe('Unassigned')
  })

  it('keeps the API error of a failed page with its reference', async () => {
    stubFetch({ [listRoute]: problem(500, 'server.error', { correlationId: reference }) })
    const store = useJobsStore()

    await store.loadJobs(noJobFilters)

    expect(store.listStatus).toBe('error')
    expect(store.listError?.correlationId).toBe(reference)
  })

  it('opens a job, and tells a missing job from a failure', async () => {
    const job = assignedInProgressJob()
    stubFetch({
      [`GET /api/jobs/${job.id}`]: json(job),
      'GET /api/jobs/01a15488-0000-7000-8000-000000000000': problem(404, 'job.not-found'),
      'GET /api/jobs/01a15488-0000-7000-8000-000000000001': problem(500, 'server.error'),
    })
    const store = useJobsStore()

    await store.openJob(job.id)
    expect(store.jobStatus).toBe('ready')
    expect(store.job?.number).toBe(1042)

    await store.openJob('01a15488-0000-7000-8000-000000000000')
    expect(store.jobStatus).toBe('not-found')
    expect(store.job).toBeNull()

    await store.openJob('01a15488-0000-7000-8000-000000000001')
    expect(store.jobStatus).toBe('error')
  })

  it('keeps a created job so its page opens without another request', async () => {
    const created = jobDetails({ id: '01a1549f-79e0-7c56-bc65-86a395b0ccdc', number: 1056 })
    const stub = stubFetch({ 'POST /api/jobs': json(created, 201) })
    const store = useJobsStore()

    await store.createJob({
      title: created.title,
      description: created.description,
      priority: 'Normal',
      customerName: created.customerName,
      siteAddress: created.siteAddress,
      scheduledStart: created.scheduledStart,
      scheduledEnd: created.scheduledEnd,
      checklistTemplateId: created.checklistTemplate.id,
    })
    await store.openJob(created.id)

    expect(store.job?.number).toBe(1056)
    expect(stub.requests).toHaveLength(1)
  })
})

describe('the jobs page', () => {
  it('shows the jobs, the total and the status counts from the API', async () => {
    const { wrapper } = await openApp('/jobs', { [listRoute]: firstPage() })

    expect(wrapper.find('h1').text()).toBe('Jobs')
    expect(wrapper.find('.page-header__subline').text()).toBe('20 jobs')
    expect(chipNames(wrapper)).toEqual([
      'All, 20',
      'Unassigned, 4',
      'Assigned, 8',
      'In progress, 2',
      'On hold, 3',
      'Completed, 2',
      'Cancelled, 1',
    ])
    expect(pressedChip(wrapper)).toBe('All, 20')
    expect(wrapper.findAll('tbody tr')).toHaveLength(12)
    expect(wrapper.find('.pagination__summary').text()).toBe('Showing 1–12 of 20 jobs')
    expect(wrapper.find('p[role="status"].visually-hidden').text()).toBe('Showing 1–12 of 20 jobs')
  })

  it('shows each row as job, customer and site, priority, schedule, technician, status and update', async () => {
    const assigned = jobSummary({
      id: '01a15415-3b40-79a4-859f-2cf627f64d02',
      number: 1042,
      title: 'Rooftop HVAC unit not cooling',
      status: 'InProgress',
      customerName: 'Brightwater Medical Clinic',
      siteAddress: '214 Harbor Road, Suite 3',
      scheduledStart: onDemoDay(0, 9, 0),
      scheduledEnd: onDemoDay(0, 11, 0),
      assignedTechnician: marco,
      updatedAt: onDemoDay(0, 9, 50),
    })
    const later = jobSummary({
      id: '01a15062-9380-7aa7-8e25-c4fbbaea52ab',
      number: 1054,
      title: 'Leaking rooftop drain',
      priority: 'Urgent',
      scheduledStart: onDemoDay(3, 10, 0),
      scheduledEnd: onDemoDay(3, 12, 0),
      updatedAt: onDemoDay(-1, 11, 5),
    })
    const { wrapper } = await openApp('/jobs', {
      [listRoute]: json(jobList([jobSummary(), assigned, later], { totalCount: 3 })),
    })
    const rows = wrapper.findAll('tbody tr')
    const cells = (index: number): string[] =>
      rows[index]?.findAll('td').map((cell) => cell.text()) ?? []

    expect(wrapper.findAll('th').map((header) => header.text())).toEqual([
      'Job',
      'Customer and site',
      'Priority',
      'Scheduled',
      'Technician',
      'Status',
      'Updated',
    ])
    expect(cells(0)).toEqual([
      'JOB-1051Replace faulty breaker panel',
      'Pinecrest Public Library300 Pinecrest Road',
      'High',
      'Today3:30–5:00 PM',
      'Unassigned',
      'Unassigned',
      '10:20 AM',
    ])
    expect(cells(1)).toEqual([
      'JOB-1042Rooftop HVAC unit not cooling',
      'Brightwater Medical Clinic214 Harbor Road, Suite 3',
      'High',
      'Today9:00–11:00 AM',
      'MRMarco Reyes',
      'In progress',
      '9:50 AM',
    ])
    expect(cells(2)[2]).toBe('Urgent')
    expect(cells(2)[3]).toBe('Thu 22 Oct10:00 AM–12:00 PM')
    expect(cells(2)[6]).toBe('Yesterday')
    expect(rows[1]?.find('a').attributes('href')).toBe('/jobs/01a15415-3b40-79a4-859f-2cf627f64d02')
    expect(rows[1]?.find('time').attributes('datetime')).toBe(onDemoDay(0, 9, 50))
  })

  it('reads its filters from the address and sends them to the API', async () => {
    const { wrapper, stub } = await openApp('/jobs?status=OnHold&priority=High&search=elm&page=2', {
      [listRoute]: json(jobList([jobSummary()], { page: 2, totalCount: 13 })),
    })

    expect(lastQuery(stub)).toEqual({
      search: 'elm',
      status: 'OnHold',
      priority: 'High',
      page: '2',
      pageSize: '12',
    })
    expect(pressedChip(wrapper)).toBe('On hold, 3')
    expect((wrapper.find('select[aria-label="Priority"]').element as HTMLSelectElement).value).toBe(
      'High',
    )
    expect((wrapper.find('input[type="search"]').element as HTMLInputElement).value).toBe('elm')
  })

  it('ignores filter values the contract does not know', async () => {
    const { stub } = await openApp('/jobs?status=Closed&priority=urgent&page=-3', {
      [listRoute]: firstPage(),
    })

    expect(lastQuery(stub)).toEqual({ page: '1', pageSize: '12' })
  })

  it('filters by status from the chips and starts again at page 1', async () => {
    const { wrapper, router, stub } = await openApp('/jobs?page=2', {
      [listRoute]: firstPage(),
    })

    const onHold = wrapper
      .findAll('[aria-label="Filter by status"] button')
      .find((chip) => chip.text().startsWith('On hold'))
    await onHold?.trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.fullPath).toBe('/jobs?status=OnHold')
    expect(lastQuery(stub)).toEqual({ status: 'OnHold', page: '1', pageSize: '12' })
    expect(pressedChip(wrapper)).toBe('On hold, 3')

    await wrapper.find('[aria-label="Filter by status"] button').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.fullPath).toBe('/jobs')
  })

  it('filters by priority', async () => {
    const { wrapper, router, stub } = await openApp('/jobs', { [listRoute]: firstPage() })

    await wrapper.find('select[aria-label="Priority"]').setValue('Urgent')
    await flushPromises()

    expect(router.currentRoute.value.query.priority).toBe('Urgent')
    expect(lastQuery(stub).priority).toBe('Urgent')
  })

  it('lists All technicians, then every technician by name as the API orders them, with no Unassigned option', async () => {
    const { wrapper } = await openApp('/jobs', { [listRoute]: firstPage() })
    const select = technicianFilter(wrapper)

    expect(optionTexts(select)).toEqual([
      'All technicians',
      'Aisha Karim',
      'Lena Park',
      'Marco Reyes',
      'Priya Natarajan',
      'Samuel Osei',
      'Tom Becker',
    ])
    expect(optionTexts(select)).not.toContain('Unassigned')
    expect(select.value).toBe('')
    expect(select.disabled).toBe(false)
    expect(Array.from(select.options).some((option) => option.disabled)).toBe(false)
  })

  it('keeps the API order of the technicians and never sorts their names itself', async () => {
    const [aisha, lena, , , samuel, tomTech] = demoTechnicians
    const { wrapper } = await openApp('/jobs', {
      [listRoute]: firstPage(),
      [techniciansRoute]: json({ items: [tomTech, samuel, lena, aisha] }),
    })

    expect(optionTexts(technicianFilter(wrapper))).toEqual([
      'All technicians',
      'Tom Becker',
      'Samuel Osei',
      'Lena Park',
      'Aisha Karim',
    ])
  })

  it('filters by technician, sends technicianId, and starts again at page 1', async () => {
    const { wrapper, router, stub } = await openApp('/jobs?page=2', { [listRoute]: firstPage() })

    await wrapper.find('select[aria-label="Technician"]').setValue(priya.id)
    await flushPromises()

    expect(router.currentRoute.value.query).toEqual({ technicianId: priya.id })
    expect(lastQuery(stub)).toEqual({ technicianId: priya.id, page: '1', pageSize: '12' })
    expect(technicianFilter(wrapper).value).toBe(priya.id)

    await wrapper.find('select[aria-label="Technician"]').setValue('')
    await flushPromises()
    expect(router.currentRoute.value.fullPath).toBe('/jobs')
    expect(lastQuery(stub)).toEqual({ page: '1', pageSize: '12' })
  })

  it('reads the technician from the address', async () => {
    const { wrapper, stub } = await openApp(`/jobs?technicianId=${priya.id}&status=Assigned`, {
      [listRoute]: firstPage(),
    })

    expect(lastQuery(stub)).toEqual({
      status: 'Assigned',
      technicianId: priya.id,
      page: '1',
      pageSize: '12',
    })
    expect(technicianFilter(wrapper).value).toBe(priya.id)
  })

  it('shows an id the list does not hold as Unknown technician until Clear filters', async () => {
    const unknownId = '01a0f6b1-9999-7000-8000-000000000999'
    const { wrapper, router } = await openApp(`/jobs?technicianId=${unknownId}`, {
      [listRoute]: [json(jobList([], { totalCount: 0 })), firstPage()],
    })
    const select = technicianFilter(wrapper)

    expect(optionTexts(select).slice(0, 2)).toEqual(['All technicians', 'Unknown technician'])
    expect(select.value).toBe(unknownId)

    await wrapper.find('tbody td[colspan="7"] button').trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.fullPath).toBe('/jobs')
    expect(technicianFilter(wrapper).value).toBe('')
    expect(optionTexts(technicianFilter(wrapper))).not.toContain('Unknown technician')
  })

  it('ignores a technician value that is not an id', async () => {
    const { stub } = await openApp('/jobs?technicianId=marco', { [listRoute]: firstPage() })

    expect(lastQuery(stub)).toEqual({ page: '1', pageSize: '12' })
  })

  it('holds only All technicians, disabled, while the technicians load, and the jobs still list', async () => {
    const slow = deferred()
    const { wrapper } = await openApp('/jobs', {
      [listRoute]: firstPage(),
      [techniciansRoute]: slow.reply,
    })

    expect(optionTexts(technicianFilter(wrapper))).toEqual(['All technicians'])
    expect(technicianFilter(wrapper).value).toBe('')
    expect(technicianFilter(wrapper).disabled).toBe(true)
    expect(wrapper.findAll('tbody tr')).toHaveLength(12)

    slow.resolve(new Response(JSON.stringify({ items: demoTechnicians }), { status: 200 }))
    await flushPromises()
    expect(technicianFilter(wrapper).disabled).toBe(false)
    expect(technicianFilter(wrapper).value).toBe('')
  })

  it('reads Selected technician, disabled, while the technicians load with a technician in the address', async () => {
    const slow = deferred()
    const { wrapper, stub } = await openApp(`/jobs?technicianId=${priya.id}`, {
      [listRoute]: firstPage(),
      [techniciansRoute]: slow.reply,
    })

    expect(optionTexts(technicianFilter(wrapper))).toEqual(['Selected technician'])
    expect(technicianFilter(wrapper).value).toBe(priya.id)
    expect(technicianFilter(wrapper).disabled).toBe(true)
    expect(lastQuery(stub)).toEqual({ technicianId: priya.id, page: '1', pageSize: '12' })
    expect(wrapper.findAll('tbody tr')).toHaveLength(12)

    slow.resolve(new Response(JSON.stringify({ items: demoTechnicians }), { status: 200 }))
    await flushPromises()
    expect(technicianFilter(wrapper).disabled).toBe(false)
    expect(technicianFilter(wrapper).value).toBe(priya.id)
    expect(technicianFilter(wrapper).selectedOptions[0]?.text.trim()).toBe('Priya Natarajan')
    expect(optionTexts(technicianFilter(wrapper))).not.toContain('Selected technician')
  })

  it('reads Selected technician after the technicians fail with a technician in the address, until Clear filters', async () => {
    const { wrapper, router } = await openApp(`/jobs?technicianId=${priya.id}`, {
      [listRoute]: [json(jobList([], { totalCount: 0 })), firstPage()],
      [techniciansRoute]: problem(500, 'server.error'),
    })

    expect(optionTexts(technicianFilter(wrapper))).toEqual(['Selected technician'])
    expect(technicianFilter(wrapper).value).toBe(priya.id)
    expect(technicianFilter(wrapper).disabled).toBe(true)

    await wrapper.find('tbody td[colspan="7"] button').trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.fullPath).toBe('/jobs')
    expect(optionTexts(technicianFilter(wrapper))).toEqual(['All technicians'])
    expect(technicianFilter(wrapper).value).toBe('')
    expect(technicianFilter(wrapper).disabled).toBe(true)
  })

  it('holds only All technicians, disabled, after the technicians fail to load', async () => {
    const { wrapper } = await openApp('/jobs', {
      [listRoute]: firstPage(),
      [techniciansRoute]: problem(500, 'server.error'),
    })

    expect(optionTexts(technicianFilter(wrapper))).toEqual(['All technicians'])
    expect(technicianFilter(wrapper).disabled).toBe(true)
    expect(wrapper.findAll('tbody tr')).toHaveLength(12)
    expect(wrapper.find('.banner--error').exists()).toBe(false)
  })

  it('searches once typing pauses, and at once on Enter', async () => {
    vi.useFakeTimers({ toFake: ['Date', 'setTimeout', 'clearTimeout'] })
    vi.setSystemTime(demoToday)
    const { wrapper, router, stub } = await openApp('/jobs', { [listRoute]: firstPage() })
    const search = wrapper.find('input[type="search"]')

    await search.setValue('elm')
    vi.advanceTimersByTime(299)
    await flushPromises()
    expect(stub.requestsTo(listRoute)).toHaveLength(1)

    vi.advanceTimersByTime(1)
    await flushPromises()
    expect(router.currentRoute.value.query.search).toBe('elm')
    expect(lastQuery(stub).search).toBe('elm')

    await search.setValue('elm street')
    await wrapper.find('form[role="search"]').trigger('submit')
    await flushPromises()
    expect(router.currentRoute.value.query.search).toBe('elm street')
    expect(stub.requestsTo(listRoute)).toHaveLength(3)
  })

  it('pages with Previous and Next', async () => {
    const { wrapper, router, stub } = await openApp('/jobs', {
      [listRoute]: [firstPage(), json(jobList(demoSummaries(8), { page: 2 }))],
    })

    await wrapper.findAll('nav[aria-label="Pagination"] button')[1]?.trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.fullPath).toBe('/jobs?page=2')
    expect(lastQuery(stub)).toEqual({ page: '2', pageSize: '12' })
    expect(wrapper.find('.pagination__summary').text()).toBe('Showing 13–20 of 20 jobs')
    expect(wrapper.findAll('tbody tr')).toHaveLength(8)
  })

  it('moves to the last page when the page asked for is past the end', async () => {
    const { router, stub } = await openApp('/jobs?page=5', {
      [listRoute]: [json(jobList([], { page: 5 })), json(jobList(demoSummaries(8), { page: 2 }))],
    })

    expect(router.currentRoute.value.fullPath).toBe('/jobs?page=2')
    expect(stub.requestsTo(listRoute).map((request) => queryOf(request).page)).toEqual(['5', '2'])
  })

  it('offers Clear filters when nothing matches', async () => {
    const { wrapper, router } = await openApp('/jobs?status=Cancelled&search=zzz', {
      [listRoute]: [json(jobList([], { totalCount: 0 })), firstPage()],
    })
    const empty = wrapper.find('tbody td[colspan="7"]')

    expect(empty.find('h2').text()).toBe('No jobs match these filters')
    expect(empty.text()).toContain('Change the search or filters, or clear them to see every job.')
    expect(wrapper.find('p[role="status"].visually-hidden').text()).toBe(
      'No jobs match these filters',
    )

    await empty.find('button').trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.fullPath).toBe('/jobs')
    expect(wrapper.findAll('tbody tr')).toHaveLength(12)
    expect(document.activeElement).toBe(
      wrapper.find('input[aria-label="Search jobs, customers or sites"]').element,
    )
  })

  it('keeps an empty state inside a sticky block as wide as the visible table region', async () => {
    const { wrapper } = await openApp('/jobs?search=zzz', {
      [listRoute]: json(jobList([], { totalCount: 0 })),
    })
    const block = wrapper.find('tbody td[colspan="7"] > .data-table__empty')

    expect(block.find('h2').text()).toBe('No jobs match these filters')
    expect(declarationsOf('shared/ui/DataTable.vue', '.data-table__scroll')).toMatchObject({
      'container-type': 'inline-size',
    })
    expect(declarationsOf('shared/ui/DataTable.vue', '.data-table__empty')).toMatchObject({
      position: 'sticky',
      left: '0',
      width: '100cqw',
    })
  })

  it('points to New job when there are no jobs at all', async () => {
    const { wrapper } = await openApp('/jobs', {
      [listRoute]: json(
        jobList([], {
          totalCount: 0,
          statusCounts: [],
        }),
      ),
    })
    const empty = wrapper.find('tbody td[colspan="7"]')

    expect(empty.find('h2').text()).toBe('No jobs yet')
    expect(empty.find('a').attributes('href')).toBe('/jobs/new')
    expect(wrapper.find('.page-header__subline').text()).toBe('0 jobs')
  })

  it('shows skeleton rows under the real header while the first page loads', async () => {
    const slow = deferred()
    const { wrapper } = await openApp('/jobs', { [listRoute]: slow.reply })

    expect(wrapper.find('table').attributes('aria-busy')).toBe('true')
    expect(wrapper.findAll('th')).toHaveLength(7)
    expect(wrapper.findAll('tbody tr[aria-hidden="true"]')).toHaveLength(5)
    expect(wrapper.find('.data-table [role="status"]').text()).toBe('Loading jobs')

    slow.resolve(new Response(JSON.stringify(jobList(demoSummaries(12))), { status: 200 }))
    await flushPromises()
    expect(wrapper.find('table').attributes('aria-busy')).toBeUndefined()
  })

  it('shows a load failure with its reference, and Try again reloads', async () => {
    const { wrapper, stub } = await openApp('/jobs', {
      [listRoute]: [problem(500, 'server.error', { correlationId: reference }), firstPage()],
    })
    const banner = wrapper.find('.banner--error')

    expect(banner.attributes('role')).toBe('status')
    expect(banner.text()).toContain('Jobs could not be loaded.')
    expect(banner.text()).toContain(`Reference ${reference}`)
    expect(wrapper.find('table').exists()).toBe(false)

    await banner.find('button').trigger('click')
    await flushPromises()

    expect(stub.requestsTo(listRoute)).toHaveLength(2)
    expect(wrapper.findAll('tbody tr')).toHaveLength(12)
    expect(document.activeElement).toBe(wrapper.find('[role="region"][aria-label="Jobs"]').element)
  })

  it('keeps focus on Try again when the reload fails again', async () => {
    const { wrapper } = await openApp('/jobs', {
      [listRoute]: [problem(500, 'server.error'), networkFailure()],
    })

    await wrapper.find('.banner--error button').trigger('click')
    await flushPromises()

    const banner = wrapper.find('.banner--error')
    expect(banner.attributes('role')).toBe('alert')
    expect(document.activeElement).toBe(banner.find('button').element)
  })

  it('announces a failure after a filter change as an alert, without the old job total', async () => {
    const { wrapper } = await openApp('/jobs', {
      [listRoute]: [firstPage(), networkFailure()],
    })
    expect(wrapper.find('.page-header__subline').text()).toBe('20 jobs')

    await wrapper.find('select[aria-label="Priority"]').setValue('Low')
    await flushPromises()

    const banner = wrapper.find('.banner--error')
    expect(banner.attributes('role')).toBe('alert')
    expect(banner.find('.banner__reference').exists()).toBe(false)
    expect(wrapper.find('.page-header__subline').exists()).toBe(false)
  })
})

describe('the job detail page', () => {
  it('shows the read-only details with the assignee and the status history', async () => {
    const job = assignedInProgressJob()
    const { wrapper } = await openApp(`/jobs/${job.id}`, { [`GET /api/jobs/${job.id}`]: json(job) })
    const rows = wrapper
      .findAll('dl > div')
      .map((row) => [row.find('dt').text(), row.find('dd').text()])

    expect(wrapper.find('.page-header__number').text()).toBe('JOB-1042')
    expect(wrapper.find('h1').text()).toBe('Rooftop HVAC unit not cooling')
    expect(wrapper.find('.page-header__meta').text()).toBe(
      'In progressHighCreated 8:14 AM by Dana Whitfield',
    )
    expect(rows).toEqual([
      ['Customer', 'Brightwater Medical Clinic'],
      ['Site', '214 Harbor Road, Suite 3'],
      ['Access', 'Roof access through the east stairwell. Ask reception for the roof key.'],
      ['Contact', 'Nina Alvarez(555) 014-6620'],
      ['Scheduled', 'Mon 19 Oct, 9:00–11:00 AM'],
      ['Equipment', 'RTU-2, 10-ton packaged rooftop unit'],
      ['Technician', 'MRMarco ReyesHVAC · Assigned 8:20 AM'],
      ['Checklist', 'Rooftop HVAC service'],
      ['Created', '8:14 AM by Dana Whitfield'],
    ])
    expect(wrapper.find('.job-detail__description').text()).toBe(job.description)
    expect(wrapper.findAll('.status-timeline__entry').map((entry) => entry.text())).toEqual([
      '8:14 AMCreated by Dana Whitfield',
      '8:20 AMAssigned to Marco Reyes',
      '9:12 AMStarted by Marco Reyes',
    ])
    expect(wrapper.findAll('.status-timeline__dot--newest')).toHaveLength(1)
    expect(wrapper.findAll('.status-timeline__dot')[2]?.classes()).toContain(
      'status-timeline__dot--newest',
    )
    expect(document.title).toBe('JOB-1042 · FieldService Admin')
    expect(wrapper.find('nav[aria-label="Breadcrumb"] [aria-current="page"]').text()).toBe(
      'JOB-1042',
    )
    expect(wrapper.find('main').findAll('input, select, textarea')).toHaveLength(0)
  })

  it('leaves out optional fields without a value and shows an unassigned job as Unassigned', async () => {
    const job = jobDetails({
      siteAccessNotes: null,
      contactName: null,
      contactPhone: null,
      equipment: null,
    })
    const { wrapper } = await openApp(`/jobs/${job.id}`, { [`GET /api/jobs/${job.id}`]: json(job) })
    const labels = wrapper.findAll('dt').map((term) => term.text())

    expect(labels).toEqual(['Customer', 'Site', 'Scheduled', 'Technician', 'Checklist', 'Created'])
    expect(wrapper.findAll('dl > div')[3]?.find('dd').text()).toBe('Unassigned')
  })

  it('shows the contact name over the phone, the phone as a muted second line', async () => {
    const job = assignedInProgressJob()
    const { wrapper } = await openApp(`/jobs/${job.id}`, { [`GET /api/jobs/${job.id}`]: json(job) })
    const contact = wrapper.findAll('dl > div').find((row) => row.find('dt').text() === 'Contact')
    const lines = contact?.find('dd .job-details__person').element.children ?? []

    expect(Array.from(lines, (line) => line.textContent)).toEqual([
      'Nina Alvarez',
      '(555) 014-6620',
    ])
    expect(lines[1]?.classList.contains('job-details__secondary')).toBe(true)
    expect(lines[1]?.classList.contains('job-details__phone')).toBe(true)
    expect(contact?.text()).not.toContain(',')
  })

  it('shows the contact phone alone when the contact has no name', async () => {
    const job = jobDetails({ contactName: null, contactPhone: '(555) 014-4417' })
    const { wrapper } = await openApp(`/jobs/${job.id}`, { [`GET /api/jobs/${job.id}`]: json(job) })
    const contact = wrapper.findAll('dl > div').find((row) => row.find('dt').text() === 'Contact')

    expect(contact?.find('dd').text()).toBe('(555) 014-4417')
    expect(contact?.find('.job-details__secondary').exists()).toBe(false)
  })

  it('dates history from earlier days and quotes the hold reason', async () => {
    const job = onHoldJob()
    const { wrapper } = await openApp(`/jobs/${job.id}`, { [`GET /api/jobs/${job.id}`]: json(job) })

    expect(wrapper.findAll('.status-timeline__entry').map((entry) => entry.text())).toEqual([
      'Thu 15 Oct, 10:15 AMCreated by Dana Whitfield',
      'Thu 15 Oct, 10:20 AMAssigned to Tom Becker',
      'Fri 16 Oct, 1:05 PMStarted by Tom Becker',
      'Fri 16 Oct, 1:50 PMPut on hold by Tom Becker“Waiting for the gas utility to restore supply to the boiler room.”',
    ])
    expect(wrapper.find('.page-header__meta').text()).toBe(
      'On holdNormalCreated Thu 15 Oct, 10:15 AM by Dana Whitfield',
    )
  })

  it('says no job matches the link, never that it was removed, and leads back to the list', async () => {
    const jobId = '7a0c1f52-5b8e-4a3d-9c61-0d2b7e4f8a19'
    const { wrapper } = await openApp(`/jobs/${jobId}`, {
      [`GET /api/jobs/${jobId}`]: problem(404, 'job.not-found'),
    })
    const current = wrapper.find('nav[aria-label="Breadcrumb"] [aria-current="page"]')

    expect(wrapper.find('h1').text()).toBe('Job not found')
    expect(wrapper.find('h2').text()).toBe('No job matches this link')
    expect(wrapper.find('.empty-state__body').text()).toBe(
      'The link may be mistyped or out of date. Every job, cancelled ones included, is in the jobs list.',
    )
    expect(wrapper.find('main').text()).not.toMatch(/removed|no longer exists/)
    expect(wrapper.find('main a[href="/jobs"]').text()).toBe('Back to jobs')
    expect(current.text()).toBe('Job not found')
    expect(current.classes()).not.toContain('app-shell__crumb-current--mono')
    expect(document.title).toBe('Job not found · FieldService Admin')
  })

  it('shows a load failure with its reference, and Try again reloads', async () => {
    const job = assignedInProgressJob()
    const { wrapper, stub } = await openApp(`/jobs/${job.id}`, {
      [`GET /api/jobs/${job.id}`]: [
        problem(500, 'server.error', { correlationId: reference }),
        json(job),
      ],
    })
    const banner = wrapper.find('.banner--error')

    expect(banner.text()).toContain('The job could not be loaded.')
    expect(banner.text()).toContain(`Reference ${reference}`)

    await banner.find('button').trigger('click')
    await flushPromises()

    expect(stub.requestsTo(`GET /api/jobs/${job.id}`)).toHaveLength(2)
    expect(wrapper.find('h1').text()).toBe('Rooftop HVAC unit not cooling')
  })

  it('shows a skeleton with a named heading while the job loads', async () => {
    const job = assignedInProgressJob()
    const slow = deferred()
    const { wrapper } = await openApp(`/jobs/${job.id}`, {
      [`GET /api/jobs/${job.id}`]: slow.reply,
    })

    expect(wrapper.find('h1').text()).toBe('Job details')
    expect(wrapper.find('main p[role="status"]').text()).toBe('Loading job')

    slow.resolve(new Response(JSON.stringify(job), { status: 200 }))
    await flushPromises()
    expect(wrapper.find('h1').text()).toBe('Rooftop HVAC unit not cooling')
  })
})
