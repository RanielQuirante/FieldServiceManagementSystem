import { onScopeDispose, watch } from 'vue'

import { useSessionStore } from '@/features/auth/session-store'

import { useRealtimeStore } from './realtime-store'

export function useRealtimeConnection(): ReturnType<typeof useRealtimeStore> {
  const session = useSessionStore()
  const realtime = useRealtimeStore()

  watch(
    () => session.isAdministrator,
    (administrator) => {
      if (administrator) {
        realtime.start()
      } else {
        realtime.stop()
      }
    },
    { immediate: true },
  )

  onScopeDispose(() => {
    realtime.stop()
  })

  return realtime
}
