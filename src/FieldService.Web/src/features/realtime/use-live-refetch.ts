import { computed, onScopeDispose, type ComputedRef } from 'vue'

import type { LiveRefetchRegistration } from './live-refetch'
import { useRealtimeStore } from './realtime-store'

export interface LiveRefetchHandle {
  readonly flashedJobIds: ComputedRef<ReadonlySet<string>>
  markFresh(): void
}

export function useLiveRefetch(registration: LiveRefetchRegistration): LiveRefetchHandle {
  const realtime = useRealtimeStore()
  const unregister = realtime.register(registration)
  onScopeDispose(unregister)
  return {
    flashedJobIds: computed(() => realtime.flashedJobIds),
    markFresh: () => {
      realtime.markFresh()
    },
  }
}
