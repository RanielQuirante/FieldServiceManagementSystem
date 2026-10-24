import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, vi } from 'vitest'

import { connectSessionAccess } from '@/shared/api/auth-fetch'

import { createInProcessBroadcastChannel } from './broadcast-channel'
import { installDialogStandIn, removeDialogStandIn } from './dialog'
import { resetFakeSockets } from './fake-socket'
import { installLockManager, removeLockManager } from './web-locks'

vi.mock('socket.io-client', async () => {
  const { fakeIo } = await import('./fake-socket')
  return { io: fakeIo }
})

beforeEach(() => {
  setActivePinia(createPinia())
  connectSessionAccess(null)
  resetFakeSockets()
  vi.stubGlobal('BroadcastChannel', createInProcessBroadcastChannel())
  installLockManager()
  installDialogStandIn()
})

afterEach(() => {
  vi.clearAllMocks()
  vi.restoreAllMocks()
  vi.useRealTimers()
  vi.unstubAllGlobals()
  removeLockManager()
  removeDialogStandIn()
})
