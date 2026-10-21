export const sessionRefreshLockName = 'fieldservice.session-refresh'

export const sessionChannelName = 'fieldservice.session'

export interface SignedOutMessage {
  readonly type: 'signed-out'
}

export interface SessionChannel {
  announceSignedOut(): void
  close(): void
}

export function withSessionLock<T>(task: () => Promise<T>): Promise<T> {
  if (!('locks' in navigator)) {
    return task()
  }
  return navigator.locks.request(sessionRefreshLockName, () => task())
}

export function isSignedOutMessage(value: unknown): value is SignedOutMessage {
  return (
    typeof value === 'object' && value !== null && 'type' in value && value.type === 'signed-out'
  )
}

export function openSessionChannel(onSignedOutElsewhere: () => void): SessionChannel {
  if (typeof BroadcastChannel === 'undefined') {
    return { announceSignedOut: () => undefined, close: () => undefined }
  }
  const channel = new BroadcastChannel(sessionChannelName)
  channel.addEventListener('message', (event: MessageEvent<unknown>) => {
    if (isSignedOutMessage(event.data)) {
      onSignedOutElsewhere()
    }
  })
  const message: SignedOutMessage = { type: 'signed-out' }
  return {
    announceSignedOut: () => channel.postMessage(message),
    close: () => channel.close(),
  }
}
