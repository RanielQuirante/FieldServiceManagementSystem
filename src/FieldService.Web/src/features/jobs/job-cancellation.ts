import { dayOffset, formatShortDate, formatTime, unbroken } from '@/shared/format/date-time'
import { formatJobNumber } from '@/shared/format/job-number'

import type { JobDetails } from './jobs-api'
import { newestCancellation, type JobNoticeCode } from './jobs-store'

export interface CancelledNotice {
  readonly title: string
  readonly reason: string | null
}

export interface JobNoticeText {
  readonly title: string
  readonly message: string
}

export function cancelledMoment(at: string, now: Date): string {
  const time = unbroken(formatTime(at))
  return dayOffset(at, now) === 0
    ? `Cancelled at ${time}`
    : `Cancelled on ${unbroken(formatShortDate(at, now))} at ${time}`
}

export function cancelledNotice(job: JobDetails, now: Date): CancelledNotice | null {
  if (job.status !== 'Cancelled') {
    return null
  }
  const entry = newestCancellation(job)
  const at = job.cancelledAt ?? entry?.occurredAt ?? null
  if (at === null) {
    return null
  }
  const by = entry === null ? '' : ` by ${entry.actor.displayName}`
  const note = entry?.note ?? null
  return {
    title: `${cancelledMoment(at, now)}${by}.`,
    reason: note === null || note.length === 0 ? null : `“${note}”`,
  }
}

export function jobNoticeText(code: JobNoticeCode, jobNumber: number): JobNoticeText {
  const number = formatJobNumber(jobNumber)
  switch (code) {
    case 'job.not-assignable':
      return {
        title: `${number} can no longer be assigned.`,
        message: 'Its status changed after you opened it. The latest version is shown.',
      }
    case 'job.invalid-transition':
      return {
        title: `${number} can no longer be cancelled.`,
        message: 'Its status changed after you opened it. The latest version is shown.',
      }
    case 'already-cancelled':
      return {
        title: `${number} was already cancelled.`,
        message: 'The status history shows who cancelled it and why.',
      }
    default:
      return {
        title: `${number} changed after you opened it.`,
        message: 'The latest version is shown. Check it, then try again.',
      }
  }
}
