import { onScopeDispose, shallowRef, type ShallowRef } from 'vue'

import { useLiveRefetch, type LivePageCopy } from '@/features/realtime/use-live-refetch'
import { isApiError } from '@/shared/api/api-error'

import { useDashboardStore } from '../dashboard-store'
import { nextLocalMidnight, startOfLocalDay } from '../dashboard-window'

export interface DashboardData {
  readonly store: ReturnType<typeof useDashboardStore>
  readonly today: ShallowRef<Date>
  retrySummary(): Promise<void>
  retryWorkload(): Promise<void>
}

function surfaceUnexpected(reason: unknown): void {
  if (!isApiError(reason)) {
    void Promise.reject(reason)
  }
}

export function useDashboardData(page: LivePageCopy): DashboardData {
  const store = useDashboardStore()
  const today = shallowRef(startOfLocalDay(new Date()))
  let rolloverTimer: ReturnType<typeof setTimeout> | undefined
  let disposed = false

  const live = useLiveRefetch({
    refetch: () => store.refreshSummary(),
    refetchLists: () => store.refreshSummary({ markNewActivity: true }),
    page,
  })

  useLiveRefetch({
    refetch: () => store.refreshWorkload(),
    refetchLists: () => store.refreshWorkload(),
  })

  function markFreshWhen(ready: boolean): void {
    if (ready && !disposed) {
      live.markFresh()
    }
  }

  function scheduleRollover(): void {
    const now = new Date()
    rolloverTimer = setTimeout(rollOver, nextLocalMidnight(now).getTime() - now.getTime())
  }

  function rollOver(): void {
    today.value = startOfLocalDay(new Date())
    scheduleRollover()
    void store.refreshSummary().then(markFreshWhen, surfaceUnexpected)
  }

  async function load(): Promise<void> {
    await store.load()
    markFreshWhen(store.summaryStatus === 'ready' || store.workloadStatus === 'ready')
  }

  async function retrySummary(): Promise<void> {
    await store.loadSummary()
    markFreshWhen(store.summaryStatus === 'ready')
  }

  async function retryWorkload(): Promise<void> {
    await store.loadWorkload()
    markFreshWhen(store.workloadStatus === 'ready')
  }

  void load()
  scheduleRollover()

  onScopeDispose(() => {
    disposed = true
    if (rolloverTimer !== undefined) {
      clearTimeout(rolloverTimer)
      rolloverTimer = undefined
    }
    store.close()
  })

  return { store, today, retrySummary, retryWorkload }
}
