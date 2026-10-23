import { vi } from 'vitest'

import {
  accessToken,
  demoAdministratorEmail,
  password,
  sessionResponse,
} from '@/features/auth/__tests__/session-fixtures'
import { useSessionStore } from '@/features/auth/session-store'
import {
  dana,
  historyEntry,
  jobDetails,
  onDemoDay,
  priya,
} from '@/features/jobs/__tests__/job-fixtures'
import { ApiError } from '@/shared/api/api-error'
import { readClientFixture } from '@/shared/testing/contract-fixtures'
import {
  json,
  stubFetch,
  type FetchStub,
  type Reply,
  type RouteTable,
} from '@/shared/testing/fetch-stub'

import type { LivePageCopy, LiveRefetchRegistration } from '../live-refetch'
import type { LiveJob } from '../realtime-api'
import type { JobEventPayload } from '../realtime-events'

export const ticketRoute = 'POST /api/auth/socket-tickets'

export const signInRoute = 'POST /api/auth/sign-in'

export const refreshRoute = 'POST /api/auth/refresh'

export const signOutRoute = 'POST /api/auth/sign-out'

export const generatorJobId = '01a153ec-f2c0-7499-a012-b2be45d90129'

export function jobRoute(jobId: string): string {
  return `GET /api/jobs/${jobId}`
}

export function ticketValue(): string {
  return `ticket-${crypto.randomUUID()}`
}

export function ticketReply(): Reply {
  return (request) =>
    json({ ticket: ticketValue(), expiresAt: new Date(Date.now() + 60_000).toISOString() })(request)
}

export function jobEvent(overrides: Partial<JobEventPayload> = {}): JobEventPayload {
  const fixture = readClientFixture('job.status-changed') as unknown as JobEventPayload
  return { ...fixture, eventId: crypto.randomUUID(), ...overrides }
}

export function generatorJob(overrides: Partial<LiveJob> = {}): LiveJob {
  return jobDetails({
    id: generatorJobId,
    number: 1049,
    title: 'Standby generator load test',
    status: 'InProgress',
    assignedTechnician: priya,
    updatedAt: onDemoDay(0, 10, 42),
    history: [
      historyEntry({ occurredAt: onDemoDay(0, 8, 0) }),
      historyEntry({
        id: '01a1547a-dae0-7a01-9e1c-6b3f0f4b7a11',
        action: 'Assigned',
        fromStatus: 'Unassigned',
        toStatus: 'Assigned',
        actor: dana,
        technician: { id: priya.id, displayName: priya.displayName },
        occurredAt: onDemoDay(0, 8, 5),
      }),
      historyEntry({
        id: '01a1549c-bac0-7b02-8af7-8acf82fc5f4d',
        action: 'Started',
        fromStatus: 'Assigned',
        toStatus: 'InProgress',
        actor: { id: priya.id, displayName: priya.displayName },
        occurredAt: onDemoDay(0, 10, 42),
      }),
    ],
    ...overrides,
  })
}

export interface ViewCalls {
  refetch: number
  lists: number
  readonly applied: LiveJob[]
}

export type ViewResult = 'refreshed' | 'skipped' | ApiError

export interface RecordingView {
  readonly calls: ViewCalls
  readonly registration: LiveRefetchRegistration
  respondWith(result: ViewResult): void
}

export const jobsPageCopy: LivePageCopy = {
  refreshFailedTitle: 'Jobs could not be refreshed.',
  showingFrom: (time) => `Showing jobs from ${time}.`,
}

export interface RecordingViewOptions {
  readonly withLists?: boolean
  readonly page?: LivePageCopy | null
}

export function recordingView(options: RecordingViewOptions = {}): RecordingView {
  const calls: ViewCalls = { refetch: 0, lists: 0, applied: [] }
  let result: ViewResult = 'refreshed'
  const respond = (): Promise<boolean> =>
    typeof result === 'string' ? Promise.resolve(result === 'refreshed') : Promise.reject(result)
  const page = options.page === undefined ? jobsPageCopy : options.page
  const registration: LiveRefetchRegistration = {
    refetch: () => {
      calls.refetch += 1
      return respond()
    },
    ...(options.withLists === false
      ? {}
      : {
          refetchLists: () => {
            calls.lists += 1
            return respond()
          },
        }),
    applyJob: (job) => {
      calls.applied.push(job)
    },
    ...(page === null ? {} : { page }),
  }
  return {
    calls,
    registration,
    respondWith: (next) => {
      result = next
    },
  }
}

export function apiFailure(status: number, code: string, retryAfterSeconds: number | null = null) {
  return new ApiError({
    status,
    code,
    title: null,
    detail: null,
    fieldErrors: {},
    correlationId: status === 0 ? null : crypto.randomUUID(),
    retryAfterSeconds,
  })
}

export async function flush(rounds = 6): Promise<void> {
  for (let round = 0; round < rounds; round += 1) {
    await vi.advanceTimersByTimeAsync(0)
  }
}

export async function signedIn(routes: RouteTable = {}): Promise<FetchStub> {
  const stub = stubFetch({
    [signInRoute]: json(sessionResponse(accessToken())),
    [ticketRoute]: ticketReply(),
    ...routes,
  })
  await useSessionStore().signIn(demoAdministratorEmail, password())
  return stub
}
