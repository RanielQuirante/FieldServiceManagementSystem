import { defineStore } from 'pinia'
import { computed, shallowRef } from 'vue'

import { useSessionStore } from '@/features/auth/session-store'
import { firstFieldError, isApiError, type ApiError } from '@/shared/api/api-error'
import { jobStatuses, type JobStatus } from '@/shared/format/labels'

import type { JobFilters } from './composables/use-job-filters'
import * as jobsApi from './jobs-api'
import type {
  CreateJobRequest,
  Inspection,
  JobDetails,
  JobHistoryEntry,
  JobList,
  JobListQuery,
} from './jobs-api'

export const jobsPageSize = 12

export type JobListStatus = 'idle' | 'loading' | 'ready' | 'error'

export type JobDetailStatus = 'idle' | 'loading' | 'ready' | 'not-found' | 'error'

export type InspectionReportStatus = JobDetailStatus

export interface InspectionReport {
  readonly job: JobDetails
  readonly inspection: Inspection
}

async function fetchInspectionReport(jobId: string): Promise<InspectionReport> {
  const [job, inspection] = await Promise.allSettled([
    jobsApi.getJobById(jobId),
    jobsApi.getInspection(jobId),
  ])
  if (job.status === 'fulfilled' && inspection.status === 'fulfilled') {
    return { job: job.value, inspection: inspection.value }
  }
  const reasons: unknown[] = [job, inspection].flatMap((result) =>
    result.status === 'rejected' ? [result.reason] : [],
  )
  const unexpected = reasons.find((reason) => !isApiError(reason))
  const missing = reasons.find((reason) => isApiError(reason) && reason.status === 404)
  throw unexpected ?? missing ?? reasons[0]
}

export type AssignConflictCode = 'concurrency.conflict' | 'job.not-assignable'

export type JobConflictCode = AssignConflictCode | 'job.invalid-transition'

export type JobNoticeCode = JobConflictCode | 'already-cancelled'

export interface JobNotice {
  readonly id: number
  readonly code: JobNoticeCode
  readonly jobNumber: number
}

export type AssignOutcome =
  | { readonly kind: 'assigned'; readonly job: JobDetails }
  | { readonly kind: 'conflict'; readonly code: AssignConflictCode }

export type CancelOutcome =
  | { readonly kind: 'cancelled'; readonly job: JobDetails }
  | { readonly kind: 'already-cancelled'; readonly job: JobDetails }
  | { readonly kind: 'reason-rejected'; readonly message: string }
  | { readonly kind: 'invalid-transition' }
  | { readonly kind: 'conflict' }
  | { readonly kind: 'not-found' }
  | { readonly kind: 'failed'; readonly error: ApiError }

const assignConflictCodes: readonly string[] = ['concurrency.conflict', 'job.not-assignable']

function conflictCodeOf(error: unknown): AssignConflictCode | null {
  if (!isApiError(error) || error.status !== 409 || error.code === null) {
    return null
  }
  return assignConflictCodes.includes(error.code) ? (error.code as AssignConflictCode) : null
}

export function newestCancellation(job: JobDetails): JobHistoryEntry | null {
  for (let index = job.history.length - 1; index >= 0; index -= 1) {
    const entry = job.history[index]
    if (entry !== undefined && entry.action === 'Cancelled') {
      return entry
    }
  }
  return null
}

export function isOwnCancellation(
  job: JobDetails,
  actorId: string | null,
  reason: string,
): boolean {
  const entry = newestCancellation(job)
  return (
    job.status === 'Cancelled' &&
    entry !== null &&
    actorId !== null &&
    entry.actor.id === actorId &&
    entry.note === reason
  )
}

export function jobListQuery(filters: JobFilters): JobListQuery {
  const search = filters.search.trim()
  return {
    ...(search.length > 0 ? { search } : {}),
    ...(filters.status !== null ? { status: filters.status } : {}),
    ...(filters.technicianId !== null ? { technicianId: filters.technicianId } : {}),
    ...(filters.priority !== null ? { priority: filters.priority } : {}),
    page: filters.page,
    pageSize: jobsPageSize,
  }
}

export const useJobsStore = defineStore('jobs', () => {
  const list = shallowRef<JobList | null>(null)
  const listStatus = shallowRef<JobListStatus>('idle')
  const listError = shallowRef<ApiError | null>(null)
  const listFilters = shallowRef<JobFilters | null>(null)
  let listRequest = 0

  let listLoad: Promise<void> | null = null

  const job = shallowRef<JobDetails | null>(null)
  const jobStatus = shallowRef<JobDetailStatus>('idle')
  const jobError = shallowRef<ApiError | null>(null)
  const jobNotice = shallowRef<JobNotice | null>(null)
  let jobRequest = 0
  let noticeCount = 0
  let openJobId: string | null = null
  let jobLoad: Promise<void> | null = null

  const report = shallowRef<InspectionReport | null>(null)
  const reportStatus = shallowRef<InspectionReportStatus>('idle')
  const reportError = shallowRef<ApiError | null>(null)
  let reportRequest = 0
  let reportRefreshCount = 0
  let reportRefreshApplied = 0
  let openReportId: string | null = null
  let reportLoad: Promise<void> | null = null

  const statusCounts = computed<Readonly<Record<JobStatus, number>> | null>(() => {
    if (list.value === null) {
      return null
    }
    const counts = Object.fromEntries(jobStatuses.map((status) => [status, 0])) as Record<
      JobStatus,
      number
    >
    for (const entry of list.value.statusCounts) {
      if (Object.prototype.hasOwnProperty.call(counts, entry.status)) {
        counts[entry.status] = entry.count
      }
    }
    return counts
  })

  const statusTotal = computed<number | null>(() =>
    list.value === null
      ? null
      : list.value.statusCounts.reduce((sum, entry) => sum + entry.count, 0),
  )

  async function loadJobs(filters: JobFilters): Promise<void> {
    const running = performLoadJobs(filters)
    listLoad = running
    try {
      await running
    } finally {
      if (listLoad === running) {
        listLoad = null
      }
    }
  }

  async function refreshJobs(): Promise<boolean> {
    if (listLoad !== null) {
      await listLoad.catch(() => undefined)
    }
    const filters = listFilters.value
    if (filters === null || list.value === null || listStatus.value !== 'ready') {
      return false
    }
    listRequest += 1
    const request = listRequest
    try {
      const result = await jobsApi.getJobs(jobListQuery(filters))
      if (request !== listRequest) {
        return false
      }
      list.value = result
      return true
    } catch (error) {
      if (request !== listRequest && isApiError(error)) {
        return false
      }
      throw error
    }
  }

  async function performLoadJobs(filters: JobFilters): Promise<void> {
    listRequest += 1
    const request = listRequest
    listStatus.value = 'loading'
    listError.value = null
    listFilters.value = filters
    try {
      const result = await jobsApi.getJobs(jobListQuery(filters))
      if (request !== listRequest) {
        return
      }
      list.value = result
      listStatus.value = 'ready'
    } catch (error) {
      if (request !== listRequest) {
        return
      }
      listStatus.value = 'error'
      if (!isApiError(error)) {
        throw error
      }
      listError.value = error
    }
  }

  async function openJob(jobId: string): Promise<void> {
    openJobId = jobId
    const running = performOpenJob(jobId)
    jobLoad = running
    try {
      await running
    } finally {
      if (jobLoad === running) {
        jobLoad = null
      }
    }
  }

  function acceptJob(result: JobDetails): void {
    const current = job.value
    if (
      current !== null &&
      current.id === result.id &&
      jobStatus.value === 'ready' &&
      Date.parse(current.updatedAt) > Date.parse(result.updatedAt)
    ) {
      return
    }
    job.value = result
    jobStatus.value = 'ready'
    jobError.value = null
  }

  async function refreshOpenJob(): Promise<boolean> {
    if (jobLoad !== null) {
      await jobLoad.catch(() => undefined)
    }
    const jobId = openJobId
    if (jobId === null || jobStatus.value !== 'ready') {
      return false
    }
    try {
      const result = await jobsApi.getJobById(jobId)
      if (openJobId !== jobId || jobStatus.value !== 'ready') {
        return false
      }
      acceptJob(result)
      return true
    } catch (error) {
      if (openJobId !== jobId && isApiError(error)) {
        return false
      }
      throw error
    }
  }

  function applyLiveJob(result: JobDetails): void {
    if (result.id !== openJobId) {
      return
    }
    if (jobStatus.value === 'loading') {
      jobRequest += 1
      acceptJob(result)
    } else if (jobStatus.value === 'ready') {
      acceptJob(result)
    }
  }

  async function performOpenJob(jobId: string): Promise<void> {
    jobRequest += 1
    const request = jobRequest
    if (job.value?.id === jobId && jobStatus.value === 'ready') {
      return
    }
    job.value = null
    jobStatus.value = 'loading'
    jobError.value = null
    jobNotice.value = null
    try {
      const result = await jobsApi.getJobById(jobId)
      if (request !== jobRequest) {
        return
      }
      job.value = result
      jobStatus.value = 'ready'
    } catch (error) {
      if (request !== jobRequest) {
        return
      }
      if (isApiError(error) && error.status === 404) {
        jobStatus.value = 'not-found'
        return
      }
      jobStatus.value = 'error'
      if (!isApiError(error)) {
        throw error
      }
      jobError.value = error
    }
  }

  async function reloadJob(jobId: string): Promise<void> {
    job.value = null
    jobStatus.value = 'idle'
    await openJob(jobId)
  }

  async function refreshJob(jobId: string): Promise<void> {
    jobRequest += 1
    const request = jobRequest
    try {
      const result = await jobsApi.getJobById(jobId)
      if (request !== jobRequest) {
        return
      }
      job.value = result
      jobStatus.value = 'ready'
      jobError.value = null
    } catch (error) {
      if (request !== jobRequest) {
        return
      }
      job.value = null
      if (isApiError(error) && error.status === 404) {
        jobStatus.value = 'not-found'
        return
      }
      jobStatus.value = 'error'
      if (!isApiError(error)) {
        throw error
      }
      jobError.value = error
    }
  }

  function closeJob(): void {
    openJobId = null
    jobRequest += 1
    job.value = null
    jobStatus.value = 'idle'
    jobError.value = null
    jobNotice.value = null
  }

  async function assignJob(technicianId: string): Promise<AssignOutcome> {
    const current = job.value
    if (current === null) {
      throw new Error('No job is open to assign')
    }
    jobNotice.value = null
    try {
      const updated = await jobsApi.assignJob(current.id, {
        technicianId,
        version: current.version,
      })
      if (job.value?.id === current.id) {
        jobRequest += 1
        job.value = updated
      }
      return { kind: 'assigned', job: updated }
    } catch (error) {
      const code = conflictCodeOf(error)
      if (code === null) {
        throw error
      }
      if (job.value?.id === current.id) {
        await refreshJob(current.id)
        showNotice(code, current.number)
      }
      return { kind: 'conflict', code }
    }
  }

  function showNotice(code: JobNoticeCode, jobNumber: number): void {
    noticeCount += 1
    jobNotice.value = { id: noticeCount, code, jobNumber }
  }

  function isOpen(jobId: string): boolean {
    return job.value?.id === jobId
  }

  function markMissing(jobId: string): void {
    if (!isOpen(jobId)) {
      return
    }
    jobRequest += 1
    job.value = null
    jobStatus.value = 'not-found'
    jobError.value = null
  }

  async function afterInvalidTransition(current: JobDetails): Promise<CancelOutcome> {
    if (!isOpen(current.id)) {
      return { kind: 'invalid-transition' }
    }
    await refreshJob(current.id)
    if (jobStatus.value === 'not-found') {
      return { kind: 'not-found' }
    }
    showNotice('job.invalid-transition', current.number)
    return { kind: 'invalid-transition' }
  }

  async function afterCancelConflict(current: JobDetails): Promise<CancelOutcome> {
    if (!isOpen(current.id)) {
      return { kind: 'conflict' }
    }
    await refreshJob(current.id)
    if (jobStatus.value === 'not-found') {
      return { kind: 'not-found' }
    }
    if (job.value !== null && !job.value.affordances.canCancel) {
      showNotice('job.invalid-transition', current.number)
      return { kind: 'invalid-transition' }
    }
    return { kind: 'conflict' }
  }

  async function cancelFailure(current: JobDetails, error: ApiError): Promise<CancelOutcome> {
    if (error.status === 404) {
      markMissing(current.id)
      return { kind: 'not-found' }
    }
    const reasonError = error.status === 400 ? firstFieldError(error.fieldErrors, 'reason') : null
    if (reasonError !== null) {
      return { kind: 'reason-rejected', message: reasonError }
    }
    if (error.status === 409 && error.code === 'job.invalid-transition') {
      return afterInvalidTransition(current)
    }
    if (error.status === 409 && error.code === 'concurrency.conflict') {
      return afterCancelConflict(current)
    }
    return { kind: 'failed', error }
  }

  async function cancelJob(reason: string): Promise<CancelOutcome> {
    const current = job.value
    if (current === null) {
      throw new Error('No job is open to cancel')
    }
    const sentReason = reason.trim()
    jobNotice.value = null
    let updated: JobDetails
    try {
      updated = await jobsApi.cancelJob(current.id, {
        reason: sentReason,
        version: current.version,
      })
    } catch (error) {
      if (!isApiError(error)) {
        throw error
      }
      return cancelFailure(current, error)
    }
    const shown = isOpen(current.id)
    if (shown) {
      jobRequest += 1
      job.value = updated
      jobStatus.value = 'ready'
      jobError.value = null
    }
    if (isOwnCancellation(updated, useSessionStore().user?.id ?? null, sentReason)) {
      return { kind: 'cancelled', job: updated }
    }
    if (shown) {
      showNotice('already-cancelled', current.number)
    }
    return { kind: 'already-cancelled', job: updated }
  }

  async function openReport(jobId: string): Promise<void> {
    openReportId = jobId
    const running = performOpenReport(jobId)
    reportLoad = running
    try {
      await running
    } finally {
      if (reportLoad === running) {
        reportLoad = null
      }
    }
  }

  async function performOpenReport(jobId: string): Promise<void> {
    reportRequest += 1
    const request = reportRequest
    report.value = null
    reportStatus.value = 'loading'
    reportError.value = null
    try {
      const result = await fetchInspectionReport(jobId)
      if (request !== reportRequest) {
        return
      }
      report.value = result
      reportStatus.value = 'ready'
    } catch (error) {
      if (request !== reportRequest) {
        return
      }
      if (isApiError(error) && error.status === 404) {
        reportStatus.value = 'not-found'
        return
      }
      reportStatus.value = 'error'
      if (!isApiError(error)) {
        throw error
      }
      reportError.value = error
    }
  }

  function acceptReport(jobId: string, refresh: number, result: InspectionReport): boolean {
    const current = report.value
    if (
      openReportId !== jobId ||
      reportStatus.value !== 'ready' ||
      current === null ||
      result.job.id !== jobId ||
      refresh < reportRefreshApplied ||
      Date.parse(current.job.updatedAt) > Date.parse(result.job.updatedAt)
    ) {
      return false
    }
    reportRefreshApplied = refresh
    report.value = result
    return true
  }

  async function runReportRefresh(
    jobId: string,
    fetch: () => Promise<InspectionReport>,
  ): Promise<boolean> {
    reportRefreshCount += 1
    const refresh = reportRefreshCount
    try {
      return acceptReport(jobId, refresh, await fetch())
    } catch (error) {
      if (openReportId !== jobId && isApiError(error)) {
        return false
      }
      throw error
    }
  }

  async function refreshReport(): Promise<boolean> {
    if (reportLoad !== null) {
      await reportLoad.catch(() => undefined)
    }
    const jobId = openReportId
    if (jobId === null || reportStatus.value !== 'ready') {
      return false
    }
    return runReportRefresh(jobId, () => fetchInspectionReport(jobId))
  }

  function reportRefetchFor(job: JobDetails): (() => Promise<boolean>) | null {
    if (job.id !== openReportId || reportStatus.value !== 'ready') {
      return null
    }
    const jobId = job.id
    return () =>
      runReportRefresh(jobId, async () => ({
        job,
        inspection: await jobsApi.getInspection(jobId),
      }))
  }

  function closeReport(): void {
    openReportId = null
    reportRequest += 1
    report.value = null
    reportStatus.value = 'idle'
    reportError.value = null
  }

  async function createJob(request: CreateJobRequest): Promise<JobDetails> {
    const created = await jobsApi.createJob(request)
    jobRequest += 1
    job.value = created
    jobStatus.value = 'ready'
    jobError.value = null
    return created
  }

  return {
    list,
    listStatus,
    listError,
    listFilters,
    statusCounts,
    statusTotal,
    job,
    jobStatus,
    jobError,
    jobNotice,
    loadJobs,
    refreshJobs,
    openJob,
    refreshOpenJob,
    applyLiveJob,
    reloadJob,
    refreshJob,
    closeJob,
    createJob,
    assignJob,
    cancelJob,
    showNotice,
    report,
    reportStatus,
    reportError,
    openReport,
    refreshReport,
    reportRefetchFor,
    closeReport,
  }
})
