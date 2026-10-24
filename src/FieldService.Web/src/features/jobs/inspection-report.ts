import type { ApiError } from '@/shared/api/api-error'
import {
  dayOffset,
  formatDuration,
  formatMomentUnbroken,
  formatShortDate,
  formatTime,
  formatTimeWindow,
  isSameLocalDay,
  noBreakSpace,
  separator,
  unbroken,
  type Instant,
} from '@/shared/format/date-time'
import type { InspectionResult, JobHistoryAction } from '@/shared/format/labels'
import type { IconName } from '@/shared/ui/icons'

import type { Inspection, InspectionAnswer, InspectionItem, JobDetails } from './jobs-api'

export interface ResultCount {
  readonly result: InspectionResult
  readonly count: number
}

export type SummaryValue =
  | { readonly kind: 'text'; readonly text: string }
  | { readonly kind: 'results'; readonly counts: readonly ResultCount[] }
  | { readonly kind: 'unavailable' }

export type SummaryTileKey = 'result' | 'time-on-site' | 'completed' | 'last-saved' | 'cancelled'

export interface SummaryTile {
  readonly key: SummaryTileKey
  readonly label: string
  readonly icon: IconName
  readonly value: SummaryValue
  readonly subline: readonly string[]
}

export interface ReportReading {
  readonly id: string
  readonly label: string
  readonly value: string
}

export interface ReportRow {
  readonly id: string
  readonly number: string
  readonly prompt: string
  readonly result: InspectionResult | null
  readonly readings: readonly ReportReading[]
  readonly note: string | null
}

export interface ReportNotice {
  readonly title: string
  readonly message: string
  readonly icon: IconName
}

export interface TechnicianNotes {
  readonly text: string
  readonly empty: boolean
}

const minuteMilliseconds = 60_000

const unavailable: SummaryValue = { kind: 'unavailable' }

const minusSign = '−'

function text(value: string): SummaryValue {
  return { kind: 'text', text: value }
}

function trimmedOrNull(value: string | null | undefined): string | null {
  const trimmed = value?.trim() ?? ''
  return trimmed.length > 0 ? trimmed : null
}

function latestEntry(job: JobDetails, action: JobHistoryAction) {
  return [...job.history].reverse().find((entry) => entry.action === action) ?? null
}

export function formatReadingValue(value: number): string {
  const magnitude = Math.abs(value)
    .toFixed(2)
    .replace(/(\.\d*?)0+$/, '$1')
    .replace(/\.$/, '')
  return value < 0 && magnitude !== '0' ? `${minusSign}${magnitude}` : magnitude
}

export function formatReading(value: number, unit: string): string {
  const number = formatReadingValue(value)
  const symbol = unit.trim()
  return symbol.length > 0 ? `${number}${noBreakSpace}${symbol}` : number
}

export function formatTimeOnSite(milliseconds: number): string {
  return milliseconds < minuteMilliseconds ? 'Less than a minute' : formatDuration(milliseconds)
}

function minuteOf(value: Instant): number {
  const time = typeof value === 'string' ? Date.parse(value) : value.getTime()
  return Math.floor(time / minuteMilliseconds) * minuteMilliseconds
}

export function timeOnSiteBetween(start: Instant, end: Instant): string {
  return formatTimeOnSite(minuteOf(end) - minuteOf(start))
}

export function millisecondsToNextMinute(now: Date): number {
  return minuteOf(now) + minuteMilliseconds - now.getTime()
}

export function formatTimeRange(start: Instant, end: Instant, now: Date): string {
  if (isSameLocalDay(start, end)) {
    const window = unbroken(formatTimeWindow(start, end, now))
    return dayOffset(start, now) === 0
      ? window
      : `${unbroken(formatShortDate(start, now))}, ${window}`
  }
  return `${formatMomentUnbroken(start, now)}${noBreakSpace}\u{2013} ${formatMomentUnbroken(end, now)}`
}

export function itemCountText(count: number): string {
  return count === 1 ? '1 item' : `${count} items`
}

export function answeredText(inspection: Inspection): string {
  return `${inspection.progress.answered} of ${inspection.progress.total} answered`
}

export function resultCounts(inspection: Inspection): ResultCount[] {
  const { passCount, failCount, notApplicableCount } = inspection.progress
  return [
    { result: 'Pass', count: passCount },
    { result: 'Fail', count: failCount },
    { result: 'NotApplicable', count: notApplicableCount },
  ]
}

function momentValue(
  at: Instant,
  actor: string | null,
  now: Date,
): Pick<SummaryTile, 'value' | 'subline'> {
  const by = actor === null ? null : `by ${actor}`
  if (dayOffset(at, now) === 0) {
    return { value: text(unbroken(formatTime(at))), subline: by === null ? [] : [by] }
  }
  const time = `at ${unbroken(formatTime(at))}`
  return {
    value: text(unbroken(formatShortDate(at, now))),
    subline: [by === null ? time : `${time} ${by}`],
  }
}

function resultTile(job: JobDetails, inspection: Inspection): SummaryTile {
  const base = { key: 'result', label: 'Result', icon: 'clipboard-check' } as const
  const counts: SummaryValue = { kind: 'results', counts: resultCounts(inspection) }
  if (job.status === 'Completed') {
    return {
      ...base,
      value: counts,
      subline: [inspection.template.name, itemCountText(inspection.progress.total)],
    }
  }
  return {
    ...base,
    value: inspection.progress.answered > 0 ? counts : unavailable,
    subline: [answeredText(inspection)],
  }
}

function timeOnSiteTile(job: JobDetails, now: Date): SummaryTile {
  const base = { key: 'time-on-site', label: 'Time on site', icon: 'clock' } as const
  const { startedAt, completedAt } = job
  switch (job.status) {
    case 'Completed':
      if (startedAt !== null && completedAt !== null) {
        return {
          ...base,
          value: text(timeOnSiteBetween(startedAt, completedAt)),
          subline: [formatTimeRange(startedAt, completedAt, now)],
        }
      }
      break
    case 'InProgress':
    case 'OnHold':
      if (startedAt !== null) {
        return {
          ...base,
          value: text(timeOnSiteBetween(startedAt, now)),
          subline: [`Since ${formatMomentUnbroken(startedAt, now)}`],
        }
      }
      break
    case 'Cancelled':
      return { ...base, value: unavailable, subline: ['Not completed'] }
    default:
      break
  }
  return { ...base, value: unavailable, subline: ['Not started'] }
}

function closingTile(job: JobDetails, inspection: Inspection, now: Date): SummaryTile {
  if (job.status === 'Completed') {
    const entry = latestEntry(job, 'Completed')
    const at = entry?.occurredAt ?? job.completedAt
    const actor = entry?.actor.displayName ?? job.assignedTechnician?.displayName ?? null
    const base = { key: 'completed', label: 'Completed', icon: 'check-circle' } as const
    return at === null
      ? { ...base, value: unavailable, subline: [] }
      : { ...base, ...momentValue(at, actor, now) }
  }
  if (job.status === 'Cancelled') {
    const entry = latestEntry(job, 'Cancelled')
    const at = entry?.occurredAt ?? job.cancelledAt
    const base = { key: 'cancelled', label: 'Cancelled', icon: 'x-circle' } as const
    return at === null
      ? { ...base, value: unavailable, subline: [] }
      : { ...base, ...momentValue(at, entry?.actor.displayName ?? null, now) }
  }
  const base = { key: 'last-saved', label: 'Last saved', icon: 'activity' } as const
  if (inspection.updatedAt === null) {
    return { ...base, value: unavailable, subline: ['No answers saved yet'] }
  }
  return {
    ...base,
    ...momentValue(inspection.updatedAt, job.assignedTechnician?.displayName ?? null, now),
  }
}

export function summaryTiles(job: JobDetails, inspection: Inspection, now: Date): SummaryTile[] {
  return [resultTile(job, inspection), timeOnSiteTile(job, now), closingTile(job, inspection, now)]
}

export function isTimeOnSiteRunning(job: JobDetails): boolean {
  return (job.status === 'InProgress' || job.status === 'OnHold') && job.startedAt !== null
}

export function reportMeta(job: JobDetails, now: Date): string {
  const completed = job.status === 'Completed' ? latestEntry(job, 'Completed') : null
  return completed === null
    ? `Created ${formatMomentUnbroken(job.createdAt, now)} by ${job.createdBy.displayName}`
    : `Completed ${formatMomentUnbroken(completed.occurredAt, now)} by ${completed.actor.displayName}`
}

export function reportNotice(status: string): ReportNotice | null {
  switch (status) {
    case 'Unassigned':
    case 'Assigned':
      return {
        title: 'Not started yet.',
        message: 'The checklist has no answers.',
        icon: 'clock',
      }
    case 'InProgress':
    case 'OnHold':
      return {
        title: 'Not submitted yet.',
        message: 'This report shows the answers saved so far.',
        icon: 'clock',
      }
    case 'Cancelled':
      return {
        title: 'This job was cancelled.',
        message: 'Its checklist was not submitted.',
        icon: 'x-circle',
      }
    default:
      return null
  }
}

export function technicianNotes(inspection: Inspection): TechnicianNotes {
  if (inspection.jobStatus === 'Cancelled') {
    return { text: 'No notes from the technician.', empty: true }
  }
  const notes = trimmedOrNull(inspection.technicianNotes)
  if (notes !== null) {
    return { text: notes, empty: false }
  }
  return inspection.submittedAt === null
    ? { text: 'Notes are added when the job is submitted.', empty: true }
    : { text: 'No notes from the technician.', empty: true }
}

function readingsOf(item: InspectionItem, answer: InspectionAnswer): ReportReading[] {
  const fields = [...item.readingFields].sort((first, second) => first.position - second.position)
  return fields.flatMap((field) => {
    const reading = answer.readings.find((entry) => entry.readingFieldId === field.readingFieldId)
    return reading === undefined
      ? []
      : [
          {
            id: field.readingFieldId,
            label: field.label,
            value: formatReading(reading.value, field.unit),
          },
        ]
  })
}

export function reportRows(inspection: Inspection): ReportRow[] {
  const width = Math.max(2, String(inspection.items.length).length)
  return [...inspection.items]
    .sort((first, second) => first.position - second.position)
    .map((item) => ({
      id: item.checklistItemId,
      number: String(item.position).padStart(width, '0'),
      prompt: item.prompt,
      result: item.answer?.result ?? null,
      readings: item.answer === null ? [] : readingsOf(item, item.answer),
      note: trimmedOrNull(item.answer?.note),
    }))
}

export function inspectionHeading(inspection: Inspection): string {
  return `Inspection${separator}${inspection.template.name}`
}

export function loadFailureMessage(error: ApiError | null): string {
  if (error === null || error.status !== 429) {
    return 'Check your connection, then try again.'
  }
  const seconds = error.retryAfterSeconds ?? 60
  return seconds === 1 ? 'Try again in 1 second.' : `Try again in ${seconds} seconds.`
}
