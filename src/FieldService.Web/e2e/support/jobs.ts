import type { Locator } from '@playwright/test'

import type { AdminApi, JobDetails, JobStatus, Technician, Trade } from './api.ts'
import { assignCopy, openJobsLabel, tradeLabels } from './copy.ts'
import { e2eTitle } from './run.ts'

export const fireSafetyJob = {
  title: 'Quarterly fire extinguisher inspection',
  description:
    'Quarterly inspection of the portable fire extinguishers in the main building and the gym.',
  customer: 'Cedar Ridge Elementary School',
  siteAddress: '1180 Cedar Ridge Drive',
  template: 'Fire extinguisher inspection',
  trade: 'FireSafety',
} as const

export const notAssignableStatuses: readonly JobStatus[] = [
  'InProgress',
  'OnHold',
  'Completed',
  'Cancelled',
]

export function tomorrowInUtc(): string {
  return new Date(Date.now() + 86_400_000).toISOString().slice(0, 10)
}

export async function createUnassignedJob(
  api: AdminApi,
  runId: string,
  label: string,
): Promise<JobDetails> {
  const template = (await api.checklistTemplates()).find(
    (item) => item.name === fireSafetyJob.template,
  )
  if (template === undefined) {
    throw new Error(`The checklist template "${fireSafetyJob.template}" is missing.`)
  }
  const date = tomorrowInUtc()
  return api.createJob({
    title: e2eTitle(runId, `${fireSafetyJob.title} (${label})`),
    description: fireSafetyJob.description,
    priority: 'Normal',
    customerName: fireSafetyJob.customer,
    siteAddress: fireSafetyJob.siteAddress,
    scheduledStart: `${date}T09:00:00Z`,
    scheduledEnd: `${date}T11:00:00Z`,
    checklistTemplateId: template.id,
  })
}

function rank(technician: Technician, trade: Trade | null): readonly number[] {
  return [
    trade === null || technician.trade === trade ? 0 : 1,
    technician.availableForAssignment ? 0 : 1,
    technician.openJobCount,
  ]
}

export function expectedAssignOrder(
  technicians: readonly Technician[],
  trade: Trade | null,
): Technician[] {
  return technicians
    .map((technician, position) => ({ technician, position, keys: rank(technician, trade) }))
    .sort((first, second) => {
      for (let key = 0; key < first.keys.length; key += 1) {
        const difference = (first.keys[key] ?? 0) - (second.keys[key] ?? 0)
        if (difference !== 0) {
          return difference
        }
      }
      return first.position - second.position
    })
    .map(({ technician }) => technician)
}

function escapeForPattern(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\/]/g, '\\$&')
}

function unavailableReason(technician: Technician): string {
  return technician.isActive ? assignCopy.offShift : 'Inactive'
}

const timePattern = '\\d{1,2}:\\d{2} [AP]M'

const datePattern = '[A-Z][a-z]{2} \\d{1,2} [A-Z][a-z]{2}'

function momentPattern(todayLead: string, otherDayLead: string): string {
  return `(?:${todayLead}\\s*${timePattern}|${otherDayLead}\\s*${datePattern}\\s*at\\s*${timePattern})`
}

export function scheduleClausePattern(
  technician: Technician,
  trade: Trade,
  now: number = Date.now(),
): string {
  if (!technician.availableForAssignment || technician.trade !== trade) {
    return ''
  }
  const current = technician.currentJob
  if (current !== null && current.status === 'InProgress') {
    const number = `JOB-${current.number}`
    return Date.parse(current.scheduledEnd) < now
      ? `\\s*· still on\\s*${number}\\s*${momentPattern(', due', ', due')}`
      : `\\s*· finishes\\s*${number}\\s*${momentPattern('by', 'on')}`
  }
  const next = technician.nextJob
  return next === null ? '' : `\\s*· next\\s*JOB-${next.number}\\s*${momentPattern('at', 'on')}`
}

export function assignRowName(technician: Technician, trade: Trade): RegExp {
  const tradeText = tradeLabels[technician.trade]
  const shift = technician.isOnShift ? assignCopy.onShift : assignCopy.offShift
  const detail = technician.availableForAssignment
    ? `${tradeText} · ${openJobsLabel(technician.openJobCount)}`
    : `${tradeText} · ${unavailableReason(technician)}`
  return new RegExp(
    `^${escapeForPattern(technician.displayName)}\\s*${shift}\\s*${escapeForPattern(detail)}${scheduleClausePattern(technician, trade)}\\s*$`,
  )
}

export interface MomentSpan {
  readonly text: string
  readonly whiteSpace: string
  readonly lineBoxes: number
}

export async function momentSpansOf(group: Locator): Promise<MomentSpan[]> {
  return group.evaluate((element) =>
    [...element.querySelectorAll('.assign-row__moment')].map((span) => ({
      text: (span.textContent ?? '').trim(),
      whiteSpace: getComputedStyle(span).whiteSpace,
      lineBoxes: span.getClientRects().length,
    })),
  )
}

export function rowNamed(displayName: string): RegExp {
  return new RegExp(`^${escapeForPattern(displayName)}`)
}

export function technicianOptionLabel(technician: Technician): string {
  const trade = tradeLabels[technician.trade]
  if (!technician.availableForAssignment) {
    return `${technician.displayName} — ${trade}, ${unavailableReason(technician).toLowerCase()}`
  }
  return `${technician.displayName} — ${trade}, ${openJobsLabel(technician.openJobCount)}`
}
