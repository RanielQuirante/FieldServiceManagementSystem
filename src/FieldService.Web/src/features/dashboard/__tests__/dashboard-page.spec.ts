import { flushPromises, mount, type DOMWrapper, type VueWrapper } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory } from 'vue-router'

import App from '@/app/App.vue'
import { createAppRouter, installNavigationGuards } from '@/app/router'
import { ruleDeclarations } from '@/app/styles/__tests__/stylesheet-source'
import { sessionResponse } from '@/features/auth/__tests__/session-fixtures'
import {
  dana,
  demoTechnicians,
  demoToday,
  onDemoDay,
  technician,
} from '@/features/jobs/__tests__/job-fixtures'
import {
  jobEvent,
  refreshRoute,
  ticketReply,
  ticketRoute,
} from '@/features/realtime/__tests__/realtime-fixtures'
import { listDebounceMilliseconds } from '@/features/realtime/live-refetch'
import { lastFakeSocket } from '@/shared/testing/fake-socket'
import {
  deferred,
  json,
  problem,
  stubFetch,
  type FetchStub,
  type RouteTable,
} from '@/shared/testing/fetch-stub'

import type { DashboardOpenJobs, DashboardRange, DashboardUnassignedJob } from '../dashboard-api'
import {
  activitySentence,
  assignedSubline,
  completedSubline,
  greetingFor,
  greetingText,
  heroSentence,
  inProgressSubline,
  jobsByStatusSubline,
  nextGreetingChange,
  onHoldSubline,
  scheduleLine,
  scheduleParts,
  statusTiles,
  unassignedSubline,
  viewAllLabel,
} from '../dashboard-copy'
import { newActivityMilliseconds } from '../dashboard-store'
import {
  activityEntry,
  demoActivity,
  demoNeedsTechnician,
  demoSummary,
  startedEntry,
  withNewActivity,
} from './dashboard-fixtures'

const summaryRoute = 'GET /api/dashboard/summary'

const techniciansRoute = 'GET /api/technicians'

const pageFile = 'features/dashboard/components'

const noBreak = ' '

let mounted: VueWrapper | null = null

afterEach(() => {
  mounted?.unmount()
  mounted = null
})

function plain(text: string | null | undefined): string {
  return (text ?? '').split(noBreak).join(' ').replace(/\s+/g, ' ').trim()
}

function shown(node: DOMWrapper<Element>): string {
  const copy = node.element.cloneNode(true) as Element
  copy.querySelectorAll('.visually-hidden').forEach((hidden) => {
    hidden.remove()
  })
  const walker = document.createTreeWalker(copy, NodeFilter.SHOW_TEXT)
  const parts: string[] = []
  for (let current = walker.nextNode(); current !== null; current = walker.nextNode()) {
    parts.push(current.textContent ?? '')
  }
  return plain(parts.join(' '))
}

function scheduledJob(start: string, end: string, number = 1053): DashboardUnassignedJob {
  const [, , later] = demoNeedsTechnician
  if (later === undefined) {
    throw new Error('Missing fixture')
  }
  return {
    ...later,
    id: `01a15424-cba0-7324-8dfe-00000000${number}`,
    number,
    scheduledStart: start,
    scheduledEnd: end,
  }
}

function openJobs(overrides: Partial<DashboardOpenJobs> = {}): DashboardOpenJobs {
  return { ...demoSummary().openJobs, ...overrides }
}

function range(overrides: Partial<DashboardRange> = {}): DashboardRange {
  return { ...demoSummary().range, ...overrides }
}

async function openDashboard(
  routes: RouteTable = {},
): Promise<{ readonly wrapper: VueWrapper; readonly stub: FetchStub }> {
  const stub = stubFetch({
    [refreshRoute]: json(sessionResponse()),
    [ticketRoute]: ticketReply(),
    [summaryRoute]: json(demoSummary()),
    [techniciansRoute]: json({ items: demoTechnicians }),
    ...routes,
  })
  const router = createAppRouter(createMemoryHistory())
  installNavigationGuards(router)
  await router.push('/')
  const wrapper = mount(App, { global: { plugins: [router] }, attachTo: document.body })
  mounted = wrapper
  await flushPromises()
  return { wrapper, stub }
}

function useDemoClock(): void {
  vi.useFakeTimers({ toFake: ['Date'] })
  vi.setSystemTime(demoToday)
}

describe('the dashboard copy', () => {
  it.each([
    [4, 59, 'Good evening'],
    [5, 0, 'Good morning'],
    [11, 59, 'Good morning'],
    [12, 0, 'Good afternoon'],
    [17, 59, 'Good afternoon'],
    [18, 0, 'Good evening'],
    [23, 30, 'Good evening'],
  ] as const)('greets at %i:%i with %s', (hour, minute, greeting) => {
    expect(greetingFor(new Date(2026, 9, 19, hour, minute))).toBe(greeting)
  })

  it('greets with the full display name, never a part of it', () => {
    expect(greetingText(demoToday, 'Dana Whitfield')).toBe('Good morning, Dana Whitfield')
    expect(greetingText(demoToday, 'Mary Ann de la Cruz')).toBe('Good morning, Mary Ann de la Cruz')
    expect(greetingText(demoToday, null)).toBe('Good morning')
  })

  it('changes the greeting at the next of 5:00, 12:00 and 18:00', () => {
    const at = (day: number, hour: number, minute = 0): Date => new Date(2026, 9, day, hour, minute)

    expect(nextGreetingChange(at(19, 4, 59))).toEqual(at(19, 5))
    expect(nextGreetingChange(at(19, 10, 42))).toEqual(at(19, 12))
    expect(nextGreetingChange(at(19, 12))).toEqual(at(19, 18))
    expect(nextGreetingChange(at(19, 18))).toEqual(at(20, 5))
    expect(nextGreetingChange(at(19, 23, 59))).toEqual(at(20, 5))
  })

  it.each([
    ['T = 0', { total: 0, unassigned: 0 }, 0, 'No open jobs.'],
    ['U = 0', { total: 17, unassigned: 0 }, 0, '17 open jobs. Every one has a technician.'],
    ['T = 1, U = 0', { total: 1, unassigned: 0 }, 0, '1 open job. It has a technician.'],
    ['U = 1, S = 1', { total: 17, unassigned: 1 }, 1, '17 open jobs. 1 needs a technician, today.'],
    ['U = 1, S = 0', { total: 17, unassigned: 1 }, 0, '17 open jobs. 1 needs a technician.'],
    ['T = 1, U = 1', { total: 1, unassigned: 1 }, 1, '1 open job. 1 needs a technician, today.'],
    ['U ≥ 2, S = 0', { total: 17, unassigned: 4 }, 0, '17 open jobs. 4 need a technician.'],
    [
      'U ≥ 2, S between',
      { total: 17, unassigned: 4 },
      2,
      '17 open jobs. 4 need a technician, 2 of them today.',
    ],
    [
      'U ≥ 2, S = U',
      { total: 17, unassigned: 4 },
      4,
      '17 open jobs. 4 need a technician, all of them today.',
    ],
  ] as const)('writes the hero sentence for %s', (_case, figures, scheduled, sentence) => {
    expect(heroSentence(openJobs(figures), range({ unassignedScheduled: scheduled }))).toBe(
      sentence,
    )
  })

  it('leaves out the Unassigned subline at 0 and reads None scheduled today with jobs but none today', () => {
    expect(unassignedSubline(openJobs({ unassigned: 0 }), range({ unassignedScheduled: 0 }))).toBe(
      null,
    )
    expect(unassignedSubline(openJobs({ unassigned: 3 }), range({ unassignedScheduled: 0 }))).toBe(
      'None scheduled today',
    )
    expect(unassignedSubline(openJobs({ unassigned: 4 }), range({ unassignedScheduled: 2 }))).toBe(
      '2 scheduled today',
    )
  })

  it('names the next start by time today, by date and time on a later day, and leaves it out when null', () => {
    expect(assignedSubline(openJobs({ nextAssignedStart: null }), demoToday)).toBeNull()
    expect(assignedSubline(openJobs(), demoToday)).toBe(`Next starts 11:30${noBreak}AM`)
    expect(assignedSubline(openJobs({ nextAssignedStart: onDemoDay(1, 8, 0) }), demoToday)).toBe(
      `Next starts Tue${noBreak}20${noBreak}Oct, 8:00${noBreak}AM`,
    )
  })

  it('counts In progress technicians in the singular for one and leaves the subline out at 0', () => {
    expect(inProgressSubline(openJobs({ inProgressTechnicianCount: 0 }))).toBeNull()
    expect(inProgressSubline(openJobs({ inProgressTechnicianCount: 1 }))).toBe(
      'Across 1 technician',
    )
    expect(inProgressSubline(openJobs({ inProgressTechnicianCount: 2 }))).toBe(
      'Across 2 technicians',
    )
  })

  it('dates the oldest hold by time today, weekday and time within six days, short date before that', () => {
    const since = (day: number, hour: number, minute: number): string | null =>
      onHoldSubline(openJobs({ onHoldSince: onDemoDay(day, hour, minute) }), demoToday)

    expect(onHoldSubline(openJobs({ onHoldSince: null }), demoToday)).toBeNull()
    expect(since(0, 9, 5)).toBe(`Oldest since 9:05${noBreak}AM`)
    expect(since(-3, 13, 50)).toBe(`Oldest since Fri 1:50${noBreak}PM`)
    expect(since(-6, 13, 50)).toBe(`Oldest since Tue 1:50${noBreak}PM`)
    expect(since(-7, 13, 50)).toBe(`Oldest since Mon${noBreak}12${noBreak}Oct`)
  })

  it('reads the last completion as a time, leaves it out when null, and always reads Today for cancellations', () => {
    expect(completedSubline(range({ lastCompletedAt: null }))).toBeNull()
    expect(completedSubline(range())).toBe(`Last at 9:58${noBreak}AM`)
    const tiles = statusTiles(
      demoSummary({ range: range({ cancelled: 0, completed: 0, lastCompletedAt: null }) }),
      demoToday,
    )
    expect(tiles.map((tile) => [tile.label, tile.value, tile.subline])).toEqual([
      ['Unassigned', 4, '2 scheduled today'],
      ['Assigned', 8, `Next starts 11:30${noBreak}AM`],
      ['In progress', 2, 'Across 2 technicians'],
      ['On hold', 3, `Oldest since Fri 1:50${noBreak}PM`],
      ['Completed today', 0, null],
      ['Cancelled', 0, 'Today'],
    ])
  })

  it('writes the Jobs by status subline from the open total and today’s completions and cancellations', () => {
    expect(jobsByStatusSubline(demoSummary())).toBe('17 open, 2 completed and 0 cancelled today')
  })

  it.each([
    ['Created', null, null, 'Dana Whitfield created JOB-1049'],
    [
      'Assigned',
      { id: 'm', displayName: 'Marco Reyes' },
      null,
      'Dana Whitfield assigned JOB-1049 to Marco Reyes',
    ],
    ['Assigned', null, null, 'Dana Whitfield assigned JOB-1049'],
    ['Started', null, null, 'Dana Whitfield started JOB-1049'],
    ['PutOnHold', null, 'Waiting', 'Dana Whitfield put JOB-1049 on hold'],
    ['Resumed', null, null, 'Dana Whitfield resumed JOB-1049'],
    ['Completed', null, null, 'Dana Whitfield completed JOB-1049'],
    ['Cancelled', null, 'Rebooked', 'Dana Whitfield cancelled JOB-1049'],
  ] as const)('writes the %s activity sentence', (action, assignee, note, sentence) => {
    const parts = activitySentence(
      activityEntry({ action, actor: dana, technician: assignee, note }),
    )

    expect(`${parts.actor}${parts.before}JOB-1049${parts.after}`).toBe(sentence)
  })

  it('writes a schedule as Today with the window, or the short date and the window, unbroken inside', () => {
    const [today, , later] = demoNeedsTechnician
    if (today === undefined || later === undefined) {
      throw new Error('Missing fixture')
    }

    expect(scheduleLine(today, demoToday)).toBe(`Today 2:00–3:30${noBreak}PM`)
    expect(scheduleLine(later, demoToday)).toBe(
      `Wed${noBreak}21${noBreak}Oct, 9:00–11:00${noBreak}AM`,
    )
  })

  it('splits a schedule today into the day and the window, each unbroken', () => {
    const [today] = demoNeedsTechnician
    if (today === undefined) {
      throw new Error('Missing fixture')
    }

    expect(scheduleParts(today, demoToday)).toEqual({
      day: 'Today',
      window: `2:00–3:30${noBreak}PM`,
    })
  })

  it('splits a schedule on another day into the short date with its comma and the window, each unbroken', () => {
    const [, , later, last] = demoNeedsTechnician
    if (later === undefined || last === undefined) {
      throw new Error('Missing fixture')
    }

    expect(scheduleParts(later, demoToday)).toEqual({
      day: `Wed${noBreak}21${noBreak}Oct,`,
      window: `9:00–11:00${noBreak}AM`,
    })
    expect(scheduleParts(last, demoToday)).toEqual({
      day: `Thu${noBreak}22${noBreak}Oct,`,
      window: `10:00${noBreak}AM–12:00${noBreak}PM`,
    })
  })

  it('splits a window that crosses midnight after its day, keeping the end day and time whole in the window', () => {
    const later = scheduledJob(onDemoDay(7, 22, 0), onDemoDay(8, 2, 0))
    const tonight = scheduledJob(onDemoDay(0, 22, 0), onDemoDay(1, 2, 0))
    const parts = scheduleParts(later, demoToday)

    expect(parts).toEqual({
      day: `Mon${noBreak}26${noBreak}Oct,`,
      window: `10:00${noBreak}PM–Tue${noBreak}27${noBreak}Oct,${noBreak}2:00${noBreak}AM`,
    })
    expect(scheduleParts(tonight, demoToday)).toEqual({
      day: 'Today',
      window: `10:00${noBreak}PM–Tue${noBreak}20${noBreak}Oct,${noBreak}2:00${noBreak}AM`,
    })
    expect([parts.day, parts.window].some((part) => part.includes(' '))).toBe(false)
    expect(plain(scheduleLine(later, demoToday))).toBe('Mon 26 Oct, 10:00 PM–Tue 27 Oct, 2:00 AM')
    expect(scheduleLine(later, demoToday)).toBe(`${parts.day} ${parts.window}`)
  })

  it('names the View all link by the unassigned total', () => {
    expect(viewAllLabel(14)).toBe('View all 14 unassigned jobs')
  })
})

describe('the dashboard page', () => {
  it('is the landing page: the greeting is the focused h1, under the long date, with the sentence and two hero tiles', async () => {
    useDemoClock()
    const { wrapper } = await openDashboard()
    const hero = wrapper.find('section.hero-band')

    expect(document.title).toBe('Dashboard · FieldService Admin')
    expect(hero.attributes('data-surface')).toBe('dark')
    expect(hero.attributes('aria-labelledby')).toBe(hero.find('h1').attributes('id'))
    expect(hero.find('.hero-band__eyebrow').text()).toBe('Monday 19 October 2026')
    expect(hero.find('h1').text()).toBe('Good morning, Dana Whitfield')
    expect(document.activeElement).toBe(hero.find('h1').element)
    expect(hero.find('.hero-band__sentence').text()).toBe(
      '17 open jobs. 4 need a technician, 2 of them today.',
    )
    expect(
      hero.findAll('.hero-band__tile').map((tile) =>
        tile
          .findAll('span')
          .map((part) => part.text())
          .join(' '),
      ),
    ).toEqual(['5 of 6 technicians on shift', '13 open jobs assigned'])
    expect(wrapper.findAll('main h1')).toHaveLength(1)
  })

  it('shows six status tiles as links named by label, value and subline, each opening its status list', async () => {
    useDemoClock()
    const { wrapper } = await openDashboard()
    const tiles = wrapper.findAll('.status-grid__tiles > a.kpi-tile')

    expect(shown(wrapper.find('.status-grid__subline'))).toBe(
      '17 open, 2 completed and 0 cancelled today',
    )
    expect(wrapper.find('.status-grid h2').attributes('tabindex')).toBe('-1')
    expect(tiles.map((tile) => plain(tile.attributes('aria-label')))).toEqual([
      'Unassigned, 4, 2 scheduled today',
      'Assigned, 8, Next starts 11:30 AM',
      'In progress, 2, Across 2 technicians',
      'On hold, 3, Oldest since Fri 1:50 PM',
      'Completed today, 2, Last at 9:58 AM',
      'Cancelled, 0, Today',
    ])
    expect(tiles.map((tile) => tile.attributes('href'))).toEqual([
      '/jobs?status=Unassigned',
      '/jobs?status=Assigned',
      '/jobs?status=InProgress',
      '/jobs?status=OnHold',
      '/jobs?status=Completed',
      '/jobs?status=Cancelled',
    ])
    expect(
      tiles.map((tile) => tile.classes().find((name) => name.startsWith('kpi-tile--'))),
    ).toEqual([
      'kpi-tile--unassigned',
      'kpi-tile--assigned',
      'kpi-tile--in-progress',
      'kpi-tile--on-hold',
      'kpi-tile--completed',
      'kpi-tile--cancelled',
    ])
  })

  it('lists the jobs that need a technician with Assign links named by job number and the capsule in words', async () => {
    useDemoClock()
    const { wrapper } = await openDashboard()
    const card = wrapper.find('.needs-technician')
    const rows = card.findAll('.needs-technician__row')

    expect(card.find('h2').text()).toBe('Needs a technician')
    expect(card.find('.count-capsule').text()).toBe('4 unassigned jobs')
    expect(shown(card.find('.count-capsule'))).toBe('4')
    expect(rows.map((row) => shown(row.find('.needs-technician__text')))).toEqual([
      'JOB-1052 Urgent Fire alarm panel trouble signal Cedar Ridge Elementary School · Today 2:00–3:30 PM',
      'JOB-1051 High Replace faulty breaker panel Pinecrest Public Library · Today 3:30–5:00 PM',
      'JOB-1050 Low Emergency lighting test Northgate Logistics Warehouse · Wed 21 Oct, 9:00–11:00 AM',
      'JOB-1054 Normal Leaking rooftop drain Harborview Apartments · Thu 22 Oct, 10:00 AM–12:00 PM',
    ])
    expect(
      rows.map(
        (row) => row.find('.needs-technician__meta > [aria-hidden="true"]').element.textContent,
      ),
    ).toEqual(rows.map(() => '\u{00a0}\u{00b7} '))
    expect(rows.map((row) => row.find('a').attributes('aria-label'))).toEqual([
      'Assign JOB-1052',
      'Assign JOB-1051',
      'Assign JOB-1050',
      'Assign JOB-1054',
    ])
    expect(rows[0]?.find('a').attributes('href')).toBe(`/jobs/${demoNeedsTechnician[0]?.id}`)
    expect(card.find('.needs-technician__more').exists()).toBe(false)
  })

  it('writes each schedule as one block of two unbreakable parts, the day and the window, with an ordinary space between', async () => {
    useDemoClock()
    const crossing = scheduledJob(onDemoDay(7, 22, 0), onDemoDay(8, 2, 0))
    const { wrapper } = await openDashboard({
      [summaryRoute]: json(
        demoSummary({
          openJobs: openJobs({ unassigned: 5 }),
          needsTechnician: [...demoNeedsTechnician, crossing],
        }),
      ),
    })
    const schedules = wrapper.findAll('.needs-technician__row .needs-technician__schedule')

    expect(
      schedules.map((schedule) =>
        Array.from(schedule.element.childNodes, (node) =>
          node instanceof Element
            ? `${node.className}: ${plain(node.textContent)}`
            : JSON.stringify(node.textContent),
        ),
      ),
    ).toEqual([
      [
        'needs-technician__schedule-part: Today',
        '" "',
        'needs-technician__schedule-part: 2:00–3:30 PM',
      ],
      [
        'needs-technician__schedule-part: Today',
        '" "',
        'needs-technician__schedule-part: 3:30–5:00 PM',
      ],
      [
        'needs-technician__schedule-part: Wed 21 Oct,',
        '" "',
        'needs-technician__schedule-part: 9:00–11:00 AM',
      ],
      [
        'needs-technician__schedule-part: Thu 22 Oct,',
        '" "',
        'needs-technician__schedule-part: 10:00 AM–12:00 PM',
      ],
      [
        'needs-technician__schedule-part: Mon 26 Oct,',
        '" "',
        'needs-technician__schedule-part: 10:00 PM–Tue 27 Oct, 2:00 AM',
      ],
    ])
    expect(schedules.map((schedule) => schedule.text())).toEqual(
      [...demoNeedsTechnician, crossing].map((job) => scheduleLine(job, demoToday)),
    )
    expect(
      schedules.every((schedule) =>
        schedule
          .findAll('.needs-technician__schedule-part')
          .every((part) => !part.text().includes(' ')),
      ),
    ).toBe(true)
  })

  it('offers View all when more jobs need a technician than the list holds', async () => {
    useDemoClock()
    const many = Array.from({ length: 10 }, (_, index) => ({
      ...demoNeedsTechnician[0],
      id: `01a15424-cba0-7324-8dfe-0000000000${String(index).padStart(2, '0')}`,
      number: 1060 + index,
    })) as typeof demoNeedsTechnician
    const { wrapper } = await openDashboard({
      [summaryRoute]: json(
        demoSummary({
          openJobs: openJobs({ unassigned: 14 }),
          needsTechnician: [...many],
        }),
      ),
    })
    const more = wrapper.find('.needs-technician__more a')

    expect(wrapper.findAll('.needs-technician__row')).toHaveLength(10)
    expect(more.text()).toBe('View all 14 unassigned jobs')
    expect(more.attributes('href')).toBe('/jobs?status=Unassigned')
    expect(more.findAll('svg')).toHaveLength(1)
    expect(more.element.lastElementChild?.tagName.toLowerCase()).toBe('svg')
  })

  it('renders every activity entry the summary returns, in its order, with job links and notes in curly quotes', async () => {
    useDemoClock()
    const { wrapper } = await openDashboard()
    const rows = wrapper.findAll('.live-activity__row[data-entry-id]')

    expect(wrapper.find('.live-activity ol').exists()).toBe(true)
    expect(rows.map((row) => shown(row.find('.live-activity__sentence')))).toEqual([
      'Priya Natarajan started JOB-1049',
      'Tom Becker put JOB-1044 on hold',
      'Dana Whitfield created JOB-1051',
      'Dana Whitfield assigned JOB-1047 to Marco Reyes',
      'Aisha Karim completed JOB-1043',
      'Marco Reyes started JOB-1042',
    ])
    expect(rows[1]?.find('.live-activity__note').text()).toBe('“Waiting for replacement gas valve”')
    expect(rows[0]?.find('.live-activity__job').attributes('href')).toBe(
      `/jobs/${demoActivity[0]?.jobId}`,
    )
    expect(rows[0]?.find('time').attributes('datetime')).toBe(demoActivity[0]?.occurredAt)
    expect(plain(rows[0]?.find('time').text())).toBe('10:42 AM')
    expect(rows.every((row) => !row.classes('live-activity__row--new'))).toBe(true)
    expect(wrapper.findAll('.live-activity .count-capsule--new')).toHaveLength(0)
  })

  it('shows the workload in name order as a table and a people list built from the same rows', async () => {
    useDemoClock()
    const inactive = technician({
      id: '01a0f6b1-3e00-7000-8000-000000000007',
      displayName: 'Nadia Flores',
      isActive: false,
      isOnShift: true,
      availableForAssignment: false,
      openJobCount: 1,
      currentJob: null,
      nextJob: null,
    })
    const { wrapper } = await openDashboard({
      [techniciansRoute]: json({ items: [...demoTechnicians, inactive] }),
    })
    const card = wrapper.find('.workload')
    const rows = card.findAll('tbody tr')
    const people = card.findAll('ul[aria-label="Technician workload"] > li')

    expect(card.find('[role="region"]').attributes('aria-label')).toBe('Technician workload')
    expect(card.find('caption').text()).toBe('Technician workload')
    expect(card.find('table').classes()).toContain('data-table__table--min-640')
    expect(rows.map((row) => row.findAll('td').map((cell) => shown(cell)))).toEqual([
      ['AK Aisha Karim Electrical', 'On shift', '3', 'Next: JOB-1055 at 1:00 PM'],
      ['LP Lena Park Fire safety', 'On shift', '1', 'Next: JOB-1045 at 11:30 AM'],
      ['MR Marco Reyes HVAC', 'On shift', '3', 'JOB-1042 In progress'],
      ['PN Priya Natarajan Electrical', 'On shift', '2', 'JOB-1049 In progress'],
      ['SO Samuel Osei HVAC', 'Off shift', '0', 'None'],
      ['TB Tom Becker Plumbing', 'On shift', '4', 'JOB-1044 On hold'],
      ['NF Nadia Flores Electrical', 'Inactive', '1', 'None'],
    ])
    expect(people.map((row) => shown(row))).toEqual([
      'AK Aisha Karim Electrical On shift 3 open jobs Next: JOB-1055 at 1:00 PM',
      'LP Lena Park Fire safety On shift 1 open job Next: JOB-1045 at 11:30 AM',
      'MR Marco Reyes HVAC On shift 3 open jobs JOB-1042 In progress',
      'PN Priya Natarajan Electrical On shift 2 open jobs JOB-1049 In progress',
      'SO Samuel Osei HVAC Off shift 0 open jobs None',
      'TB Tom Becker Plumbing On shift 4 open jobs JOB-1044 On hold',
      'NF Nadia Flores Electrical Inactive 1 open job None',
    ])
    expect(rows[2]?.find('a').attributes('href')).toBe(
      `/jobs/${demoTechnicians.find((item) => item.displayName === 'Marco Reyes')?.currentJob?.id}`,
    )
    expect(card.find('.status-chip--off-shift').text()).toContain('Off shift')
  })

  it('puts the avatar first in each workload person’s first line, then the name over the trade, then the chip, in the list and its skeleton rows', async () => {
    useDemoClock()
    const shape = (line: DOMWrapper<Element>): string[][] =>
      Array.from(line.element.children, (child) =>
        child.classList.contains('workload__identity--grow')
          ? Array.from(child.children, (part) => part.classList[1] ?? part.classList[0] ?? '')
          : [child.classList[1] ?? child.classList[0] ?? ''],
      )
    const technicians = deferred()
    const { wrapper } = await openDashboard({ [techniciansRoute]: technicians.reply })

    expect(shape(wrapper.find('.workload__skeletons .workload__first-line'))).toEqual([
      ['workload__block--avatar'],
      ['workload__block--name', 'workload__block--trade'],
      ['workload__block--chip'],
    ])

    technicians.resolve(new Response(JSON.stringify({ items: demoTechnicians }), { status: 200 }))
    await flushPromises()

    const line = wrapper.find('ul[aria-label="Technician workload"] .workload__first-line')
    expect(shape(line)).toEqual([
      ['avatar--32'],
      ['workload__name', 'workload__secondary'],
      ['status-chip--shift'],
    ])
    expect(line.element.firstElementChild?.classList.contains('avatar')).toBe(true)
    expect(line.element.lastElementChild?.classList.contains('workload__shift')).toBe(true)
  })

  it('shows skeletons and no tile links while loading, with one hidden status for the counts and one for the technicians', async () => {
    useDemoClock()
    const summary = deferred()
    const technicians = deferred()
    const { wrapper } = await openDashboard({
      [summaryRoute]: summary.reply,
      [techniciansRoute]: technicians.reply,
    })

    expect(wrapper.find('.hero-band h1').text()).toBe('Good morning, Dana Whitfield')
    expect(wrapper.find('.hero-band__sentence-skeleton').attributes('aria-hidden')).toBe('true')
    expect(wrapper.findAll('.hero-band__value-skeleton')).toHaveLength(2)
    expect(wrapper.findAll('.hero-band__caption').map((caption) => caption.text())).toEqual([
      'technicians on shift',
      'open jobs assigned',
    ])
    expect(wrapper.findAll('.status-grid a')).toHaveLength(0)
    expect(wrapper.findAll('.status-grid .kpi-tile__label').map((label) => label.text())).toEqual([
      'Unassigned',
      'Assigned',
      'In progress',
      'On hold',
      'Completed today',
      'Cancelled',
    ])
    expect(wrapper.find('.status-grid__subline').exists()).toBe(false)
    expect(wrapper.find('.needs-technician .count-capsule').exists()).toBe(false)
    expect(wrapper.findAll('.needs-technician__skeleton')).toHaveLength(3)
    expect(wrapper.findAll('.live-activity__skeletons .live-activity__row')).toHaveLength(4)
    expect(
      wrapper
        .findAll('main [role="status"]')
        .map((status) => status.text())
        .filter((text) => text.length > 0),
    ).toEqual(['Loading job counts', 'Loading technicians'])

    summary.resolve(new Response(JSON.stringify(demoSummary()), { status: 200 }))
    technicians.resolve(new Response(JSON.stringify({ items: demoTechnicians }), { status: 200 }))
    await flushPromises()

    expect(wrapper.findAll('.status-grid a')).toHaveLength(6)
  })

  it('shows one Try again for a failed summary, a line in each summary card, and focuses Jobs by status once it loads', async () => {
    useDemoClock()
    const { wrapper } = await openDashboard({
      [summaryRoute]: [
        problem(500, 'server.error'),
        problem(500, 'server.error'),
        json(demoSummary()),
      ],
    })
    const banner = (): DOMWrapper<Element> => wrapper.find('.status-grid .banner--error')

    expect(plain(banner().find('.banner__text').text())).toBe(
      'Job counts could not be loaded. Check your connection, then try again.',
    )
    expect(banner().attributes('role')).toBe('status')
    expect(banner().find('.banner__reference').text()).toMatch(/^Reference [0-9a-f-]{36}$/)
    expect(
      wrapper.findAll('main button').filter((button) => button.text() === 'Try again'),
    ).toHaveLength(1)
    expect(wrapper.find('.needs-technician__failure').text()).toBe('This list could not be loaded.')
    expect(wrapper.find('.needs-technician h2').text()).toBe('Needs a technician')
    expect(wrapper.find('.live-activity__failure').text()).toBe('Activity could not be loaded.')
    expect(wrapper.find('.hero-band__sentence').exists()).toBe(false)
    expect(wrapper.find('.hero-band__tiles').exists()).toBe(false)
    expect(wrapper.find('.hero-band h1').text()).toBe('Good morning, Dana Whitfield')
    expect(wrapper.find('.workload tbody tr').exists()).toBe(true)

    await banner().find('button').trigger('click')
    await flushPromises()
    expect(banner().attributes('role')).toBe('alert')
    expect(document.activeElement?.textContent?.trim()).toBe('Try again')

    await banner().find('button').trigger('click')
    await flushPromises()
    expect(banner().exists()).toBe(false)
    expect(wrapper.findAll('.status-grid a')).toHaveLength(6)
    expect(document.activeElement).toBe(wrapper.find('.status-grid h2').element)
  })

  it('reads a 429 as a wait without a reference', async () => {
    useDemoClock()
    const { wrapper } = await openDashboard({
      [summaryRoute]: problem(429, 'rate-limit.exceeded', {}, { 'Retry-After': '30' }),
    })
    const banner = wrapper.find('.status-grid .banner--error')

    expect(plain(banner.find('.banner__text').text())).toBe(
      'Job counts could not be loaded. Try again in 30 seconds.',
    )
    expect(banner.find('.banner__reference').exists()).toBe(false)
  })

  it('retries the workload on its own and focuses its region once it loads', async () => {
    useDemoClock()
    const { wrapper, stub } = await openDashboard({
      [techniciansRoute]: [problem(500, 'server.error'), json({ items: demoTechnicians })],
    })
    const banner = wrapper.find('.workload .banner--error')

    expect(plain(banner.find('.banner__text').text())).toBe(
      'Technicians could not be loaded. Check your connection, then try again.',
    )
    expect(wrapper.findAll('.status-grid a')).toHaveLength(6)

    await banner.find('button').trigger('click')
    await flushPromises()

    expect(stub.requestsTo(summaryRoute)).toHaveLength(1)
    expect(wrapper.findAll('.workload tbody tr')).toHaveLength(6)
    expect(document.activeElement?.getAttribute('aria-label')).toBe('Technician workload')
  })

  it('shows the empty forms with no open job, no unassigned job, no activity and no technicians', async () => {
    useDemoClock()
    const { wrapper } = await openDashboard({
      [summaryRoute]: json(
        demoSummary({
          openJobs: {
            total: 0,
            unassigned: 0,
            assigned: 0,
            inProgress: 0,
            onHold: 0,
            withTechnician: 0,
            inProgressTechnicianCount: 0,
            nextAssignedStart: null,
            onHoldSince: null,
          },
          range: range({
            completed: 0,
            cancelled: 0,
            unassignedScheduled: 0,
            lastCompletedAt: null,
          }),
          technicians: { total: 0, onShift: 0 },
          needsTechnician: [],
          recentActivity: [],
        }),
      ),
      [techniciansRoute]: json({ items: [] }),
    })

    expect(wrapper.find('.hero-band__sentence').text()).toBe('No open jobs.')
    expect(
      wrapper.findAll('.status-grid a').map((tile) => plain(tile.attributes('aria-label'))),
    ).toEqual([
      'Unassigned, 0',
      'Assigned, 0',
      'In progress, 0',
      'On hold, 0',
      'Completed today, 0',
      'Cancelled, 0, Today',
    ])
    expect(wrapper.find('.needs-technician h3').text()).toBe('Every open job has a technician')
    expect(wrapper.find('.needs-technician .empty-state__body').text()).toBe(
      'Jobs without a technician appear here, earliest first.',
    )
    expect(wrapper.find('.needs-technician button').exists()).toBe(false)
    expect(wrapper.find('.live-activity h3').text()).toBe('No activity yet today')
    expect(wrapper.findAll('.workload h3').map((heading) => heading.text())).toEqual([
      'No technicians yet',
      'No technicians yet',
    ])
  })

  it('marks an entry an event refetch added as New for 12 seconds of visible time, and nothing on a connect refetch', async () => {
    vi.useFakeTimers({ toFake: ['Date', 'setTimeout', 'clearTimeout'], now: demoToday })
    const reconnected = startedEntry(1046, 43)
    const added = startedEntry(1047, 44)
    const afterConnect = withNewActivity(demoSummary(), [reconnected])
    const { wrapper } = await openDashboard({
      [summaryRoute]: [
        json(demoSummary()),
        json(afterConnect),
        json(withNewActivity(afterConnect, [added])),
      ],
      'GET /api/jobs/01a154a0-0000-7000-9000-000000001047': problem(404, 'job.not-found'),
    })
    const socket = lastFakeSocket()
    await socket.simulateConnect()
    await flushPromises()
    expect(wrapper.find(`[data-entry-id="${reconnected.id}"]`).exists()).toBe(true)
    expect(wrapper.findAll('.live-activity__row--new')).toHaveLength(0)

    socket.emitServerEvent('job.status-changed', jobEvent({ jobId: added.jobId, jobNumber: 1047 }))
    await vi.advanceTimersByTimeAsync(listDebounceMilliseconds)
    await flushPromises()

    const fresh = wrapper.findAll('.live-activity__row--new')
    expect(fresh).toHaveLength(1)
    expect(shown(fresh[0]?.find('.live-activity__sentence') as DOMWrapper<Element>)).toBe(
      'Marco Reyes started JOB-1047',
    )
    expect(fresh[0]?.find('.live-activity__meta .count-capsule--new').text()).toBe('New')
    expect(wrapper.findAll('.live-activity__row[data-entry-id]')).toHaveLength(6)

    await vi.advanceTimersByTimeAsync(newActivityMilliseconds)
    await flushPromises()
    expect(wrapper.findAll('.live-activity__row--new')).toHaveLength(0)
    expect(wrapper.findAll('.live-activity .count-capsule--new')).toHaveLength(0)
  })

  it('follows the clock: the greeting turns to Good afternoon at noon while the page is open', async () => {
    vi.useFakeTimers({ toFake: ['Date', 'setTimeout', 'clearTimeout'], now: demoToday })
    const { wrapper } = await openDashboard()

    expect(wrapper.find('.hero-band h1').text()).toBe('Good morning, Dana Whitfield')
    await vi.advanceTimersByTimeAsync(78 * 60_000)

    expect(wrapper.find('.hero-band h1').text()).toBe('Good afternoon, Dana Whitfield')
  })
})

describe('the dashboard layout rules', () => {
  it('lays the six tiles out in 6, 3 or 2 columns by the grid’s own width', () => {
    const grid = `${pageFile}/StatusKpiGrid.vue`

    expect(ruleDeclarations(grid, '.status-grid__frame')).toEqual({
      'container-type': 'inline-size',
    })
    expect(ruleDeclarations(grid, '.status-grid__tiles')).toMatchObject({
      display: 'grid',
      'grid-template-columns': 'repeat(2, minmax(0, 1fr))',
    })
    expect(ruleDeclarations(grid, '.status-grid__tiles', '@container (width >= 528px)')).toEqual({
      'grid-template-columns': 'repeat(3, minmax(0, 1fr))',
    })
    expect(ruleDeclarations(grid, '.status-grid__tiles', '@container (width >= 1072px)')).toEqual({
      'grid-template-columns': 'repeat(6, minmax(0, 1fr))',
    })
  })

  it('turns the hero band compact below 560px of its own width', () => {
    const hero = `${pageFile}/HeroBand.vue`
    const compact = '@container (width < 560px)'

    expect(ruleDeclarations(hero, '.hero-band')).toMatchObject({
      'container-type': 'inline-size',
      background: 'var(--fs-glow-band), var(--fs-gradient-hero)',
    })
    expect(ruleDeclarations(hero, '.hero-band__layout', compact)).toEqual({
      gap: 'var(--spacing-fs-20)',
      padding: 'var(--spacing-fs-24) var(--spacing-fs-20)',
    })
    expect(ruleDeclarations(hero, '.hero-band__title', compact)).toEqual({
      'font-size': '1.75rem',
      'line-height': '1.15',
    })
    expect(ruleDeclarations(hero, '.hero-band__tiles', compact)).toMatchObject({
      display: 'grid',
      'grid-template-columns': 'repeat(2, minmax(0, 1fr))',
    })
    expect(ruleDeclarations(hero, '.hero-band__value-skeleton')).toMatchObject({
      width: '64px',
      height: '28px',
      background: 'var(--fs-glass-g4-control-bg)',
    })
  })

  it('moves the activity chip into the meta row once the card is narrower than 440px', () => {
    const card = `${pageFile}/LiveActivityCard.vue`
    const narrow = '@container (width < 422px)'

    expect(ruleDeclarations(card, '.live-activity__row')).toMatchObject({
      display: 'grid',
      'grid-template-columns': '32px minmax(0, 1fr) auto',
      'align-items': 'start',
    })
    expect(ruleDeclarations(card, '.live-activity__row', narrow)).toEqual({
      'grid-template-columns': '32px minmax(0, 1fr)',
    })
    expect(
      ruleDeclarations(card, '.live-activity__row .live-activity__chip--inline', narrow),
    ).toEqual({ display: 'inline-flex' })
    expect(ruleDeclarations(card, '.live-activity__row--new')).toEqual({
      background: 'var(--fs-flash-row)',
    })
    expect(
      ruleDeclarations(card, '.live-activity__row--new', '@media (forced-colors: active)'),
    ).toEqual({ background: 'none' })
    expect(ruleDeclarations(card, '.live-activity__note')).toMatchObject({
      '-webkit-line-clamp': '2',
    })
  })

  it('keeps a schedule an inline block that breaks only between its day and its window', () => {
    const card = `${pageFile}/NeedsTechnicianCard.vue`

    expect(ruleDeclarations(card, '.needs-technician__schedule')).toEqual({
      display: 'inline-block',
      'max-width': '100%',
    })
    expect(ruleDeclarations(card, '.needs-technician__schedule-part')).toEqual({
      'white-space': 'nowrap',
    })
  })

  it('keeps a job number and a next job on one line where they would break', () => {
    expect(
      ruleDeclarations(`${pageFile}/LiveActivityCard.vue`, '.live-activity__job'),
    ).toMatchObject({ 'white-space': 'nowrap' })
    expect(ruleDeclarations(`${pageFile}/TechnicianWorkloadCard.vue`, '.workload__single')).toEqual(
      { 'white-space': 'nowrap' },
    )
  })

  it('lays the first line of a workload person out as a grid whose trade line runs under the shift chip', () => {
    const workload = `${pageFile}/TechnicianWorkloadCard.vue`
    const firstLine = '.workload__first-line'
    const both = (...selectors: string[]): string =>
      selectors.map((selector) => `${firstLine} ${selector}`).join(',\n')

    expect(ruleDeclarations(workload, firstLine)).toEqual({
      display: 'grid',
      'grid-template-columns': '32px minmax(0, 1fr) auto',
      'column-gap': 'var(--spacing-fs-12)',
      'row-gap': 'var(--spacing-fs-2)',
      'align-items': 'start',
    })
    expect(ruleDeclarations(workload, `${firstLine} > :first-child`)).toEqual({
      'grid-row': '1 / span 2',
    })
    expect(ruleDeclarations(workload, `${firstLine} > .workload__identity--grow`)).toEqual({
      display: 'contents',
    })
    expect(ruleDeclarations(workload, both('.workload__name', '.workload__block--name'))).toEqual({
      'grid-column': '2',
      'grid-row': '1',
    })
    expect(ruleDeclarations(workload, both('.workload__shift', '.workload__block--chip'))).toEqual({
      'grid-column': '3',
      'grid-row': '1',
    })
    expect(
      ruleDeclarations(workload, both('.workload__secondary', '.workload__block--trade')),
    ).toEqual({ 'grid-column': '2 / -1', 'grid-row': '2' })
    expect(ruleDeclarations(workload, '.workload__identity')).toMatchObject({
      display: 'flex',
      'flex-direction': 'column',
    })
  })

  it('places the two summary cards side by side from 440px each', () => {
    expect(
      ruleDeclarations('features/dashboard/pages/DashboardPage.vue', '.dashboard__pair'),
    ).toEqual({
      display: 'grid',
      'grid-template-columns': 'repeat(auto-fit, minmax(min(440px, 100%), 1fr))',
      'align-items': 'start',
      gap: 'var(--spacing-fs-20)',
    })
  })
})
