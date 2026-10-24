import { defineStore } from 'pinia'
import { shallowRef } from 'vue'

import { isApiError, type ApiError } from '@/shared/api/api-error'
import { createVisibleTimeout, type VisibleTimeout } from '@/shared/composables/visible-timeout'

import * as dashboardApi from './dashboard-api'
import type { DashboardSummary, WorkloadTechnician } from './dashboard-api'
import { dashboardWindow } from './dashboard-window'

export type DashboardLoadStatus = 'idle' | 'loading' | 'ready' | 'error'

export interface SummaryRefreshOptions {
  readonly markNewActivity?: boolean
}

export const newActivityMilliseconds = 12_000

interface Resource<Data> {
  readonly data: { value: Data | null }
  readonly status: { value: DashboardLoadStatus }
  readonly error: { value: ApiError | null }
  readonly fetch: () => Promise<Data>
  request: number
  running: Promise<void> | null
}

function addedActivityIds(shown: DashboardSummary, result: DashboardSummary): string[] {
  if (shown.range.from !== result.range.from || shown.range.to !== result.range.to) {
    return []
  }
  const before = new Set(shown.recentActivity.map((entry) => entry.id))
  return result.recentActivity.map((entry) => entry.id).filter((id) => !before.has(id))
}

export const useDashboardStore = defineStore('dashboard', () => {
  const summary = shallowRef<DashboardSummary | null>(null)
  const summaryStatus = shallowRef<DashboardLoadStatus>('idle')
  const summaryError = shallowRef<ApiError | null>(null)
  const workload = shallowRef<readonly WorkloadTechnician[] | null>(null)
  const workloadStatus = shallowRef<DashboardLoadStatus>('idle')
  const workloadError = shallowRef<ApiError | null>(null)
  const newActivityIds = shallowRef<ReadonlySet<string>>(new Set())

  const newActivityBatches = new Map<string, number>()
  const newActivityTimers = new Set<VisibleTimeout>()
  let batchCount = 0

  const summaryResource: Resource<DashboardSummary> = {
    data: summary,
    status: summaryStatus,
    error: summaryError,
    fetch: () => dashboardApi.getDashboardSummary(dashboardWindow(new Date())),
    request: 0,
    running: null,
  }

  const workloadResource: Resource<readonly WorkloadTechnician[]> = {
    data: workload,
    status: workloadStatus,
    error: workloadError,
    fetch: () => dashboardApi.getTechnicians(),
    request: 0,
    running: null,
  }

  async function performLoad<Data>(resource: Resource<Data>): Promise<void> {
    resource.request += 1
    const request = resource.request
    resource.status.value = 'loading'
    resource.error.value = null
    try {
      const result = await resource.fetch()
      if (request !== resource.request) {
        return
      }
      resource.data.value = result
      resource.status.value = 'ready'
    } catch (error) {
      if (request !== resource.request) {
        return
      }
      resource.status.value = 'error'
      if (!isApiError(error)) {
        throw error
      }
      resource.error.value = error
    }
  }

  async function loadResource<Data>(resource: Resource<Data>): Promise<void> {
    const started = performLoad(resource)
    resource.running = started
    try {
      await started
    } finally {
      if (resource.running === started) {
        resource.running = null
      }
    }
  }

  async function refreshResource<Data>(
    resource: Resource<Data>,
    accept: (result: Data) => void,
  ): Promise<boolean> {
    if (resource.running !== null) {
      await resource.running.catch(() => undefined)
    }
    if (resource.data.value === null || resource.status.value !== 'ready') {
      return false
    }
    resource.request += 1
    const request = resource.request
    try {
      const result = await resource.fetch()
      if (request !== resource.request) {
        return false
      }
      accept(result)
      return true
    } catch (error) {
      if (request !== resource.request && isApiError(error)) {
        return false
      }
      throw error
    }
  }

  function setNewActivity(): void {
    newActivityIds.value = new Set(newActivityBatches.keys())
  }

  function markNewActivity(ids: readonly string[]): void {
    if (ids.length === 0) {
      return
    }
    batchCount += 1
    const batch = batchCount
    for (const id of ids) {
      newActivityBatches.set(id, batch)
    }
    setNewActivity()
    const timer = createVisibleTimeout(() => {
      newActivityTimers.delete(timer)
      for (const [id, owner] of [...newActivityBatches]) {
        if (owner === batch) {
          newActivityBatches.delete(id)
        }
      }
      setNewActivity()
    }, newActivityMilliseconds)
    newActivityTimers.add(timer)
  }

  function clearNewActivity(): void {
    for (const timer of newActivityTimers) {
      timer.cancel()
    }
    newActivityTimers.clear()
    newActivityBatches.clear()
    setNewActivity()
  }

  function loadSummary(): Promise<void> {
    return loadResource(summaryResource)
  }

  function loadWorkload(): Promise<void> {
    return loadResource(workloadResource)
  }

  async function load(): Promise<void> {
    await Promise.all([loadSummary(), loadWorkload()])
  }

  function refreshSummary(options: SummaryRefreshOptions = {}): Promise<boolean> {
    return refreshResource(summaryResource, (result) => {
      const shown = summary.value
      summary.value = result
      if (options.markNewActivity === true && shown !== null) {
        markNewActivity(addedActivityIds(shown, result))
      }
    })
  }

  function refreshWorkload(): Promise<boolean> {
    return refreshResource(workloadResource, (result) => {
      workload.value = result
    })
  }

  function resetResource<Data>(resource: Resource<Data>): void {
    resource.request += 1
    resource.running = null
    resource.data.value = null
    resource.status.value = 'idle'
    resource.error.value = null
  }

  function close(): void {
    resetResource(summaryResource)
    resetResource(workloadResource)
    clearNewActivity()
  }

  return {
    summary,
    summaryStatus,
    summaryError,
    workload,
    workloadStatus,
    workloadError,
    newActivityIds,
    load,
    loadSummary,
    loadWorkload,
    refreshSummary,
    refreshWorkload,
    close,
  }
})
