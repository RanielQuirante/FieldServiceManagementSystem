import { isApiError, type ApiError } from '@/shared/api/api-error'

import type { LiveJob } from './realtime-api'
import { isJobEventPayload } from './realtime-events'

export type Refetch = () => Promise<boolean>

export interface LivePageCopy {
  readonly refreshFailedTitle: string
  showingFrom(time: string): string
}

export interface LiveRefetchRegistration {
  readonly refetch: Refetch
  readonly refetchLists?: Refetch
  readonly applyJob?: (job: LiveJob) => void
  readonly refetchWithJob?: (job: LiveJob) => Refetch | null
  readonly page?: LivePageCopy
}

export interface RefetchFailure {
  readonly error: ApiError | null
}

export interface RefetchOutcome {
  readonly refreshed: boolean
  readonly failure: RefetchFailure | null
}

export type RefetchSource = 'lists' | 'job' | 'cap'

export interface LiveRefetchDependencies {
  readonly getJob: (jobId: string) => Promise<LiveJob>
  readonly onJobFetched: (job: LiveJob) => void
  readonly onRefetched: (
    source: RefetchSource,
    outcome: RefetchOutcome,
    eventJobIds: ReadonlySet<string>,
  ) => void
}

export type EventOutcome = 'invalid' | 'duplicate' | 'accepted'

export interface LiveRefetch {
  register(registration: LiveRefetchRegistration): () => void
  applyJob(job: LiveJob): void
  refetchAll(): Promise<RefetchOutcome>
  handleEvent(payload: unknown): EventOutcome
  reset(): void
}

export const listDebounceMilliseconds = 300

export const jobFetchIntervalMilliseconds = 1_000

export const eventRequestLimit = 60

export const eventRequestWindowMilliseconds = 60_000

export const rememberedEventIds = 200

const jobFetchEntryLimit = 200

const noJobIds: ReadonlySet<string> = new Set()

interface JobFetch {
  inFlight: boolean
  pending: boolean
  lastStartedAt: number
  timer: ReturnType<typeof setTimeout> | undefined
}

function surfaceUnexpected(reason: unknown): void {
  if (!isApiError(reason)) {
    void Promise.reject(reason)
  }
}

export async function runRefetches(refetches: readonly Refetch[]): Promise<RefetchOutcome> {
  const results = await Promise.allSettled(refetches.map((refetch) => refetch()))
  let refreshed = false
  let failure: RefetchFailure | null = null
  for (const result of results) {
    if (result.status === 'fulfilled') {
      refreshed = refreshed || result.value
      continue
    }
    surfaceUnexpected(result.reason)
    failure ??= { error: isApiError(result.reason) ? result.reason : null }
  }
  return { refreshed: refreshed && failure === null, failure }
}

export function createLiveRefetch(dependencies: LiveRefetchDependencies): LiveRefetch {
  const registrations = new Set<LiveRefetchRegistration>()
  const seenEventIds = new Set<string>()
  const jobFetches = new Map<string, JobFetch>()
  let pendingEventJobIds = new Set<string>()
  let requestTimes: number[] = []
  let listTimer: ReturnType<typeof setTimeout> | undefined
  let capTimer: ReturnType<typeof setTimeout> | undefined
  let lastCapRefetchAt = Number.NEGATIVE_INFINITY
  let generation = 0

  function register(registration: LiveRefetchRegistration): () => void {
    registrations.add(registration)
    return () => {
      registrations.delete(registration)
    }
  }

  function applyJob(job: LiveJob): void {
    const followUps: Refetch[] = []
    for (const registration of [...registrations]) {
      registration.applyJob?.(job)
      const followUp = registration.refetchWithJob?.(job) ?? null
      if (followUp !== null) {
        followUps.push(followUp)
      }
    }
    if (followUps.length === 0) {
      return
    }
    if (!take(followUps.length)) {
      overCap()
      return
    }
    const started = generation
    void runRefetches(followUps).then((outcome) => {
      if (started === generation) {
        dependencies.onRefetched('job', outcome, noJobIds)
      }
    })
  }

  function take(count: number): boolean {
    const now = Date.now()
    requestTimes = requestTimes.filter((at) => now - at < eventRequestWindowMilliseconds)
    if (requestTimes.length + count > eventRequestLimit) {
      return false
    }
    for (let index = 0; index < count; index += 1) {
      requestTimes.push(now)
    }
    return true
  }

  function refetchAll(): Promise<RefetchOutcome> {
    return runRefetches([...registrations].map((entry) => entry.refetch))
  }

  function runCapRefetch(): void {
    capTimer = undefined
    lastCapRefetchAt = Date.now()
    pendingEventJobIds = new Set()
    const started = generation
    void refetchAll().then((outcome) => {
      if (started === generation) {
        dependencies.onRefetched('cap', outcome, noJobIds)
      }
    })
  }

  function overCap(): void {
    if (capTimer !== undefined) {
      clearTimeout(capTimer)
    }
    const throttled = lastCapRefetchAt + eventRequestWindowMilliseconds - Date.now()
    capTimer = setTimeout(runCapRefetch, Math.max(listDebounceMilliseconds, throttled))
  }

  function runListRefetch(): void {
    listTimer = undefined
    const eventJobIds = pendingEventJobIds
    pendingEventJobIds = new Set()
    const refetches = [...registrations].flatMap((entry) =>
      entry.refetchLists === undefined ? [] : [entry.refetchLists],
    )
    if (refetches.length === 0) {
      return
    }
    if (!take(refetches.length)) {
      overCap()
      return
    }
    const started = generation
    void runRefetches(refetches).then((outcome) => {
      if (started === generation) {
        dependencies.onRefetched('lists', outcome, eventJobIds)
      }
    })
  }

  function scheduleListRefetch(jobId: string): void {
    pendingEventJobIds.add(jobId)
    if (listTimer !== undefined) {
      clearTimeout(listTimer)
    }
    listTimer = setTimeout(runListRefetch, listDebounceMilliseconds)
  }

  function scheduleJobFetch(jobId: string, entry: JobFetch, delay: number): void {
    entry.pending = true
    entry.timer = setTimeout(() => {
      entry.timer = undefined
      startJobFetch(jobId, entry)
    }, delay)
  }

  function startJobFetch(jobId: string, entry: JobFetch): void {
    entry.pending = false
    if (!take(1)) {
      overCap()
      return
    }
    entry.inFlight = true
    entry.lastStartedAt = Date.now()
    const started = generation
    void dependencies
      .getJob(jobId)
      .then(
        (job) => {
          if (started === generation) {
            dependencies.onJobFetched(job)
          }
        },
        (error: unknown) => {
          surfaceUnexpected(error)
        },
      )
      .finally(() => {
        entry.inFlight = false
        if (started !== generation || !entry.pending) {
          return
        }
        const wait = entry.lastStartedAt + jobFetchIntervalMilliseconds - Date.now()
        scheduleJobFetch(jobId, entry, Math.max(wait, 0))
      })
  }

  function pruneJobFetches(): void {
    if (jobFetches.size < jobFetchEntryLimit) {
      return
    }
    const now = Date.now()
    for (const [jobId, entry] of jobFetches) {
      const idle = !entry.inFlight && !entry.pending && entry.timer === undefined
      if (idle && now - entry.lastStartedAt >= jobFetchIntervalMilliseconds) {
        jobFetches.delete(jobId)
      }
    }
  }

  function requestJob(jobId: string): void {
    let entry = jobFetches.get(jobId)
    if (entry === undefined) {
      pruneJobFetches()
      entry = {
        inFlight: false,
        pending: false,
        lastStartedAt: Number.NEGATIVE_INFINITY,
        timer: undefined,
      }
      jobFetches.set(jobId, entry)
    }
    if (entry.inFlight || entry.timer !== undefined) {
      entry.pending = true
      return
    }
    const wait = entry.lastStartedAt + jobFetchIntervalMilliseconds - Date.now()
    if (wait > 0) {
      scheduleJobFetch(jobId, entry, wait)
      return
    }
    startJobFetch(jobId, entry)
  }

  function remember(eventId: string): void {
    seenEventIds.add(eventId)
    if (seenEventIds.size > rememberedEventIds) {
      const oldest = seenEventIds.values().next()
      if (oldest.done !== true) {
        seenEventIds.delete(oldest.value)
      }
    }
  }

  function handleEvent(payload: unknown): EventOutcome {
    if (!isJobEventPayload(payload)) {
      return 'invalid'
    }
    if (seenEventIds.has(payload.eventId)) {
      return 'duplicate'
    }
    remember(payload.eventId)
    requestJob(payload.jobId)
    scheduleListRefetch(payload.jobId)
    return 'accepted'
  }

  function reset(): void {
    generation += 1
    if (listTimer !== undefined) {
      clearTimeout(listTimer)
      listTimer = undefined
    }
    if (capTimer !== undefined) {
      clearTimeout(capTimer)
      capTimer = undefined
    }
    for (const entry of jobFetches.values()) {
      if (entry.timer !== undefined) {
        clearTimeout(entry.timer)
      }
    }
    jobFetches.clear()
    seenEventIds.clear()
    pendingEventJobIds = new Set()
    requestTimes = []
    lastCapRefetchAt = Number.NEGATIVE_INFINITY
  }

  return { register, applyJob, refetchAll, handleEvent, reset }
}
