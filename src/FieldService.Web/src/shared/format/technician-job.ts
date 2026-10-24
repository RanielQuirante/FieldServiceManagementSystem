import type { components } from '@/shared/api/schema.gen'

import { dayOffset, formatShortDate, formatTime, unbroken, type Instant } from './date-time'

type Schemas = components['schemas']

export type TechnicianJob = Schemas['TechnicianJobReference']

export interface TechnicianJobs {
  readonly currentJob: TechnicianJob | null
  readonly nextJob: TechnicianJob | null
}

export type CurrentOrNextJob =
  | { readonly kind: 'current'; readonly job: TechnicianJob }
  | { readonly kind: 'next'; readonly job: TechnicianJob; readonly when: string }
  | { readonly kind: 'none' }

export interface EmailParts {
  readonly local: string
  readonly domain: string
}

export function startsWhen(start: Instant, now: Date): string {
  const time = unbroken(formatTime(start))
  return dayOffset(start, now) === 0
    ? `at ${time}`
    : `on ${unbroken(formatShortDate(start, now))} at ${time}`
}

export function currentOrNextJob(technician: TechnicianJobs, now: Date): CurrentOrNextJob {
  if (technician.currentJob !== null) {
    return { kind: 'current', job: technician.currentJob }
  }
  if (technician.nextJob !== null) {
    return {
      kind: 'next',
      job: technician.nextJob,
      when: startsWhen(technician.nextJob.scheduledStart, now),
    }
  }
  return { kind: 'none' }
}

export function emailParts(email: string): EmailParts {
  const at = email.lastIndexOf('@')
  return at <= 0
    ? { local: email, domain: '' }
    : { local: email.slice(0, at), domain: email.slice(at) }
}

export function telephoneHref(phoneNumber: string): string | null {
  const digits = phoneNumber.replace(/\D/g, '')
  if (digits.length === 0) {
    return null
  }
  return phoneNumber.trim().startsWith('+') ? `tel:+${digits}` : `tel:${digits}`
}

export function openJobsLabel(count: number): string {
  return count === 1 ? 'open job' : 'open jobs'
}
