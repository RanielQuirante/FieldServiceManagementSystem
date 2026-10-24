import {
  dayOffset,
  formatShortDate,
  formatTime,
  formatTimeWindow,
  formatWeekday,
  unbroken,
  type Instant,
} from '@/shared/format/date-time'
import type { JobHistoryAction, JobStatus } from '@/shared/format/labels'

import type {
  DashboardActivityEntry,
  DashboardOpenJobs,
  DashboardRange,
  DashboardSummary,
  DashboardTechnicianCounts,
  DashboardUnassignedJob,
} from './dashboard-api'

export type Greeting = 'Good morning' | 'Good afternoon' | 'Good evening'

export type DashboardPartState = 'loading' | 'ready' | 'error'

export interface StatusTile {
  readonly status: JobStatus
  readonly label: string
  readonly value: number
  readonly subline: string | null
}

export interface ScheduleParts {
  readonly day: string
  readonly window: string
}

export interface ActivitySentence {
  readonly actor: string
  readonly before: string
  readonly after: string
}

const morningHour = 5

const afternoonHour = 12

const eveningHour = 18

const recentDays = 6

export function greetingFor(now: Date): Greeting {
  const hour = now.getHours()
  if (hour >= morningHour && hour < afternoonHour) {
    return 'Good morning'
  }
  return hour >= afternoonHour && hour < eveningHour ? 'Good afternoon' : 'Good evening'
}

export function greetingText(now: Date, displayName: string | null): string {
  const greeting = greetingFor(now)
  const name = displayName?.trim() ?? ''
  return name.length === 0 ? greeting : `${greeting}, ${name}`
}

export function nextGreetingChange(now: Date): Date {
  const year = now.getFullYear()
  const month = now.getMonth()
  const day = now.getDate()
  const candidates = [morningHour, afternoonHour, eveningHour]
    .map((hour) => new Date(year, month, day, hour))
    .filter((moment) => moment.getTime() > now.getTime())
  return candidates[0] ?? new Date(year, month, day + 1, morningHour)
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

export function onShiftFigure(technicians: DashboardTechnicianCounts): string {
  return `${technicians.onShift} of ${technicians.total}`
}

export function jobsByStatusSubline(summary: DashboardSummary): string {
  return `${summary.openJobs.total} open, ${summary.range.completed} completed and ${summary.range.cancelled} cancelled today`
}

function moment(at: Instant, now: Date): string {
  const time = unbroken(formatTime(at))
  return dayOffset(at, now) === 0 ? time : `${unbroken(formatShortDate(at, now))}, ${time}`
}

export function unassignedSubline(
  openJobs: DashboardOpenJobs,
  range: DashboardRange,
): string | null {
  if (openJobs.unassigned <= 0) {
    return null
  }
  const today = range.unassignedScheduled
  return today <= 0 ? 'None scheduled today' : `${today} scheduled today`
}

export function assignedSubline(openJobs: DashboardOpenJobs, now: Date): string | null {
  const start = openJobs.nextAssignedStart
  return start === null ? null : `Next starts ${moment(start, now)}`
}

export function inProgressSubline(openJobs: DashboardOpenJobs): string | null {
  const count = openJobs.inProgressTechnicianCount
  if (count <= 0) {
    return null
  }
  return count === 1 ? 'Across 1 technician' : `Across ${count} technicians`
}

export function onHoldSubline(openJobs: DashboardOpenJobs, now: Date): string | null {
  const since = openJobs.onHoldSince
  if (since === null) {
    return null
  }
  const offset = dayOffset(since, now)
  const time = unbroken(formatTime(since))
  if (offset === 0) {
    return `Oldest since ${time}`
  }
  if (offset < 0 && offset >= -recentDays) {
    return `Oldest since ${formatWeekday(since)} ${time}`
  }
  return `Oldest since ${unbroken(formatShortDate(since, now))}`
}

export function completedSubline(range: DashboardRange): string | null {
  const last = range.lastCompletedAt
  return last === null ? null : `Last at ${unbroken(formatTime(last))}`
}

export const statusTileOrder: readonly Pick<StatusTile, 'status' | 'label'>[] = [
  { status: 'Unassigned', label: 'Unassigned' },
  { status: 'Assigned', label: 'Assigned' },
  { status: 'InProgress', label: 'In progress' },
  { status: 'OnHold', label: 'On hold' },
  { status: 'Completed', label: 'Completed today' },
  { status: 'Cancelled', label: 'Cancelled' },
]

export function statusTiles(summary: DashboardSummary, now: Date): StatusTile[] {
  const { openJobs, range } = summary
  const figures: Readonly<Record<JobStatus, readonly [number, string | null]>> = {
    Unassigned: [openJobs.unassigned, unassignedSubline(openJobs, range)],
    Assigned: [openJobs.assigned, assignedSubline(openJobs, now)],
    InProgress: [openJobs.inProgress, inProgressSubline(openJobs)],
    OnHold: [openJobs.onHold, onHoldSubline(openJobs, now)],
    Completed: [range.completed, completedSubline(range)],
    Cancelled: [range.cancelled, 'Today'],
  }
  return statusTileOrder.map((tile) => {
    const [value, subline] = figures[tile.status]
    return { ...tile, value, subline }
  })
}

export function scheduleLine(job: DashboardUnassignedJob, now: Date): string {
  const parts = scheduleParts(job, now)
  return `${parts.day} ${parts.window}`
}

export function scheduleParts(job: DashboardUnassignedJob, now: Date): ScheduleParts {
  const day =
    dayOffset(job.scheduledStart, now) === 0
      ? 'Today'
      : `${formatShortDate(job.scheduledStart, now)},`
  return {
    day: unbroken(day),
    window: unbroken(formatTimeWindow(job.scheduledStart, job.scheduledEnd, now)),
  }
}

export function viewAllLabel(unassigned: number): string {
  return unassigned === 1 ? 'View all 1 unassigned job' : `View all ${unassigned} unassigned jobs`
}

const verbs: Readonly<Record<JobHistoryAction, readonly [string, string]>> = {
  Created: ['created', ''],
  Assigned: ['assigned', ''],
  Started: ['started', ''],
  PutOnHold: ['put', ' on hold'],
  Resumed: ['resumed', ''],
  Completed: ['completed', ''],
  Cancelled: ['cancelled', ''],
}

export function activitySentence(entry: DashboardActivityEntry): ActivitySentence {
  const [verb, rest] = verbs[entry.action] ?? ['updated', '']
  const technician = entry.action === 'Assigned' ? entry.technician : null
  return {
    actor: entry.actor.displayName,
    before: ` ${verb} `,
    after: technician === null ? rest : ` to ${technician.displayName}`,
  }
}

export function activityNote(entry: DashboardActivityEntry): string | null {
  return entry.note === null || entry.note.length === 0 ? null : `“${entry.note}”`
}

export function activityTime(entry: DashboardActivityEntry, now: Date): string {
  return moment(entry.occurredAt, now)
}
