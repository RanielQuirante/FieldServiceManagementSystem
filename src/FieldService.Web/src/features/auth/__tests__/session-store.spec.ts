import { createPinia, setActivePinia } from 'pinia'
import { describe, expect, it, vi } from 'vitest'

import { useRealtimeStore } from '@/features/realtime/realtime-store'
import { apiClient } from '@/shared/api/api-client'
import { lastFakeSocket } from '@/shared/testing/fake-socket'
import type { InProcessBroadcastChannelClass } from '@/shared/testing/broadcast-channel'
import {
  deferred,
  json,
  networkFailure,
  noContent,
  problem,
  stubFetch,
} from '@/shared/testing/fetch-stub'
import { lockManager } from '@/shared/testing/web-locks'

import {
  minimumRefreshDelayMilliseconds,
  refreshLeadMilliseconds,
  refreshRetryMilliseconds,
  useSessionStore,
  type SessionEndReason,
} from '../session-store'
import { sessionChannelName, sessionRefreshLockName } from '../session-sync'
import {
  accessToken,
  demoAdministratorEmail,
  password,
  sessionResponse,
  sessionResponseBody,
} from './session-fixtures'

const signInRoute = 'POST /api/auth/sign-in'
const refreshRoute = 'POST /api/auth/refresh'
const signOutRoute = 'POST /api/auth/sign-out'
const templatesRoute = 'GET /api/checklist-templates'

const issuedAt = new Date('2026-10-21T14:40:00.000Z')

function minutesAfterIssue(minutes: number): Date {
  return new Date(issuedAt.getTime() + minutes * 60_000)
}

function broadcastChannelClass(): InProcessBroadcastChannelClass {
  return globalThis.BroadcastChannel as unknown as InProcessBroadcastChannelClass
}

function recordEnds(store: ReturnType<typeof useSessionStore>): SessionEndReason[] {
  const reasons: SessionEndReason[] = []
  store.onSessionEnded((reason) => reasons.push(reason))
  return reasons
}

async function settle(): Promise<void> {
  for (let turn = 0; turn < 10; turn += 1) {
    await Promise.resolve()
  }
}

describe('sign-in', () => {
  it('posts the credentials for the Web client and keeps the access token only in memory', async () => {
    const token = accessToken()
    const secret = password()
    const stub = stubFetch({
      [signInRoute]: json(sessionResponse(token)),
      [templatesRoute]: json({ items: [] }),
    })
    const session = useSessionStore()

    await session.signIn(demoAdministratorEmail, secret)

    expect(JSON.parse(stub.requestsTo(signInRoute)[0]?.body ?? '')).toEqual({
      email: demoAdministratorEmail,
      password: secret,
      client: 'Web',
    })
    expect(session.status).toBe('signed-in')
    expect(session.isAdministrator).toBe(true)
    expect(session.user?.displayName).toBe('Dana Whitfield')
    expect(localStorage.length).toBe(0)
    expect(sessionStorage.length).toBe(0)
    expect(document.cookie).toBe('')
    expect(JSON.stringify(session.$state)).not.toContain(token)
    await apiClient.GET('/api/checklist-templates')
    expect(stub.requestsTo(templatesRoute)[0]?.headers.get('Authorization')).toBe(`Bearer ${token}`)
  })

  it('leaves the session signed out when the API refuses the credentials', async () => {
    stubFetch({ [signInRoute]: problem(401, 'auth.invalid-credentials') })
    const session = useSessionStore()

    await expect(session.signIn(demoAdministratorEmail, password())).rejects.toMatchObject({
      status: 401,
      code: 'auth.invalid-credentials',
    })
    expect(session.isSignedIn).toBe(false)
  })
})

describe('restore', () => {
  it('restores the session through the refresh cookie with the anti-forgery header, once', async () => {
    const stub = stubFetch({ [refreshRoute]: json(sessionResponse()) })
    const session = useSessionStore()

    await Promise.all([session.restore(), session.restore()])
    await session.restore()

    const requests = stub.requestsTo(refreshRoute)
    expect(requests).toHaveLength(1)
    expect(requests[0]?.headers.get('X-FieldService-Csrf')).toBe('1')
    expect(requests[0]?.body).toBe('')
    expect(session.status).toBe('signed-in')
    expect(lockManager().grantsOf(sessionRefreshLockName)).toBe(1)
  })

  it('ends signed out without a notice when there is no session to restore', async () => {
    stubFetch({ [refreshRoute]: problem(401, 'auth.session-expired') })
    const session = useSessionStore()
    const ends = recordEnds(session)

    await session.restore()

    expect(session.status).toBe('signed-out')
    expect(session.endReason).toBeNull()
    expect(ends).toEqual([])
  })

  it('ends signed out when the API cannot be reached', async () => {
    stubFetch({ [refreshRoute]: networkFailure() })
    const session = useSessionStore()

    await session.restore()

    expect(session.status).toBe('signed-out')
  })
})

describe('scheduled refresh', () => {
  it('refreshes 60 seconds before the access token expires, then again before the next expiry', async () => {
    vi.useFakeTimers({ now: issuedAt })
    const renewed = accessToken()
    const stub = stubFetch({
      [signInRoute]: json(sessionResponse(accessToken(), minutesAfterIssue(15))),
      [refreshRoute]: [
        json(sessionResponse(renewed, minutesAfterIssue(29))),
        json(sessionResponse(accessToken(), minutesAfterIssue(43))),
      ],
      [templatesRoute]: json({ items: [] }),
    })
    const session = useSessionStore()
    await session.signIn(demoAdministratorEmail, password())

    await vi.advanceTimersByTimeAsync(15 * 60_000 - refreshLeadMilliseconds - 1)
    expect(stub.requestsTo(refreshRoute)).toHaveLength(0)

    await vi.advanceTimersByTimeAsync(1)
    await vi.waitFor(() => expect(stub.requestsTo(refreshRoute)).toHaveLength(1))
    await settle()
    await apiClient.GET('/api/checklist-templates')
    expect(stub.requestsTo(templatesRoute)[0]?.headers.get('Authorization')).toBe(
      `Bearer ${renewed}`,
    )

    vi.setSystemTime(minutesAfterIssue(28))
    await vi.advanceTimersByTimeAsync(14 * 60_000)
    await vi.waitFor(() => expect(stub.requestsTo(refreshRoute)).toHaveLength(2))
  })

  it('waits at least the minimum delay when the expiry is already close', async () => {
    vi.useFakeTimers({ now: issuedAt })
    const stub = stubFetch({
      [signInRoute]: json(sessionResponse(accessToken(), new Date(issuedAt.getTime() + 30_000))),
      [refreshRoute]: json(sessionResponse()),
    })
    await useSessionStore().signIn(demoAdministratorEmail, password())

    await vi.advanceTimersByTimeAsync(minimumRefreshDelayMilliseconds - 1)
    expect(stub.requestsTo(refreshRoute)).toHaveLength(0)
    await vi.advanceTimersByTimeAsync(1)
    await vi.waitFor(() => expect(stub.requestsTo(refreshRoute)).toHaveLength(1))
  })

  it('ends the session as expired when the scheduled refresh is refused', async () => {
    vi.useFakeTimers({ now: issuedAt })
    stubFetch({
      [signInRoute]: json(sessionResponse(accessToken(), minutesAfterIssue(15))),
      [refreshRoute]: problem(401, 'auth.session-expired'),
    })
    const session = useSessionStore()
    const ends = recordEnds(session)
    await session.signIn(demoAdministratorEmail, password())

    await vi.advanceTimersByTimeAsync(14 * 60_000)
    await vi.waitFor(() => expect(session.status).toBe('signed-out'))

    expect(session.endReason).toBe('expired')
    expect(session.user).toBeNull()
    expect(ends).toEqual(['expired'])
  })

  it('keeps the session and tries again 30 seconds later when the refresh cannot reach the API', async () => {
    vi.useFakeTimers({ now: issuedAt })
    const stub = stubFetch({
      [signInRoute]: json(sessionResponse(accessToken(), minutesAfterIssue(15))),
      [refreshRoute]: [networkFailure(), json(sessionResponse())],
    })
    const session = useSessionStore()
    await session.signIn(demoAdministratorEmail, password())

    await vi.advanceTimersByTimeAsync(14 * 60_000)
    await vi.waitFor(() => expect(stub.requestsTo(refreshRoute)).toHaveLength(1))
    await settle()
    expect(session.isSignedIn).toBe(true)

    await vi.advanceTimersByTimeAsync(refreshRetryMilliseconds)
    await vi.waitFor(() => expect(stub.requestsTo(refreshRoute)).toHaveLength(2))
    expect(session.isSignedIn).toBe(true)
  })
})

describe('refreshSession', () => {
  it('shares one refresh request and one lock among concurrent callers', async () => {
    const refresh = deferred()
    const stub = stubFetch({
      [signInRoute]: json(sessionResponse()),
      [refreshRoute]: refresh.reply,
    })
    const session = useSessionStore()
    await session.signIn(demoAdministratorEmail, password())

    const outcomes = Promise.all([session.refreshSession(), session.refreshSession()])
    await refresh.requested
    refresh.resolve(sessionResponseBody(accessToken()))

    expect(await outcomes).toEqual(['refreshed', 'refreshed'])
    expect(stub.requestsTo(refreshRoute)).toHaveLength(1)
    expect(lockManager().grantsOf(sessionRefreshLockName)).toBe(1)
  })

  it('discards a refresh that completes after sign-out started', async () => {
    const refresh = deferred()
    stubFetch({
      [signInRoute]: json(sessionResponse()),
      [refreshRoute]: refresh.reply,
      [signOutRoute]: noContent(),
    })
    const session = useSessionStore()
    await session.signIn(demoAdministratorEmail, password())

    const refreshing = session.refreshSession()
    await refresh.requested
    const signingOut = session.signOut()
    refresh.resolve(sessionResponseBody(accessToken()))

    expect(await refreshing).toBe('ended')
    await signingOut
    expect(session.status).toBe('signed-out')
  })
})

describe('sign-out', () => {
  it('posts sign-out under the Web Lock, clears the session and tells the other tabs', async () => {
    const stub = stubFetch({ [signInRoute]: json(sessionResponse()), [signOutRoute]: noContent() })
    const session = useSessionStore()
    const ends = recordEnds(session)
    await session.signIn(demoAdministratorEmail, password())

    await session.signOut()

    const [request] = stub.requestsTo(signOutRoute)
    expect(request?.headers.get('X-FieldService-Csrf')).toBe('1')
    expect(request?.body).toBe('')
    expect(lockManager().grantsOf(sessionRefreshLockName)).toBe(1)
    expect(session.status).toBe('signed-out')
    expect(session.user).toBeNull()
    expect(session.endReason).toBe('signed-out')
    expect(ends).toEqual(['signed-out'])
    expect(broadcastChannelClass().posted).toEqual([
      { name: sessionChannelName, data: { type: 'signed-out' } },
    ])
  })

  it('disconnects live updates when the session signs out', async () => {
    stubFetch({ [signInRoute]: json(sessionResponse()), [signOutRoute]: noContent() })
    const session = useSessionStore()
    await session.signIn(demoAdministratorEmail, password())
    const realtime = useRealtimeStore()
    realtime.start()
    const socket = lastFakeSocket()

    await session.signOut()

    expect(socket.disconnectCalls).toBe(1)
    expect(realtime.state).toBe('idle')
  })

  it('ends the session as expired when the realtime ticket reports an expired session', async () => {
    const stub = stubFetch({ [signInRoute]: json(sessionResponse()) })
    const session = useSessionStore()
    const ends = recordEnds(session)
    await session.signIn(demoAdministratorEmail, password())

    session.expireSession()
    session.expireSession()

    expect(session.status).toBe('signed-out')
    expect(session.endReason).toBe('expired')
    expect(ends).toEqual(['expired'])
    expect(stub.requests).toHaveLength(1)
    expect(broadcastChannelClass().posted).toEqual([])
  })

  it('clears the session even when the sign-out request fails', async () => {
    stubFetch({ [signInRoute]: json(sessionResponse()), [signOutRoute]: networkFailure() })
    const session = useSessionStore()
    await session.signIn(demoAdministratorEmail, password())

    await session.signOut()

    expect(session.isSignedIn).toBe(false)
    expect(broadcastChannelClass().posted).toHaveLength(1)
  })

  it('signs out a second tab when the first tab signs out', async () => {
    const stub = stubFetch({ [signInRoute]: json(sessionResponse()), [signOutRoute]: noContent() })
    const firstTab = useSessionStore()
    await firstTab.signIn(demoAdministratorEmail, password())
    setActivePinia(createPinia())
    const secondTab = useSessionStore()
    const secondTabEnds = recordEnds(secondTab)
    await secondTab.signIn(demoAdministratorEmail, password())

    await firstTab.signOut()
    await settle()

    expect(secondTab.isSignedIn).toBe(false)
    expect(secondTab.endReason).toBe('signed-out-elsewhere')
    expect(secondTabEnds).toEqual(['signed-out-elsewhere'])
    expect(stub.requestsTo(signOutRoute)).toHaveLength(1)
  })

  it('ignores channel messages other than signed-out', async () => {
    stubFetch({ [signInRoute]: json(sessionResponse()) })
    const session = useSessionStore()
    await session.signIn(demoAdministratorEmail, password())
    const otherTab = new BroadcastChannel(sessionChannelName)

    otherTab.postMessage({ type: 'signed-in' })
    otherTab.postMessage('signed-out')
    otherTab.postMessage(null)
    await settle()

    expect(session.isSignedIn).toBe(true)
  })
})
