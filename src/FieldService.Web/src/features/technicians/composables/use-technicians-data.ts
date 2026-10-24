import { onScopeDispose } from 'vue'

import { useLiveRefetch, type LivePageCopy } from '@/features/realtime/use-live-refetch'

import { useTechniciansStore } from '../technicians-store'

export interface TechniciansData {
  readonly store: ReturnType<typeof useTechniciansStore>
  retry(): Promise<void>
}

export function useTechniciansData(page: LivePageCopy): TechniciansData {
  const store = useTechniciansStore()

  const live = useLiveRefetch({
    refetch: () => store.refresh(),
    refetchLists: () => store.refresh(),
    page,
  })

  async function load(): Promise<void> {
    await store.load()
    if (store.status === 'ready') {
      live.markFresh()
    }
  }

  void load()

  onScopeDispose(() => {
    store.close()
  })

  return { store, retry: load }
}
