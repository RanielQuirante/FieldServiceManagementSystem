import { defineStore } from 'pinia'
import { computed, shallowRef } from 'vue'

import { useSessionStore } from '@/features/auth/session-store'
import type { ApiError } from '@/shared/api/api-error'
import { useToasts } from '@/shared/composables/use-toasts'
import { createVisibleTimeout, type VisibleTimeout } from '@/shared/composables/visible-timeout'
import { formatMomentUnbroken } from '@/shared/format/date-time'

import {
  createLiveRefetch,
  type LivePageCopy,
  type LiveRefetchRegistration,
  type RefetchOutcome,
  type RefetchSource,
} from './live-refetch'
import { latestHistoryEntry, liveToastFor } from './live-toast'
import { getJobForToast, type LiveJob } from './realtime-api'
import { createRealtimeClient, type ConnectionPhase } from './realtime-client'

export type LiveState = 'idle' | 'connecting' | 'live' | 'reconnecting' | 'paused'

export type LiveNotice = 'reconnecting' | 'paused' | 'back-online' | 'refresh-failed'

export interface RefreshFailureNotice {
  readonly title: string
  readonly message: string
  readonly reference: string | null
}

export const backOnlineMilliseconds = 8_000

export const rowFlashMilliseconds = 12_000

const toastedEntryLimit = 200

function waitText(error: ApiError): string {
  const seconds = error.retryAfterSeconds ?? 60
  return seconds === 1 ? 'Try again in 1 second.' : `Try again in ${seconds} seconds.`
}

export const useRealtimeStore = defineStore('realtime', () => {
  const session = useSessionStore()
  const toasts = useToasts()

  const state = shallowRef<LiveState>('idle')
  const refreshedAt = shallowRef<Date | null>(null)
  const rawNotice = shallowRef<LiveNotice | null>(null)
  const failure = shallowRef<ApiError | null>(null)
  const refreshing = shallowRef(false)
  const flashedJobIds = shallowRef<ReadonlySet<string>>(new Set())
  const livePages = shallowRef<readonly LivePageCopy[]>([])

  const activePage = computed(() => livePages.value[livePages.value.length - 1] ?? null)

  const notice = computed(() => (activePage.value === null ? null : rawNotice.value))

  const refreshedLabel = computed(() => activePage.value?.refreshedLabel ?? 'Jobs refreshed at')

  const noticeTime = computed(() =>
    refreshedAt.value === null ? null : formatMomentUnbroken(refreshedAt.value),
  )

  const refreshFailure = computed<RefreshFailureNotice | null>(() => {
    const page = activePage.value
    if (page === null || rawNotice.value !== 'refresh-failed') {
      return null
    }
    const error = failure.value
    const limited = error !== null && error.status === 429
    const time = noticeTime.value
    return {
      title: page.refreshFailedTitle,
      message: limited ? waitText(error) : time === null ? '' : page.showingFrom(time),
      reference: error === null || limited || error.status === 0 ? null : error.correlationId,
    }
  })

  const flashTimers = new Map<string, VisibleTimeout>()
  const toastedEntries = new Map<string, string>()
  let backOnlineTimer: ReturnType<typeof setTimeout> | undefined
  let connectGeneration = 0
  let runGeneration = 0
  let offlineNoticeDue = false
  let offlineAnnounced = false

  function clearBackOnlineTimer(): void {
    if (backOnlineTimer !== undefined) {
      clearTimeout(backOnlineTimer)
      backOnlineTimer = undefined
    }
  }

  function setNotice(next: LiveNotice | null): void {
    if (next !== 'back-online') {
      clearBackOnlineTimer()
    }
    if (next !== 'refresh-failed') {
      failure.value = null
    }
    rawNotice.value = next
  }

  function showBackOnline(): void {
    setNotice('back-online')
    clearBackOnlineTimer()
    backOnlineTimer = setTimeout(() => {
      backOnlineTimer = undefined
      if (rawNotice.value === 'back-online') {
        rawNotice.value = null
      }
    }, backOnlineMilliseconds)
  }

  function showRefreshFailed(error: ApiError | null): void {
    setNotice('refresh-failed')
    failure.value = error
  }

  function offlineNotice(): LiveNotice | null {
    if (state.value === 'paused') {
      return 'paused'
    }
    return state.value === 'reconnecting' && offlineNoticeDue ? 'reconnecting' : null
  }

  function announceOffline(): void {
    const next = offlineNotice()
    if (next !== null) {
      offlineAnnounced = true
      setNotice(next)
    }
  }

  function setFlashed(jobId: string, flashed: boolean): void {
    const next = new Set(flashedJobIds.value)
    if (flashed) {
      next.add(jobId)
    } else {
      next.delete(jobId)
    }
    flashedJobIds.value = next
  }

  function flash(jobId: string): void {
    flashTimers.get(jobId)?.cancel()
    setFlashed(jobId, true)
    flashTimers.set(
      jobId,
      createVisibleTimeout(() => {
        flashTimers.delete(jobId)
        setFlashed(jobId, false)
      }, rowFlashMilliseconds),
    )
  }

  function clearFlashes(): void {
    for (const timer of flashTimers.values()) {
      timer.cancel()
    }
    flashTimers.clear()
    flashedJobIds.value = new Set()
  }

  function rememberToast(jobId: string, entryId: string): void {
    toastedEntries.delete(jobId)
    toastedEntries.set(jobId, entryId)
    if (toastedEntries.size > toastedEntryLimit) {
      const oldest = toastedEntries.keys().next()
      if (oldest.done !== true) {
        toastedEntries.delete(oldest.value)
      }
    }
  }

  function onJobFetched(job: LiveJob): void {
    coordinator.applyJob(job)
    const entry = latestHistoryEntry(job)
    if (entry === null || toastedEntries.get(job.id) === entry.id) {
      return
    }
    rememberToast(job.id, entry.id)
    if (entry.actor.id === session.user?.id || activePage.value === null) {
      return
    }
    toasts.show(liveToastFor(job, entry))
  }

  function noteOutcome(outcome: RefetchOutcome): void {
    if (outcome.refreshed) {
      markFresh()
    }
  }

  function onRefetched(
    source: RefetchSource,
    outcome: RefetchOutcome,
    eventJobIds: ReadonlySet<string>,
  ): void {
    noteOutcome(outcome)
    if (!outcome.refreshed) {
      return
    }
    if (rawNotice.value === 'refresh-failed') {
      setNotice(offlineNotice())
    }
    if (source === 'lists') {
      for (const jobId of eventJobIds) {
        flash(jobId)
      }
    }
  }

  const coordinator = createLiveRefetch({
    getJob: getJobForToast,
    onJobFetched,
    onRefetched,
  })

  function onPhase(phase: ConnectionPhase): void {
    connectGeneration += 1
    state.value = phase
    if (phase === 'connecting') {
      offlineNoticeDue = false
      offlineAnnounced = false
      setNotice(null)
      return
    }
    if (rawNotice.value === 'back-online') {
      setNotice(null)
    }
    announceOffline()
  }

  function onOfflineNotice(): void {
    offlineNoticeDue = true
    announceOffline()
  }

  async function refetchOnConnect(): Promise<void> {
    connectGeneration += 1
    const started = connectGeneration
    offlineNoticeDue = false
    const outcome = await coordinator.refetchAll()
    if (started !== connectGeneration || !client.isConnected()) {
      return
    }
    noteOutcome(outcome)
    state.value = 'live'
    const announced = offlineAnnounced
    offlineAnnounced = false
    if (outcome.failure !== null) {
      showRefreshFailed(outcome.failure.error)
    } else if (announced) {
      showBackOnline()
    } else if (rawNotice.value !== null) {
      setNotice(null)
    }
  }

  const client = createRealtimeClient({
    session: {
      isSignedIn: () => session.isSignedIn,
      refreshSession: () => session.refreshSession(),
      expireSession: () => {
        session.expireSession()
      },
    },
    handlers: {
      onPhase,
      onOfflineNotice,
      onConnect: () => {
        void refetchOnConnect()
      },
      onEvent: (_name, payload) => {
        coordinator.handleEvent(payload)
      },
    },
  })

  function start(): void {
    if (state.value !== 'idle') {
      return
    }
    client.start()
  }

  function stop(): void {
    if (state.value === 'idle') {
      return
    }
    runGeneration += 1
    connectGeneration += 1
    client.stop()
    coordinator.reset()
    state.value = 'idle'
    refreshedAt.value = null
    refreshing.value = false
    offlineNoticeDue = false
    offlineAnnounced = false
    setNotice(null)
    clearFlashes()
    toastedEntries.clear()
    toasts.clear()
  }

  async function refreshNow(): Promise<void> {
    if (state.value === 'idle' || refreshing.value) {
      return
    }
    refreshing.value = true
    const started = runGeneration
    if (!client.isConnected()) {
      client.retryNow()
    }
    try {
      const outcome = await coordinator.refetchAll()
      if (started !== runGeneration) {
        return
      }
      noteOutcome(outcome)
      if (outcome.failure !== null) {
        showRefreshFailed(outcome.failure.error)
      } else if (rawNotice.value === 'refresh-failed') {
        setNotice(offlineNotice())
      }
    } finally {
      refreshing.value = false
    }
  }

  function register(registration: LiveRefetchRegistration): () => void {
    const unregister = coordinator.register(registration)
    const page = registration.page
    if (page !== undefined) {
      livePages.value = [...livePages.value, page]
    }
    return () => {
      unregister()
      if (page !== undefined) {
        livePages.value = livePages.value.filter((entry) => entry !== page)
      }
    }
  }

  function markFresh(at: Date = new Date()): void {
    if (state.value === 'idle') {
      return
    }
    if (refreshedAt.value === null || at >= refreshedAt.value) {
      refreshedAt.value = at
    }
  }

  session.onSessionEnded(() => {
    stop()
  })

  return {
    state,
    refreshedAt,
    notice,
    noticeTime,
    refreshedLabel,
    refreshFailure,
    refreshing,
    flashedJobIds,
    start,
    stop,
    refreshNow,
    register,
    markFresh,
  }
})
