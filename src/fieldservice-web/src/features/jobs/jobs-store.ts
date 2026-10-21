import { defineStore } from 'pinia'
import { computed, shallowRef } from 'vue'

import { isApiError, type ApiError } from '@/shared/api/api-error'
import { jobStatuses, type JobStatus } from '@/shared/format/labels'

import type { JobFilters } from './composables/use-job-filters'
import * as jobsApi from './jobs-api'
import type { CreateJobRequest, JobDetails, JobList, JobListQuery } from './jobs-api'

export const jobsPageSize = 12

export type JobListStatus = 'idle' | 'loading' | 'ready' | 'error'

export type JobDetailStatus = 'idle' | 'loading' | 'ready' | 'not-found' | 'error'

export function jobListQuery(filters: JobFilters): JobListQuery {
  const search = filters.search.trim()
  return {
    ...(search.length > 0 ? { search } : {}),
    ...(filters.status !== null ? { status: filters.status } : {}),
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

  const job = shallowRef<JobDetails | null>(null)
  const jobStatus = shallowRef<JobDetailStatus>('idle')
  const jobError = shallowRef<ApiError | null>(null)
  let jobRequest = 0

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
    jobRequest += 1
    const request = jobRequest
    if (job.value?.id === jobId && jobStatus.value === 'ready') {
      return
    }
    job.value = null
    jobStatus.value = 'loading'
    jobError.value = null
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

  function closeJob(): void {
    jobRequest += 1
    job.value = null
    jobStatus.value = 'idle'
    jobError.value = null
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
    loadJobs,
    openJob,
    reloadJob,
    closeJob,
    createJob,
  }
})
