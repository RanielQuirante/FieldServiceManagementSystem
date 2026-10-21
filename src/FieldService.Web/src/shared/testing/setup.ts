import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, vi } from 'vitest'

import { connectSessionAccess } from '@/shared/api/auth-fetch'

import { createInProcessBroadcastChannel } from './broadcast-channel'
import { installLockManager, removeLockManager } from './web-locks'

beforeEach(() => {
  setActivePinia(createPinia())
  connectSessionAccess(null)
  vi.stubGlobal('BroadcastChannel', createInProcessBroadcastChannel())
  installLockManager()
})

afterEach(() => {
  vi.clearAllMocks()
  vi.restoreAllMocks()
  vi.useRealTimers()
  vi.unstubAllGlobals()
  removeLockManager()
})
