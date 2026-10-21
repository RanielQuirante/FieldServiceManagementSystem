import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

import { isApiError } from '@/shared/api/api-error'
import { connectSessionAccess } from '@/shared/api/auth-fetch'

import * as authApi from './auth-api'
import type { SessionResponse, SessionUser } from './auth-api'
import { openSessionChannel, withSessionLock } from './session-sync'

export type SessionStatus = 'unknown' | 'signed-in' | 'signed-out'

export type SessionEndReason = 'expired' | 'signed-out' | 'signed-out-elsewhere'

export type RefreshOutcome = 'refreshed' | 'ended' | 'failed'

export type SessionEndListener = (reason: SessionEndReason) => void

export const refreshLeadMilliseconds = 60_000

export const minimumRefreshDelayMilliseconds = 15_000

export const refreshRetryMilliseconds = 30_000

export const useSessionStore = defineStore('session', () => {
  const user = ref<SessionUser | null>(null)
  const status = ref<SessionStatus>('unknown')
  const endReason = ref<SessionEndReason | null>(null)

  const isSignedIn = computed(() => status.value === 'signed-in')
  const isAdministrator = computed(() => isSignedIn.value && user.value?.role === 'Administrator')

  let accessToken: string | null = null
  let generation = 0
  let refreshTimer: ReturnType<typeof setTimeout> | undefined
  let restoring: Promise<void> | null = null
  let refreshing: Promise<RefreshOutcome> | null = null
  const endListeners = new Set<SessionEndListener>()

  function cancelRefreshTimer(): void {
    if (refreshTimer !== undefined) {
      clearTimeout(refreshTimer)
      refreshTimer = undefined
    }
  }

  function scheduleRefresh(delay: number): void {
    cancelRefreshTimer()
    refreshTimer = setTimeout(onRefreshTimer, delay)
  }

  function scheduleRefreshBefore(expiresAt: string): void {
    const expiry = Date.parse(expiresAt)
    if (Number.isNaN(expiry)) {
      cancelRefreshTimer()
      return
    }
    const delay = expiry - Date.now() - refreshLeadMilliseconds
    scheduleRefresh(Math.max(delay, minimumRefreshDelayMilliseconds))
  }

  function onRefreshTimer(): void {
    refreshTimer = undefined
    void refreshSession().then((outcome) => {
      if (outcome === 'failed' && status.value === 'signed-in') {
        scheduleRefresh(refreshRetryMilliseconds)
      }
    })
  }

  function applySession(session: SessionResponse): void {
    accessToken = session.accessToken
    user.value = session.user
    status.value = 'signed-in'
    endReason.value = null
    scheduleRefreshBefore(session.accessTokenExpiresAt)
  }

  function endSession(reason: SessionEndReason): void {
    generation += 1
    const wasSignedIn = status.value === 'signed-in'
    accessToken = null
    user.value = null
    status.value = 'signed-out'
    cancelRefreshTimer()
    if (!wasSignedIn) {
      return
    }
    endReason.value = reason
    for (const listener of [...endListeners]) {
      listener(reason)
    }
  }

  async function runRefresh(requestedIn: number): Promise<RefreshOutcome> {
    return withSessionLock(async () => {
      if (requestedIn !== generation) {
        return 'ended'
      }
      try {
        const session = await authApi.refreshSession()
        if (requestedIn !== generation) {
          return 'ended'
        }
        applySession(session)
        return 'refreshed'
      } catch (error) {
        if (requestedIn !== generation) {
          return 'ended'
        }
        if (isApiError(error) && error.status === 401) {
          endSession('expired')
          return 'ended'
        }
        return 'failed'
      }
    })
  }

  function refreshSession(): Promise<RefreshOutcome> {
    refreshing ??= runRefresh(generation)
      .catch((): RefreshOutcome => 'failed')
      .finally(() => {
        refreshing = null
      })
    return refreshing
  }

  async function renewAccessToken(): Promise<string | null> {
    const outcome = await refreshSession()
    return outcome === 'refreshed' ? accessToken : null
  }

  function restore(): Promise<void> {
    restoring ??= refreshSession().then((outcome) => {
      if (outcome !== 'refreshed' && status.value === 'unknown') {
        status.value = 'signed-out'
      }
    })
    return restoring
  }

  async function signIn(email: string, password: string): Promise<void> {
    const session = await authApi.signIn(email, password)
    applySession(session)
  }

  async function signOut(): Promise<void> {
    generation += 1
    cancelRefreshTimer()
    await withSessionLock(() => authApi.signOut().catch(() => undefined))
    endSession('signed-out')
    channel.announceSignedOut()
  }

  function onSessionEnded(listener: SessionEndListener): () => void {
    endListeners.add(listener)
    return () => {
      endListeners.delete(listener)
    }
  }

  const channel = openSessionChannel(() => endSession('signed-out-elsewhere'))

  connectSessionAccess({
    currentAccessToken: () => accessToken,
    renewAccessToken,
  })

  return {
    user,
    status,
    endReason,
    isSignedIn,
    isAdministrator,
    restore,
    signIn,
    signOut,
    refreshSession,
    onSessionEnded,
  }
})
