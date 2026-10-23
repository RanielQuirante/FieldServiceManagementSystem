import { flushPromises, mount, type DOMWrapper, type VueWrapper } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, type Router } from 'vue-router'

import App from '@/app/App.vue'
import { createAppRouter, installNavigationGuards } from '@/app/router'
import { installStyles, ruleDeclarations } from '@/app/styles/__tests__/stylesheet-source'
import { sessionResponse } from '@/features/auth/__tests__/session-fixtures'
import { jobEvent, ticketReply, ticketRoute } from '@/features/realtime/__tests__/realtime-fixtures'
import { listDebounceMilliseconds } from '@/features/realtime/live-refetch'
import { ApiError } from '@/shared/api/api-error'
import { contractProblems } from '@/shared/testing/contract-schema'
import { lastFakeSocket, type FakeSocket } from '@/shared/testing/fake-socket'
import {
  deferred,
  json,
  networkFailure,
  problem,
  stubFetch,
  type FetchStub,
  type RouteTable,
} from '@/shared/testing/fetch-stub'
import AppIcon from '@/shared/ui/AppIcon.vue'
import type { IconName } from '@/shared/ui/icons'

import {
  formatReading,
  formatReadingValue,
  formatTimeOnSite,
  formatTimeRange,
  loadFailureMessage,
  millisecondsToNextMinute,
  reportMeta,
  reportNotice,
  reportRows,
  summaryTiles,
  technicianNotes,
  timeOnSiteBetween,
  type SummaryTile,
} from '../inspection-report'
import type { Inspection, JobDetails } from '../jobs-api'
import {
  answer,
  assignedFreezerJob,
  blankFilterInspection,
  blankFreezerInspection,
  boilerInspection,
  boilerJob,
  boilerJobId,
  boilerNotes,
  cancelledFilterJob,
  completedRooftopJob,
  dischargeField,
  fanMotorNote,
  filterJobId,
  freezerJobId,
  inspection,
  reading,
  readingField,
  rooftopInProgress,
  rooftopItem,
  rooftopItems,
  rooftopNotes,
  rooftopSubmitted,
  suctionField,
} from './inspection-fixtures'
import {
  assignedInProgressJob,
  demoToday,
  historyEntry,
  jobDetails,
  jobList,
  jobSummary,
  onDemoDay,
  tom,
} from './job-fixtures'

const rooftopJobId = '01a15415-3b40-79a4-859f-2cf627f64d02'

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

function jobRoute(jobId: string): string {
  return `GET /api/jobs/${jobId}`
}

function inspectionRoute(jobId: string): string {
  return `GET /api/jobs/${jobId}/inspection`
}

function reportPath(jobId: string): string {
  return `/jobs/${jobId}/inspection`
}

function reportRoutes(job: JobDetails, report: Inspection): RouteTable {
  return { [jobRoute(job.id)]: json(job), [inspectionRoute(job.id)]: json(report) }
}

function jsonResponse(body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}

async function openApp(path: string, routeTable: RouteTable): Promise<OpenApp> {
  const stub = stubFetch({
    'POST /api/auth/refresh': json(sessionResponse()),
    'GET /api/technicians': json({ items: [] }),
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

async function liveApp(
  path: string,
  routeTable: RouteTable,
): Promise<OpenApp & { readonly socket: FakeSocket }> {
  vi.useFakeTimers({ toFake: ['Date', 'setTimeout', 'clearTimeout'], now: demoToday })
  const opened = await openApp(path, { [ticketRoute]: ticketReply(), ...routeTable })
  const socket = lastFakeSocket()
  await socket.simulateConnect()
  await flushPromises()
  return { ...opened, socket }
}

function collapse(text: string | null): string {
  return (text ?? '').replace(/[ \t\r\n]+/g, ' ').trim()
}

function visibleText(element: Element): string {
  const copy = element.cloneNode(true) as Element
  copy.querySelectorAll('.visually-hidden').forEach((hidden) => hidden.remove())
  return collapse(copy.textContent)
}

function spokenText(element: Element): string {
  const copy = element.cloneNode(true) as Element
  copy.querySelectorAll('[aria-hidden="true"]').forEach((hidden) => hidden.remove())
  return collapse(copy.textContent)
}

function iconMarkup(name: IconName, size: 14 | 16 | 18): string {
  return mount(AppIcon, { props: { name, size } }).element.innerHTML
}

interface ShownTile {
  readonly label: string
  readonly value: string
  readonly subline: string
}

function shownTiles(wrapper: VueWrapper): ShownTile[] {
  return wrapper.findAll('.kpi-tile').map((tile) => {
    const chips = tile.findAll('.kpi-tile__content .status-chip')
    const subline = tile.find('.kpi-tile__subline')
    return {
      label: tile.find('.kpi-tile__label').text(),
      value:
        chips.length > 0
          ? chips.map((chip) => chip.text()).join(' ')
          : tile.find('.kpi-tile__value').text(),
      subline: subline.exists() ? visibleText(subline.element) : '',
    }
  })
}

function metaParts(wrapper: VueWrapper): string[] {
  return wrapper.findAll('.page-header__meta > *').map((part) => part.text())
}

function resultsRegion(wrapper: VueWrapper): DOMWrapper<Element> {
  return wrapper.find('[role="region"][aria-label="Inspection results"]')
}

function cellText(cell: DOMWrapper<Element>): string {
  const lines = cell.findAll('.inspection-results__reading')
  return lines.length > 0
    ? lines.map((line) => collapse(line.element.textContent)).join(' / ')
    : visibleText(cell.element)
}

function rowCells(wrapper: VueWrapper): string[][] {
  return resultsRegion(wrapper)
    .findAll('tbody tr')
    .map((row) => row.findAll('td').map(cellText))
}

function noticeOf(wrapper: VueWrapper): DOMWrapper<Element> {
  return wrapper.find('main .page-header + .banner')
}

function detailRows(wrapper: VueWrapper): string[][] {
  return wrapper
    .findAll('.job-details__row')
    .map((row) => [row.find('dt').text(), spokenText(row.find('dd').element)])
}

function historyLines(wrapper: VueWrapper): string[] {
  return wrapper
    .findAll('.status-timeline__entry')
    .map(
      (entry) =>
        `${visibleText(entry.find('time').element)} ${entry.find('.status-timeline__text').text()}`,
    )
}

function tileValue(tile: SummaryTile): string {
  switch (tile.value.kind) {
    case 'text':
      return tile.value.text
    case 'results':
      return tile.value.counts.map((entry) => `${entry.count} ${entry.result}`).join(', ')
    case 'unavailable':
      return '—'
  }
}

function describeTiles(tiles: readonly SummaryTile[]): string[][] {
  return tiles.map((tile) => [tile.label, tileValue(tile), tile.subline.join(' · ')])
}

describe('the inspection report fixtures', () => {
  it.each([
    ['JOB-1042 in progress', rooftopInProgress()],
    ['JOB-1042 after D5', rooftopSubmitted()],
    ['JOB-1039', boilerInspection()],
    ['JOB-1047 before work', blankFreezerInspection()],
    ['JOB-1036 cancelled', blankFilterInspection()],
  ])('hold exactly the members of InspectionResponse for %s', (_name, value) => {
    expect(contractProblems('InspectionResponse', value)).toEqual([])
  })

  it.each([
    ['JOB-1042 in progress', assignedInProgressJob()],
    ['JOB-1042 after D5', completedRooftopJob()],
    ['JOB-1039', boilerJob()],
    ['JOB-1047 before work', assignedFreezerJob()],
    ['JOB-1036 cancelled', cancelledFilterJob()],
  ])('hold exactly the members of JobDetailsResponse for %s', (_name, value) => {
    expect(contractProblems('JobDetailsResponse', value)).toEqual([])
  })

  it('are read by a checker that finds a missing member, an extra member and a wrong type', () => {
    const withoutNotes: Record<string, unknown> = { ...rooftopSubmitted() }
    Reflect.deleteProperty(withoutNotes, 'technicianNotes')
    const extra = { ...rooftopSubmitted(), score: 9 }
    const wrongReading = inspection({
      items: [
        {
          ...rooftopItem('draft', 3),
          answer: answer('Pass', onDemoDay(0, 9, 31), null, [
            { readingFieldId: suctionField, value: '118' as unknown as number },
          ]),
        },
      ],
    })

    expect(contractProblems('InspectionResponse', withoutNotes)).toEqual([
      'InspectionResponse.technicianNotes: missing',
    ])
    expect(contractProblems('InspectionResponse', extra)).toEqual([
      'InspectionResponse.score: not in the contract',
    ])
    expect(contractProblems('InspectionResponse', wrongReading)).toEqual([
      'InspectionResponse.items[0].answer: matches 0 of the oneOf options',
    ])
  })
})

describe('reading values', () => {
  it.each([
    [118, '118'],
    [285.0, '285'],
    [0.4, '0.4'],
    [12.5, '12.5'],
    [54.25, '54.25'],
    [100, '100'],
    [0, '0'],
    [-0, '0'],
    [12345.5, '12345.5'],
    [99999999.99, '99999999.99'],
    [-12.5, '−12.5'],
    [-99999999.99, '−99999999.99'],
  ])('shows %d as %s, without trailing zeros, grouping or a hyphen for minus', (value, shown) => {
    expect(formatReadingValue(value)).toBe(shown)
  })

  it('puts a no-break space between the value and its unit', () => {
    expect(formatReading(54, '°F')).toBe('54 °F')
    expect(formatReading(0.4, 'Ω')).toBe('0.4 Ω')
    expect(formatReading(-2.5, 'in')).toBe('−2.5 in')
    expect(formatReading(3, ' ')).toBe('3')
  })
})

describe('time on site', () => {
  it('reads hours and minutes, minutes alone under an hour, and less than a minute', () => {
    expect(formatTimeOnSite(30_000)).toBe('Less than a minute')
    expect(formatTimeOnSite(-5_000)).toBe('Less than a minute')
    expect(formatTimeOnSite(45 * 60_000)).toBe('45 min')
    expect(formatTimeOnSite(106 * 60_000)).toBe('1 h 46 min')
    expect(formatTimeOnSite((67 * 60 + 18) * 60_000)).toBe('67 h 18 min')
  })

  it('takes the difference of the two moments shown, each truncated to its minute (D5-12)', () => {
    const at = (hours: number, minutes: number, seconds: number) =>
      new Date(2026, 9, 19, hours, minutes, seconds).toISOString()

    expect(timeOnSiteBetween(at(9, 42, 50), at(9, 58, 10))).toBe('16 min')
    expect(formatTimeRange(at(9, 42, 50), at(9, 58, 10), demoToday)).toBe('9:42–9:58 AM')
    expect(timeOnSiteBetween(at(9, 42, 10), at(9, 42, 50))).toBe('Less than a minute')
    expect(timeOnSiteBetween(at(9, 42, 59), at(9, 43, 0))).toBe('1 min')
    expect(timeOnSiteBetween(at(9, 12, 40), at(10, 58, 5))).toBe('1 h 46 min')
    expect(timeOnSiteBetween(at(9, 12, 0), new Date(2026, 9, 19, 10, 42, 59, 999))).toBe(
      '1 h 30 min',
    )
  })

  it('applies the truncation to the tile of a completed and of a running job', () => {
    const at = (hours: number, minutes: number, seconds: number) =>
      new Date(2026, 9, 19, hours, minutes, seconds).toISOString()
    const completed: JobDetails = {
      ...completedRooftopJob(),
      startedAt: at(9, 42, 50),
      completedAt: at(9, 58, 10),
    }
    const running: JobDetails = { ...assignedInProgressJob(), startedAt: at(9, 42, 50) }
    const now = new Date(2026, 9, 19, 9, 58, 10)

    expect(describeTiles(summaryTiles(completed, rooftopSubmitted(), demoToday))[1]).toEqual([
      'Time on site',
      '16 min',
      '9:42–9:58 AM',
    ])
    expect(describeTiles(summaryTiles(running, rooftopInProgress(), now))[1]).toEqual([
      'Time on site',
      '16 min',
      'Since 9:42 AM',
    ])
    expect(
      describeTiles(
        summaryTiles(running, rooftopInProgress(), new Date(2026, 9, 19, 9, 42, 59)),
      )[1],
    ).toEqual(['Time on site', 'Less than a minute', 'Since 9:42 AM'])
  })

  it('waits for the next wall-clock minute', () => {
    expect(millisecondsToNextMinute(new Date(2026, 9, 19, 10, 42, 30))).toBe(30_000)
    expect(millisecondsToNextMinute(new Date(2026, 9, 19, 10, 42, 0))).toBe(60_000)
    expect(millisecondsToNextMinute(new Date(2026, 9, 19, 10, 42, 59, 999))).toBe(1)
  })

  it('writes a range today as a window, with the date on an earlier day', () => {
    expect(formatTimeRange(onDemoDay(0, 9, 12), onDemoDay(0, 10, 58), demoToday)).toBe(
      '9:12–10:58 AM',
    )
    expect(formatTimeRange(onDemoDay(-3, 15, 40), onDemoDay(-3, 16, 25), demoToday)).toBe(
      'Fri 16 Oct, 3:40–4:25 PM',
    )
    expect(formatTimeRange(onDemoDay(-3, 15, 40), onDemoDay(0, 8, 52), demoToday)).toBe(
      'Fri 16 Oct, 3:40 PM – 8:52 AM',
    )
  })
})

describe('the summary tiles', () => {
  it('show the result counts, time on site and completion of a completed job (JOB-1042 after D5)', () => {
    expect(
      describeTiles(summaryTiles(completedRooftopJob(), rooftopSubmitted(), demoToday)),
    ).toEqual([
      ['Result', '8 Pass, 1 Fail, 1 NotApplicable', 'Rooftop HVAC service · 10 items'],
      ['Time on site', '1 h 46 min', '9:12–10:58 AM'],
      ['Completed', '10:58 AM', 'by Marco Reyes'],
    ])
  })

  it('show the saved answers, the running time and the last save while the job is open', () => {
    const tiles = summaryTiles(assignedInProgressJob(), rooftopInProgress(), demoToday)

    expect(describeTiles(tiles)).toEqual([
      ['Result', '4 Pass, 1 Fail, 1 NotApplicable', '6 of 10 answered'],
      ['Time on site', '1 h 30 min', 'Since 9:12 AM'],
      ['Last saved', '9:50 AM', 'by Marco Reyes'],
    ])
    expect(tiles.map((tile) => tile.icon)).toEqual(['clipboard-check', 'clock', 'activity'])
  })

  it('show On hold like In progress, and "—" before the first save', () => {
    const onHold: JobDetails = { ...assignedInProgressJob(), status: 'OnHold' }
    const blank = inspection({ items: rooftopItems('blank'), startedAt: onDemoDay(0, 9, 12) })

    expect(describeTiles(summaryTiles(onHold, blank, demoToday))).toEqual([
      ['Result', '—', '0 of 10 answered'],
      ['Time on site', '1 h 30 min', 'Since 9:12 AM'],
      ['Last saved', '—', 'No answers saved yet'],
    ])
  })

  it('show "—" with the reason on a job that has not started', () => {
    const unassigned: JobDetails = { ...assignedFreezerJob(), status: 'Unassigned' }

    expect(
      describeTiles(summaryTiles(assignedFreezerJob(), blankFreezerInspection(), demoToday)),
    ).toEqual([
      ['Result', '—', '0 of 10 answered'],
      ['Time on site', '—', 'Not started'],
      ['Last saved', '—', 'No answers saved yet'],
    ])
    expect(describeTiles(summaryTiles(unassigned, blankFreezerInspection(), demoToday))).toEqual([
      ['Result', '—', '0 of 10 answered'],
      ['Time on site', '—', 'Not started'],
      ['Last saved', '—', 'No answers saved yet'],
    ])
  })

  it('show the cancellation on its earlier day, with the time and the actor in the subline', () => {
    const tiles = summaryTiles(cancelledFilterJob(), blankFilterInspection(), demoToday)

    expect(describeTiles(tiles)).toEqual([
      ['Result', '—', '0 of 10 answered'],
      ['Time on site', '—', 'Not completed'],
      ['Cancelled', 'Fri 16 Oct', 'at 9:20 AM by Dana Whitfield'],
    ])
    expect(tiles[2]?.icon).toBe('x-circle')
  })

  it('keep the chips of any answers a cancelled job saved', () => {
    const items = rooftopItems('blank').map((entry) =>
      entry.position <= 2 ? { ...entry, answer: answer('Pass', onDemoDay(-4, 10, 0)) } : entry,
    )
    const saved = inspection({ jobStatus: 'Cancelled', items })

    expect(describeTiles(summaryTiles(cancelledFilterJob(), saved, demoToday))[0]).toEqual([
      'Result',
      '2 Pass, 0 Fail, 0 NotApplicable',
      '2 of 10 answered',
    ])
  })

  it('carry the short date on an earlier day, moving the time into the subline', () => {
    const job: JobDetails = {
      ...boilerJob(),
      startedAt: onDemoDay(-3, 15, 40),
      completedAt: onDemoDay(-3, 16, 25),
      history: [
        historyEntry({
          id: '01a15400-0000-7000-8000-000010390009',
          action: 'Completed',
          fromStatus: 'InProgress',
          toStatus: 'Completed',
          actor: { id: tom.id, displayName: tom.displayName },
          occurredAt: onDemoDay(-3, 16, 25),
        }),
      ],
    }

    expect(describeTiles(summaryTiles(job, boilerInspection(), demoToday)).slice(1)).toEqual([
      ['Time on site', '45 min', 'Fri 16 Oct, 3:40–4:25 PM'],
      ['Completed', 'Fri 16 Oct', 'at 4:25 PM by Tom Becker'],
    ])
    expect(reportMeta(job, demoToday)).toBe('Completed Fri 16 Oct, 4:25 PM by Tom Becker')
  })

  it('date a running time and the last save from an earlier day', () => {
    const job: JobDetails = { ...assignedInProgressJob(), startedAt: onDemoDay(-3, 15, 40) }
    const saved: Inspection = { ...rooftopInProgress(), updatedAt: onDemoDay(-3, 9, 50) }

    expect(describeTiles(summaryTiles(job, saved, demoToday)).slice(1)).toEqual([
      ['Time on site', '67 h 2 min', 'Since Fri 16 Oct, 3:40 PM'],
      ['Last saved', 'Fri 16 Oct', 'at 9:50 AM by Marco Reyes'],
    ])
  })

  it('count one item in the singular', () => {
    const single = inspection({ jobStatus: 'Completed', items: [rooftopItem('final', 1)] })

    expect(summaryTiles(completedRooftopJob(), single, demoToday)[0]?.subline).toEqual([
      'Rooftop HVAC service',
      '1 item',
    ])
  })
})

describe('the report text', () => {
  it('names who completed the job and when, or who created an open job', () => {
    expect(reportMeta(boilerJob(), demoToday)).toBe('Completed 8:52 AM by Tom Becker')
    expect(reportMeta(completedRooftopJob(), demoToday)).toBe('Completed 10:58 AM by Marco Reyes')
    expect(reportMeta(assignedInProgressJob(), demoToday)).toBe('Created 8:14 AM by Dana Whitfield')
  })

  it.each([
    ['Unassigned', 'Not started yet.', 'The checklist has no answers.', 'clock'],
    ['Assigned', 'Not started yet.', 'The checklist has no answers.', 'clock'],
    ['InProgress', 'Not submitted yet.', 'This report shows the answers saved so far.', 'clock'],
    ['OnHold', 'Not submitted yet.', 'This report shows the answers saved so far.', 'clock'],
    ['Cancelled', 'This job was cancelled.', 'Its checklist was not submitted.', 'x-circle'],
  ])('gives a %s job the page notice "%s %s"', (status, title, message, icon) => {
    expect(reportNotice(status)).toEqual({ title, message, icon })
  })

  it('gives a completed job no page notice', () => {
    expect(reportNotice('Completed')).toBeNull()
  })

  it('says why there are no technician notes', () => {
    expect(technicianNotes(rooftopSubmitted())).toEqual({ text: rooftopNotes, empty: false })
    expect(technicianNotes({ ...rooftopSubmitted(), technicianNotes: null })).toEqual({
      text: 'No notes from the technician.',
      empty: true,
    })
    expect(technicianNotes({ ...rooftopSubmitted(), technicianNotes: ' \n ' })).toEqual({
      text: 'No notes from the technician.',
      empty: true,
    })
    expect(technicianNotes(rooftopInProgress())).toEqual({
      text: 'Notes are added when the job is submitted.',
      empty: true,
    })
  })

  it('says a cancelled job has no notes from the technician, because it will never be submitted', () => {
    expect(technicianNotes(blankFilterInspection())).toEqual({
      text: 'No notes from the technician.',
      empty: true,
    })
    expect(
      technicianNotes({ ...rooftopInProgress(), jobStatus: 'Cancelled', submittedAt: null }),
    ).toEqual({ text: 'No notes from the technician.', empty: true })
    expect(
      technicianNotes({ ...rooftopInProgress(), jobStatus: 'OnHold', submittedAt: null }),
    ).toEqual({ text: 'Notes are added when the job is submitted.', empty: true })
  })

  it('writes the load failure as the connection hint, or the wait after a 429', () => {
    const limited = (seconds: number | null) =>
      new ApiError({
        status: 429,
        code: 'rate-limit.exceeded',
        title: null,
        detail: null,
        fieldErrors: {},
        correlationId: reference,
        retryAfterSeconds: seconds,
      })

    expect(loadFailureMessage(null)).toBe('Check your connection, then try again.')
    expect(loadFailureMessage(limited(30))).toBe('Try again in 30 seconds.')
    expect(loadFailureMessage(limited(1))).toBe('Try again in 1 second.')
    expect(loadFailureMessage(limited(null))).toBe('Try again in 60 seconds.')
  })
})

describe('the results rows', () => {
  it('number the items by position with two digits and show each reading in field order', () => {
    const rows = reportRows(rooftopSubmitted())

    expect(rows.map((row) => row.number)).toEqual([
      '01',
      '02',
      '03',
      '04',
      '05',
      '06',
      '07',
      '08',
      '09',
      '10',
    ])
    expect(rows[2]?.readings).toEqual([
      { id: suctionField, label: 'Suction', value: '118 psi' },
      { id: dischargeField, label: 'Discharge', value: '285 psi' },
    ])
    expect(rows[7]?.readings.map((entry) => `${entry.label} ${entry.value}`)).toEqual([
      'Supply air temperature 54 °F',
    ])
  })

  it('keeps the order of the positions, whatever order the items arrive in', () => {
    const shuffled = { ...rooftopSubmitted(), items: [...rooftopItems('final')].reverse() }

    expect(reportRows(shuffled)[0]?.prompt).toBe('Lock out and tag out unit power')
  })

  it('leaves unanswered items without a result, a reading or a note', () => {
    const rows = reportRows(rooftopInProgress())

    expect(rows.slice(6).map((row) => [row.result, row.readings, row.note])).toEqual([
      [null, [], null],
      [null, [], null],
      [null, [], null],
      [null, [], null],
    ])
  })

  it('trims a note and leaves out a blank one', () => {
    const items = [
      { ...rooftopItem('final', 1), answer: answer('Pass', onDemoDay(0, 9, 31), '  Checked  ') },
      { ...rooftopItem('final', 2), answer: answer('Pass', onDemoDay(0, 9, 31), ' \n ') },
    ]

    expect(reportRows(inspection({ items })).map((row) => row.note)).toEqual(['Checked', null])
  })

  it('pads the item numbers to the widest position', () => {
    const items = Array.from({ length: 100 }, (_, index) => ({
      checklistItemId: `01a15175-3c00-7000-8000-${String(index).padStart(12, '0')}`,
      position: index + 1,
      prompt: `Item ${index + 1}`,
      readingFields: [],
      answer: null,
    }))

    const numbers = reportRows(inspection({ items })).map((row) => row.number)
    expect([numbers[0], numbers[99]]).toEqual(['001', '100'])
  })

  it('labels a reading on a Fail answer, with a long unit', () => {
    const item = {
      ...rooftopItem('blank', 1),
      readingFields: [readingField(suctionField, 1, 'Flow', 'gal/min')],
      answer: answer('Fail', onDemoDay(0, 9, 31), 'Low flow', [reading(suctionField, 12.3)]),
    }

    expect(reportRows(inspection({ items: [item] }))[0]?.readings).toEqual([
      { id: suctionField, label: 'Flow', value: '12.3 gal/min' },
    ])
  })
})

describe('the inspection report page', () => {
  it('shows the JOB-1039 report as the board draws it', async () => {
    const { wrapper } = await openApp(
      reportPath(boilerJobId),
      reportRoutes(boilerJob(), boilerInspection()),
    )

    const eyebrow = wrapper.find('.page-header__eyebrow')
    expect(visibleText(eyebrow.element)).toBe('JOB-1039 · Inspection report')
    expect(spokenText(eyebrow.element)).toBe('JOB-1039, Inspection report')
    expect(eyebrow.find('.page-header__number').text()).toBe('JOB-1039')
    expect(wrapper.find('h1').text()).toBe('Boiler pressure drop')
    expect(metaParts(wrapper)).toEqual(['Completed', 'High', 'Completed 8:52 AM by Tom Becker'])
    expect(document.title).toBe('JOB-1039 inspection report · FieldService Admin')
    const crumb = wrapper.find('nav[aria-label="Breadcrumb"] [aria-current="page"]')
    expect(crumb.text()).toBe('JOB-1039')
    expect(crumb.classes()).toContain('app-shell__crumb-current--mono')
    expect(wrapper.findAll('nav[aria-label="Breadcrumb"] a').map((link) => link.text())).toEqual([
      'Jobs',
    ])

    const back = wrapper.find('.page-header__actions a')
    expect(back.text()).toBe('Job details')
    expect(back.attributes('href')).toBe(`/jobs/${boilerJobId}`)
    expect(back.classes()).toEqual(
      expect.arrayContaining(['app-button--secondary', 'app-button--small']),
    )
    expect(back.find('svg').element.innerHTML).toBe(iconMarkup('arrow-left', 16))

    expect(noticeOf(wrapper).exists()).toBe(false)
    expect(shownTiles(wrapper)).toEqual([
      { label: 'Result', value: '7 Pass 0 Fail 1 N/A', subline: 'Boiler service · 8 items' },
      { label: 'Time on site', value: '1 h 47 min', subline: '7:05–8:52 AM' },
      { label: 'Completed', value: '8:52 AM', subline: 'by Tom Becker' },
    ])

    expect(wrapper.find('section.section-card h2').text()).toBe('Inspection · Boiler service')
    expect(resultsRegion(wrapper).attributes('tabindex')).toBe('0')
    expect(resultsRegion(wrapper).find('caption').text()).toBe('Inspection results')
    expect(
      resultsRegion(wrapper)
        .findAll('th')
        .map((cell) => cell.text()),
    ).toEqual(['#', 'Item', 'Result', 'Reading', 'Note'])
    expect(rowCells(wrapper)).toEqual([
      ['01', 'Isolate gas and electrical supply', 'Pass', '—', '—'],
      [
        '02',
        'Inspect valves and fittings for leaks',
        'Pass',
        '—',
        'Tightened a weeping fitting on the return line',
      ],
      ['03', 'Check system pressure', 'Pass', 'System pressure 18 psi', '—'],
      ['04', 'Test pressure relief valve', 'Pass', '—', '—'],
      ['05', 'Inspect expansion tank', 'Pass', '—', 'Recharged to 12 psi'],
      ['06', 'Check burner flame and combustion', 'Pass', '—', '—'],
      ['07', 'Test low-water cutoff', 'N/A', '—', 'Not fitted on this model'],
      ['08', 'Restore supply and confirm operation', 'Pass', '—', '—'],
    ])

    expect(wrapper.find('.inspection-report__notes').text()).toBe(boilerNotes)
    expect(detailRows(wrapper)).toEqual([
      ['Customer', 'Lakeside Dental'],
      ['Site', '9 Lakeside Court'],
      ['Scheduled', 'Mon 19 Oct, 7:00–8:30 AM'],
      ['Technician', 'Tom Becker'],
      ['Checklist', 'Boiler service'],
    ])
    expect(wrapper.find('.job-details__technician .avatar').text()).toBe('TB')
    expect(historyLines(wrapper)).toEqual([
      '6:58 AM Created by Dana Whitfield',
      '7:00 AM Assigned to Tom Becker',
      '7:05 AM Started by Tom Becker',
      '8:52 AM Completed by Tom Becker',
    ])
    expect(wrapper.findAll('.status-timeline__dot--newest')).toHaveLength(1)
  })

  it('shows the JOB-1042 report after D5 with labelled readings in field order (W5-AC10)', async () => {
    const { wrapper } = await openApp(
      reportPath(rooftopJobId),
      reportRoutes(completedRooftopJob(), rooftopSubmitted()),
    )

    expect(visibleText(wrapper.find('.page-header__eyebrow').element)).toBe(
      'JOB-1042 · Inspection report',
    )
    expect(wrapper.find('h1').text()).toBe('Rooftop HVAC unit not cooling')
    expect(metaParts(wrapper)).toEqual(['Completed', 'High', 'Completed 10:58 AM by Marco Reyes'])
    expect(shownTiles(wrapper)).toEqual([
      {
        label: 'Result',
        value: '8 Pass 1 Fail 1 N/A',
        subline: 'Rooftop HVAC service · 10 items',
      },
      { label: 'Time on site', value: '1 h 46 min', subline: '9:12–10:58 AM' },
      { label: 'Completed', value: '10:58 AM', subline: 'by Marco Reyes' },
    ])
    const rows = rowCells(wrapper)
    expect(rows).toHaveLength(10)
    expect(rows[2]).toEqual([
      '03',
      'Check refrigerant pressures',
      'Pass',
      'Suction 118 psi / Discharge 285 psi',
      '—',
    ])
    expect(rows[4]).toEqual([
      '05',
      'Inspect condenser fan motor and bearings',
      'Fail',
      '—',
      fanMotorNote,
    ])
    expect(rows[5]).toEqual(['06', 'Check belts and pulleys', 'N/A', '—', 'Direct-drive unit'])
    expect(rows[7]).toEqual([
      '08',
      'Record supply air temperature',
      'Pass',
      'Supply air temperature 54 °F',
      '—',
    ])
    expect(
      rows.filter((_, index) => ![2, 4, 5, 7].includes(index)).map((row) => row.slice(2)),
    ).toEqual(Array.from({ length: 6 }, () => ['Pass', '—', '—']))

    const line = resultsRegion(wrapper).find('.inspection-results__reading')
    expect(line.find('.inspection-results__reading-label').text()).toBe('Suction')
    expect(line.find('.inspection-results__reading-value').text()).toBe('118 psi')
    expect(wrapper.find('.inspection-report__notes').text()).toBe(rooftopNotes)
    expect(historyLines(wrapper)).toEqual([
      '8:14 AM Created by Dana Whitfield',
      '8:20 AM Assigned to Marco Reyes',
      '9:12 AM Started by Marco Reyes',
      '10:58 AM Completed by Marco Reyes',
    ])
  })

  it('reads reading values the API writes with trailing zeros, as 118.00', async () => {
    const wire = JSON.stringify(rooftopSubmitted())
      .replace('"value":285', '"value":285.00')
      .replace('"value":118', '"value":118.00')
      .replace('"value":54', '"value":54.00')
    const { wrapper } = await openApp(reportPath(rooftopJobId), {
      [jobRoute(rooftopJobId)]: json(completedRooftopJob()),
      [inspectionRoute(rooftopJobId)]: () =>
        new Response(wire, { status: 200, headers: { 'Content-Type': 'application/json' } }),
    })

    expect(wire).toContain('"value":118.00')
    expect(rowCells(wrapper)[2]?.[3]).toBe('Suction 118 psi / Discharge 285 psi')
    expect(rowCells(wrapper)[7]?.[3]).toBe('Supply air temperature 54 °F')
  })

  it('stacks every reading label over its value, readings 6px apart (D5-01)', async () => {
    const removeStyles = installStyles('features/jobs/components/InspectionResultsTable.vue')
    try {
      const { wrapper } = await openApp(
        reportPath(rooftopJobId),
        reportRoutes(completedRooftopJob(), rooftopSubmitted()),
      )

      const lines = resultsRegion(wrapper).findAll('.inspection-results__reading')
      expect(lines).toHaveLength(3)
      for (const line of lines) {
        const style = getComputedStyle(line.element)
        expect([style.display, style.flexDirection]).toEqual(['flex', 'column'])
        expect(style.flexWrap).not.toBe('wrap')
        expect(
          Array.from(line.element.children, (part) => part.className.replace(/\s.*$/, '')),
        ).toEqual(['inspection-results__reading-label', 'inspection-results__reading-value'])
      }
      const readings = resultsRegion(wrapper).find('.inspection-results__readings')
      expect(getComputedStyle(readings.element).gap).toBe('var(--spacing-fs-6)')
      expect(
        ruleDeclarations(
          'features/jobs/components/InspectionResultsTable.vue',
          '.inspection-results__reading-label',
        ),
      ).toMatchObject({
        'font-size': 'var(--text-fs-meta)',
        'line-height': 'var(--text-fs-meta--line-height)',
      })
    } finally {
      removeStyles()
    }
  })

  it('draws the result tile as a wrapping row of result chips with their icons', async () => {
    const { wrapper } = await openApp(
      reportPath(rooftopJobId),
      reportRoutes(completedRooftopJob(), rooftopSubmitted()),
    )

    const chips = wrapper.findAll('.kpi-tile__content .status-chip')
    expect(chips.map((chip) => chip.classes().find((name) => name !== 'status-chip'))).toEqual([
      'status-chip--completed',
      'status-chip--fail',
      'status-chip--cancelled',
    ])
    expect(chips.map((chip) => chip.find('svg').element.innerHTML)).toEqual([
      iconMarkup('check', 14),
      iconMarkup('x', 14),
      iconMarkup('minus', 14),
    ])
    expect(ruleDeclarations('shared/ui/KpiTile.vue', '.kpi-tile__content')).toMatchObject({
      display: 'flex',
      'flex-wrap': 'wrap',
      gap: 'var(--spacing-fs-8)',
    })
  })

  it('marks a job in progress as not submitted and shows the answers saved so far', async () => {
    const { wrapper } = await openApp(
      reportPath(rooftopJobId),
      reportRoutes(assignedInProgressJob(), rooftopInProgress()),
    )

    const notice = noticeOf(wrapper)
    expect(notice.text()).toBe('Not submitted yet. This report shows the answers saved so far.')
    expect(notice.attributes('role')).toBeUndefined()
    expect(notice.classes()).toContain('banner--info')
    expect(notice.find('.banner__title').text()).toBe('Not submitted yet.')
    expect(notice.find('.banner__tile svg').element.innerHTML).toBe(iconMarkup('clock', 18))
    expect(metaParts(wrapper)).toEqual(['In progress', 'High', 'Created 8:14 AM by Dana Whitfield'])
    expect(shownTiles(wrapper)).toEqual([
      { label: 'Result', value: '4 Pass 1 Fail 1 N/A', subline: '6 of 10 answered' },
      { label: 'Time on site', value: '1 h 30 min', subline: 'Since 9:12 AM' },
      { label: 'Last saved', value: '9:50 AM', subline: 'by Marco Reyes' },
    ])
    expect(rowCells(wrapper).slice(6)).toEqual([
      ['07', 'Clear condensate drain line', 'Not answered', '—', '—'],
      ['08', 'Record supply air temperature', 'Not answered', '—', '—'],
      ['09', 'Test thermostat and economizer controls', 'Not answered', '—', '—'],
      ['10', 'Restore power and confirm normal operation', 'Not answered', '—', '—'],
    ])
    expect(resultsRegion(wrapper).findAll('.inspection-results__not-answered')).toHaveLength(4)
    const notes = wrapper.find('.inspection-report__notes')
    expect(notes.text()).toBe('Notes are added when the job is submitted.')
    expect(notes.classes()).toContain('inspection-report__notes--empty')
  })

  it('counts the time on site once a minute while the job is open', async () => {
    vi.useFakeTimers({ toFake: ['Date', 'setTimeout', 'clearTimeout'], now: demoToday })
    const { wrapper } = await openApp(
      reportPath(rooftopJobId),
      reportRoutes(assignedInProgressJob(), rooftopInProgress()),
    )
    const timeTile = () => wrapper.find('[data-tile="time-on-site"] .kpi-tile__value')

    expect(timeTile().text()).toBe('1 h 30 min')
    vi.advanceTimersByTime(59_999)
    await flushPromises()
    expect(timeTile().text()).toBe('1 h 30 min')
    vi.advanceTimersByTime(1)
    await flushPromises()
    expect(timeTile().text()).toBe('1 h 31 min')
    vi.advanceTimersByTime(60_000)
    await flushPromises()
    expect(timeTile().text()).toBe('1 h 32 min')
  })

  it('moves the running time on site at each wall-clock minute, from moments with seconds (D5-12)', async () => {
    vi.useFakeTimers({
      toFake: ['Date', 'setTimeout', 'clearTimeout'],
      now: new Date(2026, 9, 19, 9, 58, 10),
    })
    const job: JobDetails = {
      ...assignedInProgressJob(),
      startedAt: new Date(2026, 9, 19, 9, 42, 50).toISOString(),
    }
    const { wrapper } = await openApp(
      reportPath(rooftopJobId),
      reportRoutes(job, rooftopInProgress()),
    )
    const timeTile = () => wrapper.find('[data-tile="time-on-site"]')

    expect(timeTile().find('.kpi-tile__value').text()).toBe('16 min')
    expect(timeTile().find('.kpi-tile__subline').text()).toBe('Since 9:42 AM')
    vi.advanceTimersByTime(49_999)
    await flushPromises()
    expect(timeTile().find('.kpi-tile__value').text()).toBe('16 min')
    vi.advanceTimersByTime(1)
    await flushPromises()
    expect(timeTile().find('.kpi-tile__value').text()).toBe('17 min')
    vi.advanceTimersByTime(60_000)
    await flushPromises()
    expect(timeTile().find('.kpi-tile__value').text()).toBe('18 min')
  })

  it('shows a job that has not started with "—" in the muted colour and the reasons', async () => {
    const { wrapper } = await openApp(
      reportPath(freezerJobId),
      reportRoutes(assignedFreezerJob(), blankFreezerInspection()),
    )

    expect(noticeOf(wrapper).text()).toBe('Not started yet. The checklist has no answers.')
    expect(shownTiles(wrapper)).toEqual([
      { label: 'Result', value: '—', subline: '0 of 10 answered' },
      { label: 'Time on site', value: '—', subline: 'Not started' },
      { label: 'Last saved', value: '—', subline: 'No answers saved yet' },
    ])
    expect(wrapper.findAll('.kpi-tile__value--unavailable')).toHaveLength(3)
    expect(rowCells(wrapper).every((row) => row[2] === 'Not answered')).toBe(true)
  })

  it('shows a cancelled job with its notice and the cancellation tile', async () => {
    const { wrapper } = await openApp(
      reportPath(filterJobId),
      reportRoutes(cancelledFilterJob(), blankFilterInspection()),
    )

    expect(noticeOf(wrapper).text()).toBe(
      'This job was cancelled. Its checklist was not submitted.',
    )
    expect(noticeOf(wrapper).find('.banner__tile svg').element.innerHTML).toBe(
      iconMarkup('x-circle', 18),
    )
    expect(shownTiles(wrapper)).toEqual([
      { label: 'Result', value: '—', subline: '0 of 10 answered' },
      { label: 'Time on site', value: '—', subline: 'Not completed' },
      { label: 'Cancelled', value: 'Fri 16 Oct', subline: 'at 9:20 AM by Dana Whitfield' },
    ])
    const notes = wrapper.find('.inspection-report__notes')
    expect(notes.text()).toBe('No notes from the technician.')
    expect(notes.classes()).toContain('inspection-report__notes--empty')
  })

  it('says the technician left no notes on a submitted inspection without them', async () => {
    const { wrapper } = await openApp(
      reportPath(boilerJobId),
      reportRoutes(boilerJob(), { ...boilerInspection(), technicianNotes: null }),
    )

    expect(wrapper.find('.inspection-report__notes').text()).toBe('No notes from the technician.')
  })

  it('keeps line breaks in the notes and wraps a long word anywhere', async () => {
    const notes = `First line.\nSecond line with an unbroken ${'x'.repeat(300)}`
    const { wrapper } = await openApp(
      reportPath(boilerJobId),
      reportRoutes(boilerJob(), { ...boilerInspection(), technicianNotes: notes }),
    )

    expect(wrapper.find('.inspection-report__notes').element.textContent).toBe(notes)
    expect(
      ruleDeclarations('features/jobs/pages/InspectionReportPage.vue', '.inspection-report__notes'),
    ).toMatchObject({ 'white-space': 'pre-line', 'overflow-wrap': 'anywhere' })
    expect(
      ruleDeclarations(
        'features/jobs/components/InspectionResultsTable.vue',
        '.inspection-results__note',
      ),
    ).toEqual({
      color: 'var(--color-fs-ink-2)',
      'white-space': 'pre-line',
      'overflow-wrap': 'anywhere',
      'text-wrap': 'pretty',
    })
  })

  it('shows notes, prompts and answers that look like markup as plain text', async () => {
    const markup = '<img src=x onerror=alert(1)>'
    const items = rooftopItems('final').map((entry) =>
      entry.position === 1
        ? { ...entry, prompt: markup, answer: answer('Fail', onDemoDay(0, 9, 31), markup) }
        : entry,
    )
    const { wrapper } = await openApp(
      reportPath(rooftopJobId),
      reportRoutes(completedRooftopJob(), {
        ...rooftopSubmitted(),
        items,
        technicianNotes: markup,
      }),
    )

    expect(wrapper.find('main img').exists()).toBe(false)
    expect(rowCells(wrapper)[0]).toEqual(['01', markup, 'Fail', '—', markup])
    expect(wrapper.find('.inspection-report__notes').text()).toBe(markup)
  })

  it('offers no control that edits the inspection', async () => {
    const { wrapper } = await openApp(
      reportPath(rooftopJobId),
      reportRoutes(assignedInProgressJob(), rooftopInProgress()),
    )

    expect(wrapper.find('main').findAll('input, select, textarea, button')).toHaveLength(0)
    expect(
      wrapper
        .find('main')
        .findAll('a')
        .map((link) => link.text()),
    ).toEqual(['Job details'])
  })

  it('waits for both calls before it shows the report', async () => {
    const slow = deferred()
    const { wrapper } = await openApp(reportPath(boilerJobId), {
      [jobRoute(boilerJobId)]: json(boilerJob()),
      [inspectionRoute(boilerJobId)]: slow.reply,
    })

    expect(wrapper.find('.page-header__eyebrow').exists()).toBe(false)
    expect(wrapper.find('main [role="status"].visually-hidden').text()).toBe(
      'Loading inspection report',
    )

    slow.resolve(jsonResponse(boilerInspection()))
    await flushPromises()

    expect(wrapper.find('h1').text()).toBe('Boiler pressure drop')
    expect(wrapper.find('main [role="status"].visually-hidden').exists()).toBe(false)
  })

  it('shows the loading state with the real tile labels, the table header and five skeleton rows', async () => {
    const pending = deferred()
    const { wrapper } = await openApp(reportPath(boilerJobId), {
      [jobRoute(boilerJobId)]: pending.reply,
      [inspectionRoute(boilerJobId)]: pending.reply,
    })

    expect(wrapper.find('h1').text()).toBe('Inspection report')
    expect(wrapper.find('h1 .loading-state').attributes('aria-hidden')).toBe('true')
    expect(wrapper.find('nav[aria-label="Breadcrumb"] [aria-current="page"]').text()).toBe(
      'Inspection report',
    )
    expect(document.title).toBe('Inspection report · FieldService Admin')
    expect(wrapper.findAll('.kpi-tile__label').map((label) => label.text())).toEqual([
      'Result',
      'Time on site',
    ])
    const closing = wrapper.find('[data-tile="closing"]')
    expect(closing.find('.kpi-tile__label').exists()).toBe(false)
    expect(closing.find('.kpi-tile__label-skeleton').attributes('aria-hidden')).toBe('true')
    expect(closing.find('.icon-tile').classes()).toEqual(
      expect.arrayContaining(['icon-tile--40', 'icon-tile--neutral']),
    )
    expect(closing.find('.icon-tile svg').exists()).toBe(false)
    expect(
      wrapper.findAll('.kpi-tile').map((tile) => tile.find('.icon-tile svg').exists()),
    ).toEqual([true, true, false])
    expect(wrapper.find('.inspection-summary').text()).toBe('ResultTime on site')
    const skeletons = wrapper.findAll('.kpi-tile__skeleton')
    expect(skeletons).toHaveLength(3)
    expect(skeletons.every((block) => block.attributes('aria-hidden') === 'true')).toBe(true)
    expect(resultsRegion(wrapper).find('table').attributes('aria-busy')).toBe('true')
    expect(
      resultsRegion(wrapper)
        .findAll('th')
        .map((cell) => cell.text()),
    ).toEqual(['#', 'Item', 'Result', 'Reading', 'Note'])
    const skeletonRows = resultsRegion(wrapper).findAll('tbody tr')
    expect(skeletonRows).toHaveLength(5)
    expect(skeletonRows.every((row) => row.attributes('aria-hidden') === 'true')).toBe(true)
    expect(
      resultsRegion(wrapper)
        .findAll('tbody tr:first-child .loading-state__block')
        .map((block) => block.classes().find((name) => name.startsWith('loading-state__block--'))),
    ).toEqual([
      'loading-state__block--item-number',
      'loading-state__block--item-prompt',
      'loading-state__block--result-chip',
      'loading-state__block--reading',
      'loading-state__block--note',
    ])
    expect(wrapper.findAll('section.section-card h2').map((heading) => heading.text())).toEqual([
      'Job details',
      'Status history',
    ])
    const statuses = wrapper
      .findAll('main [role="status"]')
      .filter((status) => status.text() !== '')
    expect(statuses.map((status) => status.text())).toEqual(['Loading inspection report'])
  })

  it('names no closing tile while a deep link to an open job loads, then shows Last saved', async () => {
    const slow = deferred()
    const { wrapper } = await openApp(reportPath(rooftopJobId), {
      [jobRoute(rooftopJobId)]: json(assignedInProgressJob()),
      [inspectionRoute(rooftopJobId)]: slow.reply,
    })

    const loadingText = wrapper.find('.inspection-summary').text()
    expect(loadingText).not.toContain('Completed')
    expect(loadingText).not.toContain('Last saved')
    expect(wrapper.find('[data-tile="closing"] .kpi-tile__label-skeleton').exists()).toBe(true)

    slow.resolve(jsonResponse(rooftopInProgress()))
    await flushPromises()

    expect(wrapper.find('[data-tile="closing"]').exists()).toBe(false)
    expect(wrapper.find('[data-tile="last-saved"] .kpi-tile__label').text()).toBe('Last saved')
    expect(wrapper.find('.kpi-tile__label-skeleton').exists()).toBe(false)
    expect(wrapper.findAll('.icon-tile svg')).toHaveLength(3)
  })

  it('shows a load failure with its reference, and Try again shows the report and focuses the heading', async () => {
    const { wrapper, stub } = await openApp(reportPath(boilerJobId), {
      [jobRoute(boilerJobId)]: json(boilerJob()),
      [inspectionRoute(boilerJobId)]: [
        problem(
          500,
          'server.error',
          { correlationId: reference },
          { 'X-Correlation-ID': reference },
        ),
        json(boilerInspection()),
      ],
    })

    expect(wrapper.find('h1').text()).toBe('Inspection report')
    const banner = wrapper.find('main .banner--error')
    expect(banner.attributes('role')).toBe('status')
    expect(banner.find('.banner__text').text()).toBe(
      'The inspection report could not be loaded. Check your connection, then try again.',
    )
    expect(banner.find('.banner__reference').text()).toBe(`Reference ${reference}`)
    expect(wrapper.find('.kpi-tile').exists()).toBe(false)

    await banner.find('button').trigger('click')
    await flushPromises()

    expect(stub.requestsTo(inspectionRoute(boilerJobId))).toHaveLength(2)
    expect(wrapper.find('h1').text()).toBe('Boiler pressure drop')
    expect(document.activeElement).toBe(wrapper.find('h1').element)
  })

  it('announces a second failure as an alert and moves focus to the new Try again', async () => {
    const { wrapper } = await openApp(reportPath(boilerJobId), {
      [jobRoute(boilerJobId)]: problem(500, 'server.error'),
      [inspectionRoute(boilerJobId)]: json(boilerInspection()),
    })

    await wrapper.find('main .banner--error button').trigger('click')
    await flushPromises()

    const banner = wrapper.find('main .banner--error')
    expect(banner.attributes('role')).toBe('alert')
    expect(document.activeElement).toBe(banner.find('button').element)
  })

  it('waits for Retry-After after a 429 and shows no reference', async () => {
    const { wrapper } = await openApp(reportPath(boilerJobId), {
      [jobRoute(boilerJobId)]: problem(429, 'rate-limit.exceeded', {}, { 'Retry-After': '30' }),
      [inspectionRoute(boilerJobId)]: json(boilerInspection()),
    })

    const banner = wrapper.find('main .banner--error')
    expect(banner.find('.banner__text').text()).toBe(
      'The inspection report could not be loaded. Try again in 30 seconds.',
    )
    expect(banner.find('.banner__reference').exists()).toBe(false)
  })

  it('shows no reference when the API did not answer', async () => {
    const { wrapper } = await openApp(reportPath(boilerJobId), {
      [jobRoute(boilerJobId)]: networkFailure(),
      [inspectionRoute(boilerJobId)]: networkFailure(),
    })

    const banner = wrapper.find('main .banner--error')
    expect(banner.find('.banner__text').text()).toBe(
      'The inspection report could not be loaded. Check your connection, then try again.',
    )
    expect(banner.find('.banner__reference').exists()).toBe(false)
  })

  it.each([
    ['GetJobById', 'job'],
    ['GetInspection', 'inspection'],
  ])('shows the missing-job state when %s answers 404', async (_name, missing) => {
    const notFound = problem(404, 'job.not-found')
    const { wrapper } = await openApp(reportPath(boilerJobId), {
      [jobRoute(boilerJobId)]: missing === 'job' ? notFound : json(boilerJob()),
      [inspectionRoute(boilerJobId)]: missing === 'job' ? json(boilerInspection()) : notFound,
    })

    expect(wrapper.find('h1').text()).toBe('Job not found')
    expect(wrapper.find('main h2').text()).toBe('No job matches this link')
    expect(wrapper.find('main').text()).toContain(
      'The link may be mistyped or out of date. Every job, cancelled ones included, is in the jobs list.',
    )
    expect(wrapper.find('main a').text()).toBe('Back to jobs')
    expect(wrapper.find('main a').attributes('href')).toBe('/jobs')
    expect(wrapper.find('nav[aria-label="Breadcrumb"] [aria-current="page"]').text()).toBe(
      'Job not found',
    )
    expect(document.title).toBe('Job not found · FieldService Admin')
  })

  it('prefers the missing-job state when one call answers 404 and the other fails', async () => {
    const { wrapper } = await openApp(reportPath(boilerJobId), {
      [jobRoute(boilerJobId)]: problem(500, 'server.error'),
      [inspectionRoute(boilerJobId)]: problem(404, 'job.not-found'),
    })

    expect(wrapper.find('h1').text()).toBe('Job not found')
  })

  it('shows the contact, access and equipment rows when the job has them, and no Created row', async () => {
    const { wrapper } = await openApp(
      reportPath(rooftopJobId),
      reportRoutes(completedRooftopJob(), rooftopSubmitted()),
    )

    expect(detailRows(wrapper).map(([term]) => term)).toEqual([
      'Customer',
      'Site',
      'Access',
      'Contact',
      'Scheduled',
      'Equipment',
      'Technician',
      'Checklist',
    ])
    expect(detailRows(wrapper)[6]).toEqual(['Technician', 'Marco Reyes'])
  })

  it('lays out the tiles in a 240px auto-fit grid, the main column and the 380px side column', () => {
    expect(
      ruleDeclarations(
        'features/jobs/components/InspectionSummaryStrip.vue',
        '.inspection-summary',
      ),
    ).toEqual({
      display: 'grid',
      'grid-template-columns': 'repeat(auto-fit, minmax(min(240px, 100%), 1fr))',
      gap: 'var(--spacing-fs-16)',
    })
    expect(
      ruleDeclarations('features/jobs/pages/InspectionReportPage.vue', '.inspection-report'),
    ).toMatchObject({ display: 'flex', 'flex-wrap': 'wrap', gap: 'var(--spacing-fs-24)' })
    expect(
      ruleDeclarations('features/jobs/pages/InspectionReportPage.vue', '.inspection-report__side'),
    ).toEqual({
      display: 'grid',
      flex: '1 1 380px',
      'grid-template-columns': 'repeat(auto-fit, minmax(min(380px, 100%), 1fr))',
      'align-items': 'start',
      gap: 'var(--spacing-fs-16)',
      'min-width': '0',
    })
    expect(
      ruleDeclarations('features/jobs/pages/InspectionReportPage.vue', '.inspection-report__main'),
    ).toMatchObject({ flex: '999 1 600px', 'min-width': '0' })
    expect(
      ruleDeclarations(
        'features/jobs/components/InspectionResultsTable.vue',
        '.inspection-results__reading',
      ),
    ).toEqual({ display: 'flex', 'flex-direction': 'column' })
    expect(
      ruleDeclarations(
        'features/jobs/components/InspectionResultsTable.vue',
        '.inspection-results__readings',
      ),
    ).toEqual({ display: 'flex', 'flex-direction': 'column', gap: 'var(--spacing-fs-6)' })
    expect(
      ruleDeclarations(
        'features/jobs/components/InspectionResultsTable.vue',
        '.inspection-results__reading-value',
      ),
    ).toMatchObject({ 'white-space': 'nowrap', 'font-family': 'var(--font-mono)' })
    expect(
      ruleDeclarations(
        'features/jobs/components/JobDetailsCard.vue',
        '.job-details--report .job-details__row',
      ),
    ).toEqual({ 'grid-template-columns': '112px minmax(0, 1fr)' })
  })

  it('puts the two side cards straight into the side grid, loaded and loading (D5-02)', async () => {
    const pending = deferred()
    const loading = await openApp(reportPath(boilerJobId), {
      [jobRoute(boilerJobId)]: pending.reply,
      [inspectionRoute(boilerJobId)]: pending.reply,
    })
    const sideHeadings = (wrapper: VueWrapper) =>
      wrapper
        .findAll('.inspection-report__side > section.section-card')
        .map((card) => card.find('h2').text())

    expect(sideHeadings(loading.wrapper)).toEqual(['Job details', 'Status history'])
    loading.wrapper.unmount()
    mounted = null

    const { wrapper } = await openApp(
      reportPath(boilerJobId),
      reportRoutes(boilerJob(), boilerInspection()),
    )
    expect(sideHeadings(wrapper)).toEqual(['Job details', 'Status history'])
    expect(wrapper.findAll('.inspection-report__side > *')).toHaveLength(2)
  })

  it('keeps the results table at 640px inside its own scroll region', async () => {
    const { wrapper } = await openApp(
      reportPath(boilerJobId),
      reportRoutes(boilerJob(), boilerInspection()),
    )

    expect(resultsRegion(wrapper).find('table').classes()).toContain('data-table__table--min-640')
    expect(resultsRegion(wrapper).element.closest('.data-table')?.classList).toContain(
      'data-table--in-card',
    )
  })
})

describe('live updates on the inspection report', () => {
  it('refetches both calls on connect and shows the completion in place (W5-AC9)', async () => {
    const { wrapper, stub, socket } = await liveApp(reportPath(rooftopJobId), {
      [jobRoute(rooftopJobId)]: [
        json(assignedInProgressJob()),
        json(assignedInProgressJob()),
        json(completedRooftopJob()),
      ],
      [inspectionRoute(rooftopJobId)]: [
        json(rooftopInProgress()),
        json(rooftopInProgress()),
        json(rooftopSubmitted()),
      ],
    })
    expect(stub.requestsTo(jobRoute(rooftopJobId))).toHaveLength(2)
    expect(stub.requestsTo(inspectionRoute(rooftopJobId))).toHaveLength(2)
    expect(wrapper.find('header .live-pill').attributes('data-state')).toBe('live')
    const heading = wrapper.find<HTMLElement>('h1').element
    heading.focus()
    const statuses = wrapper.findAll('main [role="status"]').map((status) => status.text())

    socket.emitServerEvent(
      'job.status-changed',
      jobEvent({
        jobId: rooftopJobId,
        jobNumber: 1042,
        status: 'Completed',
        previousStatus: 'InProgress',
      }),
    )
    await flushPromises()

    expect(stub.requestsTo(jobRoute(rooftopJobId))).toHaveLength(3)
    expect(stub.requestsTo(inspectionRoute(rooftopJobId))).toHaveLength(3)
    expect(metaParts(wrapper)).toEqual(['Completed', 'High', 'Completed 10:58 AM by Marco Reyes'])
    expect(noticeOf(wrapper).exists()).toBe(false)
    expect(shownTiles(wrapper).map((tile) => tile.value)).toEqual([
      '8 Pass 1 Fail 1 N/A',
      '1 h 46 min',
      '10:58 AM',
    ])
    expect(wrapper.find('.inspection-report__notes').text()).toBe(rooftopNotes)
    expect(historyLines(wrapper).slice(-1)).toEqual(['10:58 AM Completed by Marco Reyes'])
    expect(wrapper.find('.kpi-tile__skeleton').exists()).toBe(false)
    expect(resultsRegion(wrapper).find('table').attributes('aria-busy')).toBeUndefined()
    expect(wrapper.findAll('main [role="status"]').map((status) => status.text())).toEqual(statuses)
    expect(document.activeElement).toBe(heading)
    expect(wrapper.find('[aria-label="Notifications"] .toast__title').text()).toBe(
      'Marco Reyes completed a job',
    )
    expect(wrapper.find('.data-table__row--updated').exists()).toBe(false)

    vi.advanceTimersByTime(listDebounceMilliseconds)
    await flushPromises()
    expect(stub.requestsTo(inspectionRoute(rooftopJobId))).toHaveLength(3)
    expect(wrapper.find('.data-table__row--updated').exists()).toBe(false)
  })

  it('asks no inspection for an event about another job', async () => {
    const otherJob = jobDetails({ updatedAt: demoToday.toISOString() })
    const { stub, socket } = await liveApp(reportPath(rooftopJobId), {
      ...reportRoutes(assignedInProgressJob(), rooftopInProgress()),
      [jobRoute(otherJob.id)]: json(otherJob),
    })

    socket.emitServerEvent('job.created', jobEvent({ jobId: otherJob.id, jobNumber: 1051 }))
    await flushPromises()
    vi.advanceTimersByTime(listDebounceMilliseconds)
    await flushPromises()

    expect(stub.requestsTo(jobRoute(otherJob.id))).toHaveLength(1)
    expect(stub.requestsTo(inspectionRoute(rooftopJobId))).toHaveLength(2)
    expect(stub.requestsTo(jobRoute(rooftopJobId))).toHaveLength(2)
  })

  it('keeps the report silently when an event refetch fails', async () => {
    const { wrapper, socket } = await liveApp(reportPath(rooftopJobId), {
      [jobRoute(rooftopJobId)]: [
        json(assignedInProgressJob()),
        json(assignedInProgressJob()),
        json(completedRooftopJob()),
      ],
      [inspectionRoute(rooftopJobId)]: [
        json(rooftopInProgress()),
        json(rooftopInProgress()),
        problem(500, 'server.error'),
      ],
    })

    socket.emitServerEvent('job.status-changed', jobEvent({ jobId: rooftopJobId, jobNumber: 1042 }))
    await flushPromises()

    expect(noticeOf(wrapper).text()).toContain('Not submitted yet.')
    expect(shownTiles(wrapper)[0]?.subline).toBe('6 of 10 answered')
    expect(wrapper.find('.connection-banner__banner').exists()).toBe(false)
    expect(wrapper.find('main .banner--error').exists()).toBe(false)
  })

  it('shows its own Refresh failed copy over the kept report when the connect refetch fails', async () => {
    const { wrapper, stub } = await liveApp(reportPath(boilerJobId), {
      [jobRoute(boilerJobId)]: json(boilerJob()),
      [inspectionRoute(boilerJobId)]: [
        json(boilerInspection()),
        problem(500, 'server.error'),
        json(boilerInspection()),
      ],
    })

    const banner = wrapper.find('.connection-banner__banner')
    expect(banner.attributes('data-notice')).toBe('refresh-failed')
    expect(banner.text()).toContain(
      'The inspection report could not be refreshed. Showing it as of 10:42 AM.',
    )
    expect(banner.text()).toMatch(/Reference [0-9a-f-]{36}/)
    expect(wrapper.find('h1').text()).toBe('Boiler pressure drop')
    expect(rowCells(wrapper)).toHaveLength(8)

    await banner.find('button').trigger('click')
    await flushPromises()

    expect(stub.requestsTo(inspectionRoute(boilerJobId))).toHaveLength(3)
    expect(wrapper.find('.connection-banner__banner').exists()).toBe(false)
  })

  it('keeps a newer report when an older refetch lands after it', async () => {
    const older = deferred()
    const { wrapper, socket } = await liveApp(reportPath(rooftopJobId), {
      [jobRoute(rooftopJobId)]: [
        json(assignedInProgressJob()),
        json(assignedInProgressJob()),
        json(completedRooftopJob()),
      ],
      [inspectionRoute(rooftopJobId)]: [
        json(rooftopInProgress()),
        older.reply,
        json(rooftopSubmitted()),
      ],
    })

    socket.emitServerEvent('job.status-changed', jobEvent({ jobId: rooftopJobId, jobNumber: 1042 }))
    await flushPromises()
    expect(shownTiles(wrapper)[2]?.label).toBe('Completed')

    older.resolve(jsonResponse(rooftopInProgress()))
    await flushPromises()

    expect(shownTiles(wrapper)[2]?.label).toBe('Completed')
    expect(noticeOf(wrapper).exists()).toBe(false)
  })
})

describe('links between the report and the job page', () => {
  const completedSummary = jobSummary({
    id: boilerJobId,
    number: 1039,
    title: 'Boiler pressure drop',
    status: 'Completed',
  })

  it('sends a completed title in the Jobs table to its report and every other title to the job', async () => {
    const open = jobSummary()
    const { wrapper } = await openApp('/jobs', {
      'GET /api/jobs': json(jobList([completedSummary, open], { totalCount: 2 })),
    })

    const links = wrapper.findAll('.jobs-table__title')
    expect(links.map((link) => [link.text(), link.attributes('href')])).toEqual([
      ['Boiler pressure drop', `/jobs/${boilerJobId}/inspection`],
      ['Replace faulty breaker panel', `/jobs/${open.id}`],
    ])
  })

  it('opens the report from a completed title in the Jobs table', async () => {
    const { wrapper, router } = await openApp('/jobs', {
      'GET /api/jobs': json(jobList([completedSummary], { totalCount: 1 })),
      ...reportRoutes(boilerJob(), boilerInspection()),
    })

    await wrapper.find('.jobs-table__title').trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.name).toBe('job-inspection')
    expect(wrapper.find('h1').text()).toBe('Boiler pressure drop')
    expect(visibleText(wrapper.find('.page-header__eyebrow').element)).toBe(
      'JOB-1039 · Inspection report',
    )
  })

  it('puts Inspection report in the header of a completed job', async () => {
    const { wrapper } = await openApp(`/jobs/${boilerJobId}`, {
      [jobRoute(boilerJobId)]: json(boilerJob()),
    })

    const link = wrapper.find('.page-header__actions a')
    expect(link.text()).toBe('Inspection report')
    expect(link.attributes('href')).toBe(`/jobs/${boilerJobId}/inspection`)
    expect(link.classes()).toEqual(
      expect.arrayContaining(['app-button--secondary', 'app-button--small']),
    )
    expect(link.find('svg').element.innerHTML).toBe(iconMarkup('clipboard-check', 16))
  })

  it.each([
    ['in progress', assignedInProgressJob()],
    ['cancelled', cancelledFilterJob()],
    ['assigned', assignedFreezerJob()],
  ])('leaves the link out on a job %s', async (_name, job) => {
    const { wrapper } = await openApp(`/jobs/${job.id}`, { [jobRoute(job.id)]: json(job) })

    expect(wrapper.find('h1').text()).toBe(job.title)
    expect(wrapper.find('.page-header__actions').exists()).toBe(false)
  })

  it('goes from the job page to its report and back', async () => {
    const { wrapper, router } = await openApp(
      `/jobs/${boilerJobId}`,
      reportRoutes(boilerJob(), boilerInspection()),
    )

    await wrapper.find('.page-header__actions a').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.fullPath).toBe(`/jobs/${boilerJobId}/inspection`)
    expect(shownTiles(wrapper)[2]).toEqual({
      label: 'Completed',
      value: '8:52 AM',
      subline: 'by Tom Becker',
    })

    await wrapper.find('.page-header__actions a').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.fullPath).toBe(`/jobs/${boilerJobId}`)
    expect(wrapper.find('.page-header__number').text()).toBe('JOB-1039')
    expect(document.title).toBe('JOB-1039 · FieldService Admin')
  })

  it('adds the link to the job page when the job completes live', async () => {
    const { wrapper, socket } = await liveApp(`/jobs/${rooftopJobId}`, {
      [jobRoute(rooftopJobId)]: [
        json(assignedInProgressJob()),
        json(assignedInProgressJob()),
        json(completedRooftopJob()),
      ],
    })
    expect(wrapper.find('.page-header__actions').exists()).toBe(false)

    socket.emitServerEvent('job.status-changed', jobEvent({ jobId: rooftopJobId, jobNumber: 1042 }))
    await flushPromises()

    expect(wrapper.find('.page-header__actions a').text()).toBe('Inspection report')
  })
})
