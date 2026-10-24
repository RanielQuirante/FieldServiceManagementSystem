import type { APIResponse, Locator } from '@playwright/test'

import { seededJobNumbers } from '../fixtures.ts'
import {
  readProblem,
  type AdminApi,
  type Inspection,
  type JobDetails,
  type JobStatus,
  type JobSummary,
  type Technician,
  type TechnicianApi,
  type Trade,
} from './api.ts'
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

export function holdNoteFor(runId: string): string {
  return `Waiting for the replacement extinguisher bracket to arrive. E2E ${runId}`
}

export async function createUnassignedJob(
  api: AdminApi,
  runId: string,
  label: string,
  technicianId?: string,
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
    ...(technicianId !== undefined ? { technicianId } : {}),
  })
}

async function jobsOutsideTheRuns(api: AdminApi, status: JobStatus): Promise<JobSummary[]> {
  const found: JobSummary[] = []
  for (let page = 1; ; page += 1) {
    const list = await api.getJobs({ status, page, pageSize: 100 })
    found.push(...list.items.filter((job) => !job.title.startsWith('E2E ')))
    if (list.items.length === 0 || page * list.pageSize >= list.totalCount) {
      return found
    }
  }
}

export async function seededJobInStatus(api: AdminApi, status: JobStatus): Promise<JobDetails> {
  const match = (await jobsOutsideTheRuns(api, status)).find(
    (job) => job.number >= seededJobNumbers.first && job.number <= seededJobNumbers.last,
  )
  if (match === undefined) {
    throw new Error(`No seeded job is ${status}.`)
  }
  return api.getJob(match.id)
}

export function titleLinkPath(job: Pick<JobDetails, 'id' | 'status'>): string {
  return job.status === 'Completed' ? `/jobs/${job.id}/inspection` : `/jobs/${job.id}`
}

export async function jobWithDraftInspection(api: AdminApi): Promise<JobDetails> {
  const candidates = (await jobsOutsideTheRuns(api, 'InProgress')).sort(
    (first, second) => first.number - second.number,
  )
  for (const candidate of candidates) {
    const inspection = await api.getInspection(candidate.id)
    if (inspection.submittedAt === null && inspection.progress.answered > 0) {
      return api.getJob(candidate.id)
    }
  }
  throw new Error('No In progress job outside the e2e runs holds a draft inspection.')
}

export async function createAssignedJob(
  api: AdminApi,
  runId: string,
  label: string,
  technicianId: string,
): Promise<JobDetails> {
  const job = await createUnassignedJob(api, runId, label, technicianId)
  if (job.status !== 'Assigned' || job.assignedTechnician?.id !== technicianId) {
    throw new Error(
      `The job created for "${label}" is ${job.status}, not Assigned to the technician.`,
    )
  }
  return job
}

async function readOk<T>(response: APIResponse, what: string): Promise<T> {
  if (!response.ok()) {
    const problem = await readProblem(response)
    throw new Error(`${what} failed with ${problem.status} ${problem.code}.`)
  }
  return (await response.json()) as T
}

export async function completeJob(technician: TechnicianApi, jobId: string): Promise<JobDetails> {
  const opened = await readOk<Inspection>(await technician.getInspection(jobId), 'GetInspection')
  const saved = await readOk<Inspection>(
    await technician.saveInspection(jobId, {
      answers: opened.items.map((item) => ({
        checklistItemId: item.checklistItemId,
        result: 'Pass',
        note: null,
        readings:
          item.readingFields.length === 0
            ? null
            : item.readingFields.map((field) => ({
                readingFieldId: field.readingFieldId,
                value: 12,
              })),
      })),
      version: opened.version,
    }),
    'SaveInspection',
  )
  return readOk<JobDetails>(
    await technician.submitJob(jobId, {
      technicianNotes: null,
      siteSafetyConfirmed: true,
      version: saved.version,
    }),
    'SubmitJob',
  )
}

export async function createOnHoldJob(
  api: AdminApi,
  technician: TechnicianApi,
  runId: string,
  label: string,
  note: string,
): Promise<JobDetails> {
  const job = await createAssignedJob(api, runId, label, technician.user.id)
  await technician.start(job.id)
  return technician.hold(job.id, note)
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
    ? `${escapeForPattern(tradeText)}\\s*·\\s*${escapeForPattern(openJobsLabel(technician.openJobCount))}`
    : `${escapeForPattern(tradeText)}\\s*·\\s*${escapeForPattern(unavailableReason(technician))}`
  return new RegExp(
    `^${escapeForPattern(technician.displayName)}\\s*${shift}\\s*${detail}${scheduleClausePattern(technician, trade)}\\s*$`,
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
