import type { Locator, Page, Request } from '@playwright/test'

import type {
  DashboardOpenJobs,
  DashboardRange,
  DashboardSummary,
  DayWindow,
  JobStatus,
  Technician,
} from './api.ts'
import { clockPattern, dashboardTiles } from './copy.ts'

export const summaryUrl = /\/api\/dashboard\/summary\?/

export const techniciansUrl = /\/api\/technicians$/

export interface ExpectedTile {
  readonly status: JobStatus
  readonly label: string
  readonly value: number
  readonly subline: string | null
}

export function isDashboardSummary(request: Request): boolean {
  return request.method() === 'GET' && new URL(request.url()).pathname === '/api/dashboard/summary'
}

export function isTechniciansRead(request: Request): boolean {
  return request.method() === 'GET' && new URL(request.url()).pathname === '/api/technicians'
}

export function isCancellation(request: Request, jobId: string): boolean {
  return (
    request.method() === 'POST' &&
    new URL(request.url()).pathname === `/api/jobs/${jobId}/cancellation`
  )
}

export function isClientErrorReport(request: Request): boolean {
  return request.method() === 'POST' && new URL(request.url()).pathname === '/api/client-errors'
}

export function trackRequests(page: Page, matches: (request: Request) => boolean): Request[] {
  const requests: Request[] = []
  page.on('request', (request) => {
    if (matches(request)) {
      requests.push(request)
    }
  })
  return requests
}

export function utcDayWindow(now: Date = new Date()): DayWindow {
  const start = Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate())
  return {
    from: new Date(start).toISOString(),
    to: new Date(start + 86_400_000).toISOString(),
  }
}

function openJobsText(total: number): string {
  return total === 1 ? '1 open job.' : `${total} open jobs.`
}

export function heroSentence(openJobs: DashboardOpenJobs, range: DashboardRange): string {
  const total = openJobs.total
  if (total <= 0) {
    return 'No open jobs.'
  }
  const first = openJobsText(total)
  const unassigned = openJobs.unassigned
  if (unassigned <= 0) {
    return total === 1 ? `${first} It has a technician.` : `${first} Every one has a technician.`
  }
  const today = range.unassignedScheduled
  if (unassigned === 1) {
    return today >= 1 ? `${first} 1 needs a technician, today.` : `${first} 1 needs a technician.`
  }
  if (today <= 0) {
    return `${first} ${unassigned} need a technician.`
  }
  if (today >= unassigned) {
    return `${first} ${unassigned} need a technician, all of them today.`
  }
  return `${first} ${unassigned} need a technician, ${today} of them today.`
}

function sublineOf(status: JobStatus, summary: DashboardSummary): string | null {
  const { openJobs, range } = summary
  switch (status) {
    case 'Unassigned':
      if (openJobs.unassigned <= 0) {
        return null
      }
      return range.unassignedScheduled <= 0
        ? 'None scheduled today'
        : `${range.unassignedScheduled} scheduled today`
    case 'Assigned':
      return openJobs.nextAssignedStart === null ? null : 'Next starts .+'
    case 'InProgress':
      if (openJobs.inProgressTechnicianCount <= 0) {
        return null
      }
      return openJobs.inProgressTechnicianCount === 1
        ? 'Across 1 technician'
        : `Across ${openJobs.inProgressTechnicianCount} technicians`
    case 'OnHold':
      return openJobs.onHoldSince === null ? null : 'Oldest since .+'
    case 'Completed':
      return range.lastCompletedAt === null ? null : `Last at ${clockPattern}`
    default:
      return 'Today'
  }
}

function valueOf(status: JobStatus, summary: DashboardSummary): number {
  const values: Readonly<Record<JobStatus, number>> = {
    Unassigned: summary.openJobs.unassigned,
    Assigned: summary.openJobs.assigned,
    InProgress: summary.openJobs.inProgress,
    OnHold: summary.openJobs.onHold,
    Completed: summary.range.completed,
    Cancelled: summary.range.cancelled,
  }
  return values[status]
}

export function expectedTiles(summary: DashboardSummary): ExpectedTile[] {
  return dashboardTiles.map((tile) => ({
    status: tile.status,
    label: tile.label,
    value: valueOf(tile.status, summary),
    subline: sublineOf(tile.status, summary),
  }))
}

export function emptySummary(window: DayWindow): DashboardSummary {
  return {
    generatedAt: new Date().toISOString(),
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
    range: {
      from: window.from,
      to: window.to,
      created: 0,
      completed: 0,
      cancelled: 0,
      unassignedScheduled: 0,
      lastCompletedAt: null,
    },
    technicians: { total: 0, onShift: 0 },
    needsTechnician: [],
    recentActivity: [],
  }
}

export function shiftLabelOf(technician: Technician): 'On shift' | 'Off shift' | 'Inactive' {
  if (!technician.isActive) {
    return 'Inactive'
  }
  return technician.isOnShift ? 'On shift' : 'Off shift'
}

export function isOnShift(technician: Technician): boolean {
  return technician.isActive && technician.isOnShift
}

export interface HeldRequest {
  release(): void
}

export async function holdNext(page: Page, url: RegExp): Promise<HeldRequest> {
  let release: () => void = () => undefined
  const held = new Promise<void>((resolve) => {
    release = resolve
  })
  await page.route(
    url,
    async (route) => {
      await held
      await route.continue()
    },
    { times: 1 },
  )
  return { release: () => release() }
}

export interface BrokenText {
  readonly text: string
  readonly lines: number
}

export async function brokenTimesAndDates(scope: Locator): Promise<{
  readonly checked: number
  readonly broken: readonly BrokenText[]
}> {
  return scope.evaluate((root) => {
    const pattern =
      /\d{1,2}:\d{2}\s[AP]M|(?:Mon|Tue|Wed|Thu|Fri|Sat|Sun)\s\d{1,2}\s(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)/g
    const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT)
    const broken: { text: string; lines: number }[] = []
    let checked = 0
    for (let node = walker.nextNode(); node !== null; node = walker.nextNode()) {
      const text = node.textContent ?? ''
      const parent = node.parentElement
      if (parent === null || parent.getClientRects().length === 0) {
        continue
      }
      for (const match of text.matchAll(pattern)) {
        const range = document.createRange()
        range.setStart(node, match.index)
        range.setEnd(node, match.index + match[0].length)
        const tops = new Set(
          [...range.getClientRects()]
            .filter((rect) => rect.width > 0)
            .map((rect) => Math.round(rect.top)),
        )
        checked += 1
        if (tops.size > 1) {
          broken.push({ text: match[0], lines: tops.size })
        }
      }
    }
    return { checked, broken }
  })
}

export interface Overflow {
  readonly row: number
  readonly by: number
}

export async function overflowsOfRows(
  rows: Locator,
  childSelector: string,
): Promise<{ readonly checked: number; readonly overflows: readonly Overflow[] }> {
  return rows.evaluateAll((elements, selector) => {
    const overflows: { row: number; by: number }[] = []
    let checked = 0
    elements.forEach((row, index) => {
      const style = getComputedStyle(row)
      const box = row.getBoundingClientRect()
      const contentRight = box.right - Number.parseFloat(style.paddingRight)
      for (const child of row.querySelectorAll(selector)) {
        const range = document.createRange()
        range.selectNodeContents(child)
        const right = Math.max(...[...range.getClientRects()].map((rect) => rect.right))
        checked += 1
        if (right > contentRight + 0.5) {
          overflows.push({ row: index + 1, by: Math.round((right - contentRight) * 100) / 100 })
        }
      }
    })
    return { checked, overflows }
  }, childSelector)
}
