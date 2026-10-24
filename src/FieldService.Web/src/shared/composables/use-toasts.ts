import { defineStore } from 'pinia'
import { ref, shallowRef } from 'vue'

export type ToastKind = 'event' | 'error'

export type ToastLayout = 'wide' | 'narrow'

export interface ToastMessage {
  readonly id: number
  readonly kind: ToastKind
  readonly title: string
  readonly body: string | null
  readonly note: string | null
  readonly meta: string | null
  readonly metaDetail: string | null
  readonly reference: string | null
  readonly announcement: string | null
  readonly durationMs: number | null
}

export interface ToastRequest {
  readonly kind?: ToastKind
  readonly title: string
  readonly body?: string | null
  readonly note?: string | null
  readonly meta?: string | null
  readonly metaDetail?: string | null
  readonly reference?: string | null
  readonly announcement?: string | null
  readonly durationMs?: number | null
}

export const maximumToasts = 3

export const narrowMaximumToasts = 2

export const narrowMaximumEventToasts = 1

export const wideToastLayoutQuery = '(min-width: 600px)'

export const eventToastMilliseconds = 8_000

export const errorToastTitle = 'Something went wrong'

export const errorToastBody =
  'Your last action may not have finished. Check the page, then try again.'

const useToastQueue = defineStore('toasts', () => {
  const toasts = ref<ToastMessage[]>([])
  const layout = shallowRef<ToastLayout>('wide')
  const held = new Set<number>()
  let nextId = 1

  function limitsOf(current: ToastLayout): { total: number; events: number } {
    return current === 'narrow'
      ? { total: narrowMaximumToasts, events: narrowMaximumEventToasts }
      : { total: maximumToasts, events: maximumToasts }
  }

  function enforceLimits(newestId: number | null): void {
    const limits = limitsOf(layout.value)
    let list = toasts.value
    for (;;) {
      const events = list.filter((toast) => toast.kind === 'event')
      if (list.length <= limits.total && events.length <= limits.events) {
        break
      }
      const leaving = events.find((toast) => !held.has(toast.id) && toast.id !== newestId)
      if (leaving === undefined) {
        break
      }
      list = list.filter((toast) => toast.id !== leaving.id)
    }
    if (list.length !== toasts.value.length) {
      toasts.value = list
    }
  }

  function show(request: ToastRequest): number {
    const kind = request.kind ?? 'event'
    const toast: ToastMessage = {
      id: nextId,
      kind,
      title: request.title,
      body: request.body ?? null,
      note: request.note ?? null,
      meta: request.meta ?? null,
      metaDetail: request.metaDetail ?? null,
      reference: request.reference ?? null,
      announcement: request.announcement ?? null,
      durationMs:
        request.durationMs !== undefined
          ? request.durationMs
          : kind === 'error'
            ? null
            : eventToastMilliseconds,
    }
    nextId += 1
    toasts.value = [...toasts.value, toast]
    enforceLimits(toast.id)
    return toast.id
  }

  function showError(reference: string | null): number {
    const shown = toasts.value.find(
      (toast) => toast.kind === 'error' && toast.reference === reference,
    )
    if (shown !== undefined) {
      return shown.id
    }
    return show({ kind: 'error', title: errorToastTitle, body: errorToastBody, reference })
  }

  function attachReference(id: number, reference: string): boolean {
    const target = toasts.value.find((toast) => toast.id === id)
    if (target === undefined || target.kind !== 'error' || target.reference !== null) {
      return false
    }
    toasts.value = toasts.value.map((toast) => (toast.id === id ? { ...toast, reference } : toast))
    return true
  }

  function dismiss(id: number): void {
    held.delete(id)
    toasts.value = toasts.value.filter((toast) => toast.id !== id)
  }

  function hold(id: number): void {
    held.add(id)
  }

  function release(id: number): void {
    if (held.delete(id)) {
      enforceLimits(null)
    }
  }

  function setLayout(next: ToastLayout): void {
    if (layout.value === next) {
      return
    }
    layout.value = next
    enforceLimits(null)
  }

  function clear(): void {
    held.clear()
    toasts.value = []
  }

  return {
    toasts,
    layout,
    show,
    showError,
    attachReference,
    dismiss,
    hold,
    release,
    setLayout,
    clear,
  }
})

export function useToasts(): ReturnType<typeof useToastQueue> {
  return useToastQueue()
}
