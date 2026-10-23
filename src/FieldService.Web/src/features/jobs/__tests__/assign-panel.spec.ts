import { flushPromises, mount, type DOMWrapper, type VueWrapper } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory } from 'vue-router'

import App from '@/app/App.vue'
import { createAppRouter, installNavigationGuards } from '@/app/router'
import { readTokens, ruleDeclarations } from '@/app/styles/__tests__/stylesheet-source'
import { sessionResponse } from '@/features/auth/__tests__/session-fixtures'
import {
  deferred,
  json,
  networkFailure,
  problem,
  stubFetch,
  type FetchStub,
  type Reply,
  type RouteTable,
} from '@/shared/testing/fetch-stub'

import type { JobDetails, Technician } from '../jobs-api'
import { assignOrder, assignRowDetail, type DetailPart } from '../technician-options'
import {
  aishaTechnician,
  assignedInProgressJob,
  demoTechnicians,
  demoToday,
  historyEntry,
  jobDetails,
  lenaTechnician,
  marco,
  marcoTechnician,
  onDemoDay,
  priya,
  priyaTechnician,
  readOnlyAffordances,
  samuelTechnician,
  technician,
  tomTechnician,
} from './job-fixtures'

const job = jobDetails()

const jobRoute = `GET /api/jobs/${job.id}`

const assignRoute = `PUT /api/jobs/${job.id}/assignment`

const techniciansRoute = 'GET /api/technicians'

const reference = '5f0c8a3e-2b71-4d9a-9c6e-81f4a2d7b390'

const cardFile = 'features/jobs/components/AssignTechnicianCard.vue'

interface OpenPage {
  readonly wrapper: VueWrapper
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

function names(items: readonly Technician[]): string[] {
  return items.map((item) => item.displayName)
}

function assignedTo(target: Technician, overrides: Partial<JobDetails> = {}): JobDetails {
  return jobDetails({
    status: 'Assigned',
    assignedTechnician: { id: target.id, displayName: target.displayName, trade: target.trade },
    assignedAt: onDemoDay(0, 10, 44),
    updatedAt: onDemoDay(0, 10, 44),
    version: 48102,
    history: [
      historyEntry(),
      historyEntry({
        id: '01a1549f-79e0-71ae-8e7f-000000000002',
        action: 'Assigned',
        fromStatus: 'Unassigned',
        toStatus: 'Assigned',
        technician: { id: target.id, displayName: target.displayName },
        occurredAt: onDemoDay(0, 10, 44),
      }),
    ],
    ...overrides,
  })
}

async function openJob(details: JobDetails, routeTable: RouteTable = {}): Promise<OpenPage> {
  const stub = stubFetch({
    'POST /api/auth/refresh': json(sessionResponse()),
    [`GET /api/jobs/${details.id}`]: json(details),
    [techniciansRoute]: json({ items: demoTechnicians }),
    ...routeTable,
  })
  const router = createAppRouter(createMemoryHistory())
  installNavigationGuards(router)
  await router.push(`/jobs/${details.id}`)
  const wrapper = mount(App, { global: { plugins: [router] }, attachTo: document.body })
  mounted = wrapper
  await flushPromises()
  return { wrapper, stub }
}

function panel(wrapper: VueWrapper): DOMWrapper<HTMLElement> {
  return wrapper.find<HTMLElement>('section[aria-labelledby="assign-title"]')
}

function rowNames(wrapper: VueWrapper): string[] {
  return panel(wrapper)
    .findAll('.assign-row__name')
    .map((name) => name.text())
}

function row(wrapper: VueWrapper, name: string): DOMWrapper<HTMLLabelElement> {
  const found = panel(wrapper)
    .findAll<HTMLLabelElement>('label.assign-row')
    .find((label) => label.find('.assign-row__name').text() === name)
  if (found === undefined) {
    throw new Error(`No assign row for ${name}`)
  }
  return found
}

function radio(wrapper: VueWrapper, name: string): DOMWrapper<HTMLInputElement> {
  return row(wrapper, name).find<HTMLInputElement>('input[type="radio"]')
}

function assignButton(wrapper: VueWrapper): DOMWrapper<HTMLButtonElement> {
  return panel(wrapper).find<HTMLButtonElement>('button.app-button--block')
}

function helper(wrapper: VueWrapper): DOMWrapper<HTMLElement> {
  const id = assignButton(wrapper).attributes('aria-describedby') ?? ''
  return panel(wrapper).find<HTMLElement>(`[id="${id}"]`)
}

function searchField(wrapper: VueWrapper): DOMWrapper<HTMLInputElement> {
  return panel(wrapper).find<HTMLInputElement>('input[aria-label="Search technicians"]')
}

async function choose(wrapper: VueWrapper, name: string): Promise<void> {
  await radio(wrapper, name).setValue(true)
}

async function pressAssign(wrapper: VueWrapper): Promise<void> {
  await assignButton(wrapper).trigger('click')
  await flushPromises()
}

function bodiesOf(stub: FetchStub): unknown[] {
  return stub.requestsTo(assignRoute).map((request) => JSON.parse(request.body) as unknown)
}

describe('the assign order', () => {
  it('puts the template trade first, then available technicians, then fewer open jobs', () => {
    expect(names(assignOrder(demoTechnicians, 'Electrical'))).toEqual([
      'Priya Natarajan',
      'Aisha Karim',
      'Lena Park',
      'Marco Reyes',
      'Tom Becker',
      'Samuel Osei',
    ])
    expect(names(assignOrder(demoTechnicians, 'Hvac'))).toEqual([
      'Marco Reyes',
      'Samuel Osei',
      'Lena Park',
      'Priya Natarajan',
      'Aisha Karim',
      'Tom Becker',
    ])
    expect(names(assignOrder(demoTechnicians, 'FireSafety'))).toEqual([
      'Lena Park',
      'Priya Natarajan',
      'Aisha Karim',
      'Marco Reyes',
      'Tom Becker',
      'Samuel Osei',
    ])
  })

  it('drops the trade key when there is no template yet', () => {
    expect(names(assignOrder(demoTechnicians, null))).toEqual([
      'Lena Park',
      'Priya Natarajan',
      'Aisha Karim',
      'Marco Reyes',
      'Tom Becker',
      'Samuel Osei',
    ])
  })

  it('breaks ties with the API order and never compares names', () => {
    const zoe = technician({ id: 'z', displayName: 'Zoe Young', openJobCount: 2, nextJob: null })
    const adam = technician({ id: 'a', displayName: 'Adam Ash', openJobCount: 2, nextJob: null })

    expect(names(assignOrder([zoe, adam], 'Electrical'))).toEqual(['Zoe Young', 'Adam Ash'])
    expect(names(assignOrder([adam, zoe], 'Electrical'))).toEqual(['Adam Ash', 'Zoe Young'])
  })
})

describe('the assign row detail', () => {
  const timeOrDate =
    /\d{1,2}:\d{2}|\b(?:AM|PM)\b|\b(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\b/

  function textOf(parts: readonly DetailPart[]): string {
    return parts.map((part) => part.text).join('')
  }

  function moments(parts: readonly DetailPart[]): string[] {
    return parts.filter((part) => part.kind === 'moment').map((part) => part.text)
  }

  function plainTexts(parts: readonly DetailPart[]): string[] {
    return parts.filter((part) => part.kind === 'text').map((part) => part.text)
  }

  function runningSince(dayOffset: number, startHour: number, endHour: number): Technician {
    return technician({
      ...marcoTechnician,
      currentJob: {
        id: '01a15400-0000-7000-8000-000000001042',
        number: 1042,
        status: 'InProgress',
        scheduledStart: onDemoDay(dayOffset, startHour, 0),
        scheduledEnd: onDemoDay(dayOffset, endHour, 0),
      },
    })
  }

  it('keeps each time and each date of a schedule clause in one unbreakable part', () => {
    const today = assignRowDetail(priyaTechnician, 'Electrical', demoToday)
    const tomorrow = assignRowDetail(tomTechnician, 'Plumbing', demoToday)

    expect(textOf(today)).toBe('Electrical · 2 open jobs · finishes JOB-1049 by 12:00 PM')
    expect(moments(today)).toEqual(['12:00 PM'])
    expect(textOf(tomorrow)).toBe('Plumbing · 4 open jobs · next JOB-1048 on Tue 20 Oct at 8:00 AM')
    expect(moments(tomorrow)).toEqual(['Tue 20 Oct', '8:00 AM'])
    expect(
      [...plainTexts(today), ...plainTexts(tomorrow)].filter((text) => timeOrDate.test(text)),
    ).toEqual([])
  })

  it('says still on a job that has run past its end, with the day when it ended before today', () => {
    const afterEleven = new Date(2026, 9, 19, 11, 5)
    const overrun = assignRowDetail(marcoTechnician, 'Hvac', afterEleven)
    const sinceSunday = assignRowDetail(runningSince(-1, 8, 10), 'Hvac', demoToday)

    expect(textOf(overrun)).toBe('HVAC · 3 open jobs · still on JOB-1042, due 11:00 AM')
    expect(moments(overrun)).toEqual(['11:00 AM'])
    expect(overrun.filter((part) => part.kind === 'job-number').map((part) => part.text)).toEqual([
      'JOB-1042',
    ])
    expect(textOf(sinceSunday)).toBe(
      'HVAC · 3 open jobs · still on JOB-1042, due Sun 18 Oct at 10:00 AM',
    )
    expect(moments(sinceSunday)).toEqual(['Sun 18 Oct', '10:00 AM'])
  })

  it('says finishes by up to the minute a running job is due to end', () => {
    expect(textOf(assignRowDetail(marcoTechnician, 'Hvac', new Date(2026, 9, 19, 11, 0)))).toBe(
      'HVAC · 3 open jobs · finishes JOB-1042 by 11:00 AM',
    )
    expect(textOf(assignRowDetail(runningSince(1, 9, 12), 'Hvac', demoToday))).toBe(
      'HVAC · 3 open jobs · finishes JOB-1042 on Tue 20 Oct at 12:00 PM',
    )
  })

  it('names the next job, never an overrun, when the current job is on hold past its end', () => {
    expect(textOf(assignRowDetail(tomTechnician, 'Plumbing', demoToday))).toBe(
      'Plumbing · 4 open jobs · next JOB-1048 on Tue 20 Oct at 8:00 AM',
    )
  })
})

describe('the assign panel', () => {
  it('shows only while the job can be assigned, and only then asks for the technicians', async () => {
    const running = assignedInProgressJob()
    const { wrapper, stub } = await openJob(running)

    expect(wrapper.find('h1').text()).toBe('Rooftop HVAC unit not cooling')
    expect(panel(wrapper).exists()).toBe(false)
    expect(wrapper.find('.job-detail__side').exists()).toBe(false)
    expect(stub.requestsTo(techniciansRoute)).toHaveLength(0)
  })

  it('sits in the right column beside the job details', async () => {
    const { wrapper } = await openJob(job)

    expect(wrapper.find('.job-detail > .job-detail__side').exists()).toBe(true)
    expect(ruleDeclarations('features/jobs/pages/JobDetailPage.vue', '.job-detail__side')).toEqual({
      flex: '1 1 380px',
      'min-width': '0',
    })
  })

  it('heads the panel with the trade and keeps the group legend visually hidden', async () => {
    const { wrapper } = await openJob(job)
    const card = panel(wrapper)
    const legend = card.find('fieldset > legend')

    expect(card.find('h2').text()).toBe('Assign technician')
    expect(card.find('.section-card__subheading').text()).toBe('Electrical technicians first')
    expect(legend.text()).toBe('Assign technician')
    expect(legend.classes()).toContain('visually-hidden')
    expect(card.findAll('fieldset > p.assign-panel__eyebrow').map((item) => item.text())).toEqual([
      'Electrical',
      'Different trade',
    ])
  })

  it('lists the template trade first, then the other trades, in the assign order', async () => {
    const { wrapper } = await openJob(job)

    expect(rowNames(wrapper)).toEqual([
      'Priya Natarajan',
      'Aisha Karim',
      'Lena Park',
      'Marco Reyes',
      'Tom Becker',
      'Samuel Osei',
    ])
    expect(
      panel(wrapper)
        .findAll('fieldset > *')
        .slice(1)
        .map((child) => (child.find('.assign-row__name').exists() ? 'row' : child.text())),
    ).toEqual(['Electrical', 'row', 'row', 'Different trade', 'row', 'row', 'row', 'row'])
  })

  it('details each row on one line, with the schedule only on available rows of the trade', async () => {
    const { wrapper } = await openJob(job)
    const details = panel(wrapper)
      .findAll('.assign-row__detail')
      .map((detail) => detail.text())

    expect(details).toEqual([
      'Electrical · 2 open jobs · finishes JOB-1049 by 12:00 PM',
      'Electrical · 3 open jobs · next JOB-1055 at 1:00 PM',
      'Fire safety · 1 open job',
      'HVAC · 3 open jobs',
      'Plumbing · 4 open jobs',
      'HVAC · Off shift',
    ])
    expect(row(wrapper, 'Priya Natarajan').find('.assign-row__job').text()).toBe('JOB-1049')
    expect(ruleDeclarations(cardFile, '.assign-row__job')).toMatchObject({
      'white-space': 'nowrap',
      'font-family': 'var(--font-mono)',
    })
  })

  it('never lets a time or a date break inside a row, keeping each one on one line', async () => {
    const { wrapper } = await openJob(job, {
      [techniciansRoute]: json({
        items: [priyaTechnician, technician({ ...priyaTechnician, id: 'p2', currentJob: null })],
      }),
    })
    const details = panel(wrapper).findAll('.assign-row__detail')
    const bareText = details.flatMap((detail) =>
      Array.from(detail.element.childNodes)
        .filter((node) => node.nodeType === Node.TEXT_NODE)
        .map((node) => node.textContent ?? ''),
    )

    expect(details.map((detail) => detail.text())).toEqual([
      'Electrical · 2 open jobs · finishes JOB-1049 by 12:00 PM',
      'Electrical · 2 open jobs · next JOB-1046 on Tue 20 Oct at 9:00 AM',
    ])
    expect(
      details.map((detail) => detail.findAll('.assign-row__moment').map((part) => part.text())),
    ).toEqual([['12:00 PM'], ['Tue 20 Oct', '9:00 AM']])
    expect(bareText.join('|')).not.toMatch(/\d{1,2}:\d{2}|\bAM\b|\bPM\b|\bOct\b/)
    expect(ruleDeclarations(cardFile, '.assign-row__moment')).toEqual({ 'white-space': 'nowrap' })
  })

  it('says a technician is still on a job that has run past its end', async () => {
    vi.setSystemTime(new Date(2026, 9, 19, 11, 5))
    const freezer = jobDetails({
      id: '01a1546d-1f40-74c9-b6a6-b365b1422136',
      number: 1047,
      status: 'Assigned',
      checklistTemplate: {
        id: '01a15175-3c00-7000-8000-000102000000',
        name: 'Rooftop HVAC service',
        trade: 'Hvac',
      },
      assignedTechnician: marco,
      assignedAt: onDemoDay(0, 10, 5),
    })
    const { wrapper } = await openJob(freezer)

    expect(row(wrapper, 'Marco Reyes').find('.assign-row__detail').text()).toBe(
      'HVAC · 3 open jobs · still on JOB-1042, due 11:00 AM',
    )
    expect(
      row(wrapper, 'Marco Reyes')
        .findAll('.assign-row__detail > span')
        .map((part) => [part.classes()[0], part.text()]),
    ).toEqual([
      ['assign-row__job', 'JOB-1042'],
      ['assign-row__moment', '11:00 AM'],
    ])
  })

  it('dates a next job that is not today, and names an inactive technician', async () => {
    const tomorrowOnly = technician({
      ...priyaTechnician,
      currentJob: null,
    })
    const inactive = technician({
      ...samuelTechnician,
      isActive: false,
      isOnShift: true,
    })
    const { wrapper } = await openJob(job, {
      [techniciansRoute]: json({ items: [tomorrowOnly, inactive] }),
    })

    expect(row(wrapper, 'Priya Natarajan').find('.assign-row__detail').text()).toBe(
      'Electrical · 2 open jobs · next JOB-1046 on Tue 20 Oct at 9:00 AM',
    )
    expect(row(wrapper, 'Samuel Osei').find('.assign-row__detail').text()).toBe('HVAC · Inactive')
  })

  it('disables off-shift technicians from availableForAssignment and shows their shift', async () => {
    const { wrapper } = await openJob(job)
    const samuel = row(wrapper, 'Samuel Osei')

    expect(radio(wrapper, 'Samuel Osei').element.disabled).toBe(true)
    expect(samuel.classes()).toContain('assign-row--unavailable')
    expect(samuel.find('.status-chip--off-shift').text()).toBe('Off shift')
    expect(row(wrapper, 'Priya Natarajan').find('.status-chip--on-shift').text()).toBe('On shift')
    expect(
      ['Priya Natarajan', 'Aisha Karim', 'Lena Park', 'Marco Reyes', 'Tom Becker'].map(
        (name) => radio(wrapper, name).element.disabled,
      ),
    ).toEqual([false, false, false, false, false])
    expect(ruleDeclarations(cardFile, '.assign-row--unavailable')).toEqual({
      opacity: '0.6',
      cursor: 'not-allowed',
    })
  })

  it('preselects nobody on an unassigned job and keeps Assign technician unavailable', async () => {
    const { wrapper, stub } = await openJob(job)
    const button = assignButton(wrapper)

    expect(panel(wrapper).findAll('input[type="radio"]:checked')).toHaveLength(0)
    expect(button.text()).toBe('Assign technician')
    expect(button.attributes('aria-disabled')).toBe('true')
    expect(button.attributes('disabled')).toBeUndefined()
    expect(helper(wrapper).text()).toBe('Choose an available technician.')

    await pressAssign(wrapper)
    expect(stub.requestsTo(assignRoute)).toHaveLength(0)
  })

  it('names the chosen technician in full on the button and in its description', async () => {
    const { wrapper } = await openJob(job)

    await choose(wrapper, 'Priya Natarajan')

    expect(row(wrapper, 'Priya Natarajan').classes()).toContain('assign-row--selected')
    expect(assignButton(wrapper).text()).toBe('Assign to Priya Natarajan')
    expect(assignButton(wrapper).attributes('aria-disabled')).toBeUndefined()
    expect(helper(wrapper).text()).toBe(
      'Priya Natarajan will see this job in the technician app straight away.',
    )
  })

  it('assigns with the job version and re-renders the page from the response', async () => {
    const { wrapper, stub } = await openJob(job, {
      [assignRoute]: json(assignedTo(priyaTechnician)),
    })

    await choose(wrapper, 'Priya Natarajan')
    assignButton(wrapper).element.focus()
    await pressAssign(wrapper)

    expect(bodiesOf(stub)).toEqual([{ technicianId: priya.id, version: 48101 }])
    expect(stub.requestsTo(jobRoute)).toHaveLength(1)
    expect(wrapper.find('.page-header__meta .status-chip').text()).toBe('Assigned')
    const technicianRow = wrapper
      .findAll('dl > div')
      .find((item) => item.find('dt').text() === 'Technician')
    expect(technicianRow?.find('dd').text()).toBe('PNPriya NatarajanElectrical · Assigned 10:44 AM')
    expect(wrapper.findAll('.status-timeline__entry').map((entry) => entry.text())).toEqual([
      '10:20 AMCreated by Dana Whitfield',
      '10:44 AMAssigned to Priya Natarajan',
    ])
    expect(panel(wrapper).find('h2').text()).toBe('Reassign technician')
    expect(radio(wrapper, 'Priya Natarajan').element.checked).toBe(true)
    expect(assignButton(wrapper).text()).toBe('Reassign technician')
    expect(assignButton(wrapper).attributes('aria-disabled')).toBe('true')
    expect(helper(wrapper).attributes('role')).toBe('status')
    expect(helper(wrapper).text()).toBe('Assigned to Priya Natarajan.')
    expect(document.activeElement).toBe(assignButton(wrapper).element)
  })

  it('refetches the technicians in the background after a success, keeping the rows, the choice and the success line', async () => {
    const refetch = deferred()
    const priyaWithTheJob = technician({ ...priyaTechnician, openJobCount: 3 })
    const { wrapper, stub } = await openJob(job, {
      [assignRoute]: json(assignedTo(priyaTechnician)),
      [techniciansRoute]: [json({ items: demoTechnicians }), refetch.reply],
    })

    await choose(wrapper, 'Priya Natarajan')
    assignButton(wrapper).element.focus()
    await pressAssign(wrapper)

    expect(stub.requestsTo(techniciansRoute)).toHaveLength(2)
    expect(panel(wrapper).find('.assign-panel__skeleton').exists()).toBe(false)
    expect(rowNames(wrapper)).toHaveLength(6)
    expect(row(wrapper, 'Priya Natarajan').find('.assign-row__detail').text()).toContain(
      '2 open jobs',
    )
    expect(searchField(wrapper).element.disabled).toBe(false)
    expect(helper(wrapper).text()).toBe('Assigned to Priya Natarajan.')

    refetch.resolve(
      new Response(
        JSON.stringify({
          items: demoTechnicians.map((item) => (item.id === priya.id ? priyaWithTheJob : item)),
        }),
        { status: 200 },
      ),
    )
    await flushPromises()

    expect(row(wrapper, 'Priya Natarajan').find('.assign-row__detail').text()).toBe(
      'Electrical · 3 open jobs · finishes JOB-1049 by 12:00 PM',
    )
    expect(rowNames(wrapper).slice(0, 2)).toEqual(['Aisha Karim', 'Priya Natarajan'])
    expect(radio(wrapper, 'Priya Natarajan').element.checked).toBe(true)
    expect(helper(wrapper).attributes('role')).toBe('status')
    expect(helper(wrapper).text()).toBe('Assigned to Priya Natarajan.')
    expect(document.activeElement).toBe(assignButton(wrapper).element)
  })

  it('replaces the rows with the load failure when the background refetch fails, and moves focus to Try again', async () => {
    const { wrapper } = await openJob(job, {
      [assignRoute]: json(assignedTo(priyaTechnician)),
      [techniciansRoute]: [
        json({ items: demoTechnicians }),
        problem(500, 'server.error', { correlationId: reference }),
      ],
    })

    await choose(wrapper, 'Priya Natarajan')
    assignButton(wrapper).element.focus()
    await pressAssign(wrapper)

    const banner = panel(wrapper).find('.banner--error')
    expect(banner.attributes('role')).toBe('status')
    expect(banner.text()).toContain(
      'Technicians could not be loaded. Check your connection, then try again.',
    )
    expect(banner.text()).toContain(`Reference ${reference}`)
    expect(panel(wrapper).find('fieldset').exists()).toBe(false)
    expect(assignButton(wrapper).exists()).toBe(false)
    expect(searchField(wrapper).element.disabled).toBe(true)
    expect(document.activeElement).toBe(banner.find('button').element)
  })

  it('leaves focus where the user moved it when the background refetch fails', async () => {
    const refetch = deferred()
    const { wrapper } = await openJob(job, {
      [assignRoute]: json(assignedTo(priyaTechnician)),
      [techniciansRoute]: [json({ items: demoTechnicians }), refetch.reply],
    })

    await choose(wrapper, 'Priya Natarajan')
    await pressAssign(wrapper)
    const title = wrapper.find<HTMLElement>('h1').element
    title.focus()

    refetch.resolve(
      new Response(JSON.stringify({ title: 'Problem title', status: 500 }), {
        status: 500,
        headers: { 'Content-Type': 'application/problem+json' },
      }),
    )
    await flushPromises()

    expect(panel(wrapper).find('.banner--error').exists()).toBe(true)
    expect(document.activeElement).toBe(title)
  })

  it('shows Assigning… while the request runs, locks the rows and ignores another press', async () => {
    const slow = deferred()
    const { wrapper, stub } = await openJob(job, { [assignRoute]: slow.reply })

    await choose(wrapper, 'Aisha Karim')
    await pressAssign(wrapper)
    await pressAssign(wrapper)

    expect(assignButton(wrapper).text()).toBe('Assigning…')
    expect(assignButton(wrapper).attributes('aria-disabled')).toBe('true')
    expect(assignButton(wrapper).classes()).toContain('app-button--busy')
    expect(panel(wrapper).find('fieldset').attributes('inert')).toBeDefined()
    expect(row(wrapper, 'Aisha Karim').classes()).toContain('assign-row--selected')
    expect(stub.requestsTo(assignRoute)).toHaveLength(1)

    slow.resolve(new Response(JSON.stringify(assignedTo(aishaTechnician)), { status: 200 }))
    await flushPromises()
    expect(helper(wrapper).text()).toBe('Assigned to Aisha Karim.')
    expect(panel(wrapper).find('fieldset').attributes('inert')).toBeUndefined()
  })

  it('keeps every row and radio looking as it did while the request runs, without disabling them', async () => {
    const slow = deferred()
    const { wrapper } = await openJob(job, { [assignRoute]: slow.reply })
    await choose(wrapper, 'Aisha Karim')
    const before = panel(wrapper)
      .findAll('label.assign-row')
      .map((label) => [
        label.classes().join(' '),
        label.find('input').element.disabled,
        label.find('input').element.checked,
      ])

    await pressAssign(wrapper)

    const fieldset = panel(wrapper).find<HTMLFieldSetElement>('fieldset')
    expect(assignButton(wrapper).text()).toBe('Assigning…')
    expect(fieldset.element.disabled).toBe(false)
    expect(fieldset.element.matches(':disabled')).toBe(false)
    expect(panel(wrapper).findAll('input[type="radio"]:disabled')).toHaveLength(1)
    expect(radio(wrapper, 'Samuel Osei').element.disabled).toBe(true)
    expect(
      panel(wrapper)
        .findAll('label.assign-row')
        .map((label) => [
          label.classes().join(' '),
          label.find('input').element.disabled,
          label.find('input').element.checked,
        ]),
    ).toEqual(before)
    expect(radio(wrapper, 'Aisha Karim').element.checked).toBe(true)
    expect(ruleDeclarations(cardFile, '.assign-panel__group')).not.toHaveProperty('opacity')

    slow.resolve(new Response(JSON.stringify(assignedTo(aishaTechnician)), { status: 200 }))
    await flushPromises()
  })

  it('moves focus from a row to the button when the press starts while a radio holds focus', async () => {
    const slow = deferred()
    const { wrapper } = await openJob(job, { [assignRoute]: slow.reply })
    await choose(wrapper, 'Aisha Karim')
    radio(wrapper, 'Aisha Karim').element.focus()
    expect(document.activeElement).toBe(radio(wrapper, 'Aisha Karim').element)

    await pressAssign(wrapper)

    expect(panel(wrapper).find('fieldset').attributes('inert')).toBeDefined()
    expect(document.activeElement).toBe(assignButton(wrapper).element)

    slow.resolve(new Response(JSON.stringify(assignedTo(aishaTechnician)), { status: 200 }))
    await flushPromises()
    expect(document.activeElement).toBe(assignButton(wrapper).element)
  })

  it('checks the current technician of an assigned job and offers a reassignment', async () => {
    const freezer = jobDetails({
      id: '01a1546d-1f40-74c9-b6a6-b365b1422136',
      number: 1047,
      title: 'Walk-in freezer temperature alarm',
      status: 'Assigned',
      checklistTemplate: {
        id: '01a15175-3c00-7000-8000-000102000000',
        name: 'Rooftop HVAC service',
        trade: 'Hvac',
      },
      assignedTechnician: marco,
      assignedAt: onDemoDay(0, 10, 5),
    })
    const reassignRoute = `PUT /api/jobs/${freezer.id}/assignment`
    const slow = deferred()
    const { wrapper, stub } = await openJob(freezer, { [reassignRoute]: slow.reply })

    expect(panel(wrapper).find('h2').text()).toBe('Reassign technician')
    expect(panel(wrapper).find('.section-card__subheading').text()).toBe('HVAC technicians first')
    expect(rowNames(wrapper)).toEqual([
      'Marco Reyes',
      'Samuel Osei',
      'Lena Park',
      'Priya Natarajan',
      'Aisha Karim',
      'Tom Becker',
    ])
    expect(radio(wrapper, 'Marco Reyes').element.checked).toBe(true)
    expect(row(wrapper, 'Marco Reyes').find('.assign-row__detail').text()).toBe(
      'HVAC · 3 open jobs · finishes JOB-1042 by 11:00 AM',
    )
    expect(assignButton(wrapper).text()).toBe('Reassign technician')
    expect(assignButton(wrapper).attributes('aria-disabled')).toBe('true')
    expect(helper(wrapper).text()).toBe(
      'Marco Reyes is assigned now. Choose another technician to reassign the job.',
    )

    await choose(wrapper, 'Aisha Karim')
    expect(assignButton(wrapper).text()).toBe('Reassign to Aisha Karim')
    expect(helper(wrapper).text()).toBe(
      'Aisha Karim will see this job in the technician app straight away. Marco Reyes will no longer see it.',
    )

    await pressAssign(wrapper)
    expect(assignButton(wrapper).text()).toBe('Reassigning…')
    expect(JSON.parse(stub.requestsTo(reassignRoute)[0]?.body ?? '{}')).toEqual({
      technicianId: aishaTechnician.id,
      version: 48101,
    })

    const reassigned = assignedTo(aishaTechnician, {
      id: freezer.id,
      number: 1047,
      checklistTemplate: freezer.checklistTemplate,
    })
    slow.resolve(new Response(JSON.stringify(reassigned), { status: 200 }))
    await flushPromises()
    expect(helper(wrapper).text()).toBe('Reassigned to Aisha Karim.')
    expect(radio(wrapper, 'Aisha Karim').element.checked).toBe(true)
  })

  it('says nobody is available when every row is disabled', async () => {
    const offShift = demoTechnicians.map((item) =>
      technician({ ...item, isOnShift: false, availableForAssignment: false }),
    )
    const { wrapper } = await openJob(job, { [techniciansRoute]: json({ items: offShift }) })

    expect(panel(wrapper).findAll('input[type="radio"]:not(:disabled)')).toHaveLength(0)
    expect(assignButton(wrapper).attributes('aria-disabled')).toBe('true')
    expect(helper(wrapper).text()).toBe('No technician is available right now.')
  })

  it('filters the rows by name as the user types, leaving out empty groups', async () => {
    const { wrapper } = await openJob(job)

    await searchField(wrapper).setValue('PRI')
    expect(rowNames(wrapper)).toEqual(['Priya Natarajan'])
    expect(
      panel(wrapper)
        .findAll('.assign-panel__eyebrow')
        .map((item) => item.text()),
    ).toEqual(['Electrical'])

    await searchField(wrapper).setValue('lena')
    expect(rowNames(wrapper)).toEqual(['Lena Park'])
    const eyebrows = panel(wrapper).findAll('.assign-panel__eyebrow')
    expect(eyebrows.map((item) => item.text())).toEqual(['Different trade'])
    expect(eyebrows[0]?.classes()).not.toContain('assign-panel__eyebrow--later')
  })

  it('says when no technician matches the search, and Clear search brings the list back', async () => {
    const { wrapper } = await openJob(job)

    await searchField(wrapper).setValue('zzz')
    const note = panel(wrapper).find('p[role="status"]')
    expect(note.text()).toBe('No technicians match “zzz”.')
    expect(panel(wrapper).find('fieldset').exists()).toBe(false)

    const clear = panel(wrapper)
      .findAll('button')
      .find((button) => button.text() === 'Clear search')
    expect(clear?.classes()).toContain('app-button--quiet')
    await clear?.trigger('click')

    expect(searchField(wrapper).element.value).toBe('')
    expect(rowNames(wrapper)).toHaveLength(6)
    expect(document.activeElement).toBe(searchField(wrapper).element)
  })

  it('says when there are no technician accounts at all, without Clear search', async () => {
    const { wrapper } = await openJob(job, { [techniciansRoute]: json({ items: [] }) })

    expect(panel(wrapper).find('p[role="status"]').text()).toBe(
      'There are no technician accounts yet.',
    )
    expect(
      panel(wrapper)
        .findAll('button')
        .map((button) => button.text()),
    ).toEqual(['Assign technician'])
    expect(helper(wrapper).text()).toBe('No technician is available right now.')
  })

  it('shows skeleton rows and a disabled search while the technicians load, with no button', async () => {
    const slow = deferred()
    const { wrapper } = await openJob(job, { [techniciansRoute]: slow.reply })
    const card = panel(wrapper)

    expect(card.find('h2').text()).toBe('Assign technician')
    expect(card.findAll('.assign-panel__skeleton-row')).toHaveLength(3)
    expect(card.find('.assign-panel__skeleton').attributes('aria-hidden')).toBe('true')
    expect(card.find('p[role="status"].visually-hidden').text()).toBe('Loading technicians')
    expect(searchField(wrapper).element.disabled).toBe(true)
    expect(card.find('button').exists()).toBe(false)
    expect(
      card
        .findAll('.loading-state__block')
        .map((block) => Array.from(block.element.classList).find((name) => name.includes('--'))),
    ).toEqual(
      Array.from({ length: 3 }, () => [
        'loading-state__block--radio',
        'loading-state__block--avatar-large',
        'loading-state__block--technician-name',
        'loading-state__block--technician-detail',
      ]).flat(),
    )

    slow.resolve(new Response(JSON.stringify({ items: demoTechnicians }), { status: 200 }))
    await flushPromises()
    expect(rowNames(wrapper)).toHaveLength(6)
    expect(searchField(wrapper).element.disabled).toBe(false)
  })

  it('shows a technicians load failure with its reference, and Try again moves focus to the search', async () => {
    const { wrapper, stub } = await openJob(job, {
      [techniciansRoute]: [
        problem(500, 'server.error', { correlationId: reference }),
        json({ items: demoTechnicians }),
      ],
    })
    const banner = panel(wrapper).find('.banner--error')

    expect(banner.attributes('role')).toBe('status')
    expect(banner.text()).toContain(
      'Technicians could not be loaded. Check your connection, then try again.',
    )
    expect(banner.text()).toContain(`Reference ${reference}`)
    expect(assignButton(wrapper).exists()).toBe(false)

    await banner.find('button').trigger('click')
    await flushPromises()

    expect(stub.requestsTo(techniciansRoute)).toHaveLength(2)
    expect(rowNames(wrapper)).toHaveLength(6)
    expect(document.activeElement).toBe(searchField(wrapper).element)
  })

  it('keeps focus on Try again when the technicians fail again', async () => {
    const { wrapper } = await openJob(job, {
      [techniciansRoute]: [problem(500, 'server.error'), networkFailure()],
    })

    await panel(wrapper).find('.banner--error button').trigger('click')
    await flushPromises()

    const banner = panel(wrapper).find('.banner--error')
    expect(banner.attributes('role')).toBe('alert')
    expect(document.activeElement).toBe(banner.find('button').element)
  })

  it('explains an off-shift technician, refreshes the list and moves focus to the first enabled row', async () => {
    const priyaOffShift = technician({
      ...priyaTechnician,
      isOnShift: false,
      availableForAssignment: false,
    })
    const refreshed = demoTechnicians.map((item) => (item.id === priya.id ? priyaOffShift : item))
    const { wrapper, stub } = await openJob(job, {
      [assignRoute]: problem(409, 'technician.unavailable'),
      [techniciansRoute]: [json({ items: demoTechnicians }), json({ items: refreshed })],
    })

    await choose(wrapper, 'Priya Natarajan')
    await pressAssign(wrapper)

    const banner = panel(wrapper).find('.banner--warning')
    expect(banner.attributes('role')).toBe('alert')
    expect(banner.text()).toBe(
      'Priya Natarajan is no longer available. The list is up to date. Choose another technician.',
    )
    expect(banner.find('.banner__reference').exists()).toBe(false)
    expect(stub.requestsTo(techniciansRoute)).toHaveLength(2)
    expect(panel(wrapper).findAll('input[type="radio"]:checked')).toHaveLength(0)
    expect(radio(wrapper, 'Priya Natarajan').element.disabled).toBe(true)
    expect(assignButton(wrapper).text()).toBe('Assign technician')
    expect(rowNames(wrapper).slice(0, 2)).toEqual(['Aisha Karim', 'Priya Natarajan'])
    expect(document.activeElement).toBe(radio(wrapper, 'Aisha Karim').element)
  })

  it('refetches a job someone else changed, explains it in the panel above the button and keeps the choice', async () => {
    const changed = assignedTo(lenaTechnician, { version: 48102 })
    const lenaWithTheJob = technician({ ...lenaTechnician, openJobCount: 2 })
    const { wrapper, stub } = await openJob(job, {
      [jobRoute]: [json(job), json(changed)],
      [techniciansRoute]: [
        json({ items: demoTechnicians }),
        json({
          items: demoTechnicians.map((item) =>
            item.id === lenaWithTheJob.id ? lenaWithTheJob : item,
          ),
        }),
      ],
      [assignRoute]: [
        problem(409, 'concurrency.conflict'),
        json(assignedTo(priyaTechnician, { version: 48103 })),
      ],
    })

    await choose(wrapper, 'Priya Natarajan')
    assignButton(wrapper).element.focus()
    await pressAssign(wrapper)

    expect(stub.requestsTo(jobRoute)).toHaveLength(2)
    expect(stub.requestsTo(techniciansRoute)).toHaveLength(2)
    expect(wrapper.find('main .page-header + .banner').exists()).toBe(false)
    expect(wrapper.findAll('.banner')).toHaveLength(1)
    const notice = panel(wrapper).find('.banner')
    expect(notice.classes()).toContain('banner--warning')
    expect(notice.attributes('role')).toBe('alert')
    expect(notice.text()).toBe(
      'JOB-1051 changed after you opened it. The latest version is shown. Check it, then try again.',
    )
    expect(notice.find('.banner__reference').exists()).toBe(false)
    expect(notice.element.parentElement?.classList.contains('assign-panel')).toBe(true)
    expect(notice.element.nextElementSibling).toBe(assignButton(wrapper).element)
    expect(row(wrapper, 'Lena Park').find('.assign-row__detail').text()).toBe(
      'Fire safety · 2 open jobs',
    )
    expect(panel(wrapper).find('h2').text()).toBe('Reassign technician')
    expect(radio(wrapper, 'Priya Natarajan').element.checked).toBe(true)
    expect(assignButton(wrapper).text()).toBe('Reassign to Priya Natarajan')
    expect(helper(wrapper).text()).toBe(
      'Priya Natarajan will see this job in the technician app straight away. Lena Park will no longer see it.',
    )
    expect(document.activeElement).toBe(assignButton(wrapper).element)

    await pressAssign(wrapper)
    expect(bodiesOf(stub)).toEqual([
      { technicianId: priya.id, version: 48101 },
      { technicianId: priya.id, version: 48102 },
    ])
    expect(wrapper.find('.banner').exists()).toBe(false)
  })

  it('keeps a 16 pixel scroll margin around the assign button, so its focus ring stays on screen', async () => {
    const { wrapper } = await openJob(job)

    expect(assignButton(wrapper).classes()).toContain('assign-panel__action')
    expect(ruleDeclarations(cardFile, '.assign-panel__action')).toEqual({
      'scroll-margin-block': 'var(--spacing-fs-16)',
    })
    expect(readTokens().get('--spacing-fs-16')).toBe('1rem')
  })

  it('scrolls the focused button into view when a banner above it pushes it down', async () => {
    const scrolled: { element: Element; options: unknown }[] = []
    HTMLElement.prototype.scrollIntoView = function (this: Element, options?: unknown) {
      scrolled.push({ element: this, options })
    }
    try {
      const { wrapper } = await openJob(job, {
        [jobRoute]: [json(job), json(assignedTo(lenaTechnician, { version: 48102 }))],
        [assignRoute]: [problem(409, 'concurrency.conflict'), problem(500, 'server.error')],
      })

      await choose(wrapper, 'Priya Natarajan')
      assignButton(wrapper).element.focus()
      await pressAssign(wrapper)

      expect(panel(wrapper).find('.banner--warning').exists()).toBe(true)
      expect(scrolled).toEqual([
        { element: assignButton(wrapper).element, options: { block: 'nearest' } },
      ])

      wrapper.find<HTMLElement>('h1').element.focus()
      await pressAssign(wrapper)

      expect(panel(wrapper).find('.banner--error').exists()).toBe(true)
      expect(scrolled).toHaveLength(1)
    } finally {
      Reflect.deleteProperty(HTMLElement.prototype, 'scrollIntoView')
    }
  })

  it('shows the conflict at the top of the page and focuses the title when the refetched job cannot be assigned', async () => {
    const started = jobDetails({
      status: 'InProgress',
      assignedTechnician: { id: priya.id, displayName: priya.displayName, trade: 'Electrical' },
      assignedAt: onDemoDay(0, 10, 30),
      version: 48105,
      affordances: readOnlyAffordances(),
    })
    const { wrapper, stub } = await openJob(job, {
      [jobRoute]: [json(job), json(started)],
      [assignRoute]: problem(409, 'concurrency.conflict'),
    })

    await choose(wrapper, 'Aisha Karim')
    assignButton(wrapper).element.focus()
    expect(document.activeElement).toBe(assignButton(wrapper).element)
    await pressAssign(wrapper)

    expect(panel(wrapper).exists()).toBe(false)
    expect(stub.requestsTo(techniciansRoute)).toHaveLength(1)
    const notice = wrapper.find('main .page-header + .banner')
    expect(notice.attributes('role')).toBe('alert')
    expect(notice.text()).toBe(
      'JOB-1051 changed after you opened it. The latest version is shown. Check it, then try again.',
    )
    expect(document.activeElement).toBe(wrapper.find('h1').element)
  })

  it('refetches a job that can no longer be assigned, removes the panel and focuses the title', async () => {
    const started = jobDetails({
      status: 'InProgress',
      assignedTechnician: { id: priya.id, displayName: priya.displayName, trade: 'Electrical' },
      assignedAt: onDemoDay(0, 10, 30),
      version: 48105,
      affordances: readOnlyAffordances(),
    })
    const { wrapper, stub } = await openJob(job, {
      [jobRoute]: [json(job), json(started)],
      [assignRoute]: problem(409, 'job.not-assignable'),
    })

    await choose(wrapper, 'Aisha Karim')
    assignButton(wrapper).element.focus()
    expect(document.activeElement).toBe(assignButton(wrapper).element)
    await pressAssign(wrapper)

    expect(stub.requestsTo(jobRoute)).toHaveLength(2)
    expect(panel(wrapper).exists()).toBe(false)
    const notice = wrapper.find('main .page-header + .banner')
    expect(notice.attributes('role')).toBe('alert')
    expect(notice.text()).toBe(
      'JOB-1051 can no longer be assigned. Its status changed after you opened it. The latest version is shown.',
    )
    expect(wrapper.find('.page-header__meta .status-chip').text()).toBe('In progress')
    expect(document.activeElement).toBe(wrapper.find('h1').element)
  })

  it('refreshes the list and marks the group when the server does not know the technician', async () => {
    const { wrapper, stub } = await openJob(job, {
      [assignRoute]: problem(400, 'request.validation-failed', {
        errors: { technicianId: ['Choose a technician who exists.'] },
      }),
    })

    await choose(wrapper, 'Lena Park')
    await pressAssign(wrapper)

    const group = panel(wrapper).find('fieldset')
    const errorId = group.attributes('aria-describedby') ?? ''
    expect(stub.requestsTo(techniciansRoute)).toHaveLength(2)
    expect(errorId).not.toBe('')
    expect(panel(wrapper).find(`[id="${errorId}"]`).text()).toBe(
      'Choose a technician from the list.',
    )
    expect(group.element.nextElementSibling?.id).toBe(errorId)

    await choose(wrapper, 'Aisha Karim')
    expect(panel(wrapper).find('fieldset').attributes('aria-describedby')).toBeUndefined()
  })

  it('moves focus to the first enabled row after the refetch, so the group is read with its error', async () => {
    const refetch = deferred()
    const { wrapper, stub } = await openJob(job, {
      [assignRoute]: problem(400, 'request.validation-failed', {
        errors: { technicianId: ['Choose a technician who exists.'] },
      }),
      [techniciansRoute]: [json({ items: demoTechnicians }), refetch.reply],
    })

    await choose(wrapper, 'Lena Park')
    assignButton(wrapper).element.focus()
    await pressAssign(wrapper)

    expect(stub.requestsTo(techniciansRoute)).toHaveLength(2)
    expect(document.activeElement).toBe(assignButton(wrapper).element)

    const withoutLena = demoTechnicians.filter((item) => item.id !== lenaTechnician.id)
    refetch.resolve(new Response(JSON.stringify({ items: withoutLena }), { status: 200 }))
    await flushPromises()

    const group = panel(wrapper).find('fieldset')
    const error = panel(wrapper).find(`[id="${group.attributes('aria-describedby') ?? ''}"]`)
    expect(error.text()).toBe('Choose a technician from the list.')
    expect(error.attributes('role')).toBeUndefined()
    expect(error.attributes('aria-live')).toBeUndefined()
    expect(group.attributes('inert')).toBeUndefined()
    expect(panel(wrapper).findAll('input[type="radio"]:checked')).toHaveLength(0)
    expect(document.activeElement).toBe(radio(wrapper, 'Priya Natarajan').element)
    expect(group.element.contains(document.activeElement)).toBe(true)
  })

  it('moves focus to Try again when the refetch after the server refused the technician fails', async () => {
    const { wrapper } = await openJob(job, {
      [assignRoute]: problem(400, 'request.validation-failed', {
        errors: { technicianId: ['Choose a technician who exists.'] },
      }),
      [techniciansRoute]: [json({ items: demoTechnicians }), problem(500, 'server.error')],
    })

    await choose(wrapper, 'Lena Park')
    await pressAssign(wrapper)

    const banner = panel(wrapper).find('.banner--error')
    expect(banner.text()).toContain('Technicians could not be loaded.')
    expect(document.activeElement).toBe(banner.find('button').element)
  })

  it.each<[string, Reply, string, string | null]>([
    [
      '403',
      problem(403, 'authz.forbidden', { correlationId: reference }),
      'Your account cannot assign jobs.',
      `Reference ${reference}`,
    ],
    [
      '429',
      problem(429, 'rate-limit.exceeded', {}, { 'Retry-After': '42' }),
      'Too many requests. Try again in 42 seconds.',
      null,
    ],
    [
      'no response',
      networkFailure(),
      'FieldService could not be reached. Check your connection, then try again.',
      null,
    ],
    [
      '500',
      problem(500, 'server.error', { correlationId: reference }),
      'The job could not be assigned. Try again in a moment.',
      `Reference ${reference}`,
    ],
  ])('explains a %s failure above the button', async (_name, reply, copy, referenceLine) => {
    const { wrapper } = await openJob(job, { [assignRoute]: reply })

    await choose(wrapper, 'Priya Natarajan')
    await pressAssign(wrapper)

    const banner = panel(wrapper).find('.banner--error')
    const line = banner.find('.banner__reference')
    expect(banner.attributes('role')).toBe('alert')
    expect(banner.find('.banner__text').text()).toBe(copy)
    expect(line.exists() ? line.text() : null).toBe(referenceLine)
    expect(banner.element.nextElementSibling).toBe(assignButton(wrapper).element)
    expect(assignButton(wrapper).text()).toBe('Assign to Priya Natarajan')
  })
})
