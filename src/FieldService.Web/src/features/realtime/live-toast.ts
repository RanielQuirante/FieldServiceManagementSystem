import type { ToastRequest } from '@/shared/composables/use-toasts'
import { formatMoment } from '@/shared/format/date-time'
import { formatJobNumber } from '@/shared/format/job-number'
import { jobStatusLabel, type JobHistoryAction } from '@/shared/format/labels'

import type { LiveJob, LiveJobHistoryEntry } from './realtime-api'

export const justNowMilliseconds = 60_000

export const toastNoteLength = 80

type TitleWriter = (actor: string, entry: LiveJobHistoryEntry) => string

const titleWriters: Readonly<Record<JobHistoryAction, TitleWriter>> = {
  Created: (actor) => `${actor} created a job`,
  Assigned: (actor, entry) =>
    entry.technician === null
      ? `${actor} assigned a job`
      : `${actor} assigned a job to ${entry.technician.displayName}`,
  Started: (actor) => `${actor} started work`,
  PutOnHold: (actor) => `${actor} put a job on hold`,
  Resumed: (actor) => `${actor} resumed work`,
  Completed: (actor) => `${actor} completed a job`,
  Cancelled: (actor) => `${actor} cancelled a job`,
}

const notedActions: ReadonlySet<string> = new Set<JobHistoryAction>(['PutOnHold', 'Cancelled'])

export function latestHistoryEntry(job: LiveJob): LiveJobHistoryEntry | null {
  let latest: LiveJobHistoryEntry | null = null
  for (const entry of job.history) {
    if (latest === null || Date.parse(entry.occurredAt) >= Date.parse(latest.occurredAt)) {
      latest = entry
    }
  }
  return latest
}

export function liveToastTitle(entry: LiveJobHistoryEntry): string {
  const action: string = entry.action
  const write = Object.prototype.hasOwnProperty.call(titleWriters, action)
    ? titleWriters[action as JobHistoryAction]
    : null
  return write === null
    ? `${entry.actor.displayName} updated a job`
    : write(entry.actor.displayName, entry)
}

export function liveToastBody(job: LiveJob): string {
  return `${job.title} is now ${jobStatusLabel(job.status)}.`
}

export function shortenNote(note: string): string {
  const text = note.replace(/\s+/g, ' ').trim()
  if (text.length <= toastNoteLength) {
    return text
  }
  const space = text.lastIndexOf(' ', toastNoteLength - 1)
  const head = space > 0 ? text.slice(0, space) : text.slice(0, toastNoteLength - 1)
  return `${head.trimEnd()}…`
}

export function liveToastNote(entry: LiveJobHistoryEntry): string | null {
  if (!notedActions.has(entry.action) || entry.note === null) {
    return null
  }
  const text = shortenNote(entry.note)
  return text.length === 0 ? null : `“${text}”`
}

export function liveToastMoment(entry: LiveJobHistoryEntry, now: Date): string {
  const elapsed = now.getTime() - Date.parse(entry.occurredAt)
  return elapsed < justNowMilliseconds ? 'just now' : formatMoment(entry.occurredAt, now)
}

export function liveToastFor(
  job: LiveJob,
  entry: LiveJobHistoryEntry,
  now: Date = new Date(),
): ToastRequest {
  const jobNumber = formatJobNumber(job.number)
  const title = liveToastTitle(entry)
  const body = liveToastBody(job)
  const note = liveToastNote(entry)
  const sentences = [`${jobNumber}.`, `${title}.`, body, ...(note === null ? [] : [note])]
  return {
    kind: 'event',
    title,
    body,
    note,
    meta: jobNumber,
    metaDetail: liveToastMoment(entry, now),
    announcement: sentences.join(' '),
  }
}
