import { defineStore } from 'pinia'
import { ref } from 'vue'

export type ToastKind = 'event' | 'error'

export interface ToastMessage {
  readonly id: number
  readonly kind: ToastKind
  readonly title: string
  readonly body: string | null
  readonly meta: string | null
  readonly reference: string | null
  readonly durationMs: number | null
}

export interface ToastRequest {
  readonly kind?: ToastKind
  readonly title: string
  readonly body?: string | null
  readonly meta?: string | null
  readonly reference?: string | null
  readonly durationMs?: number | null
}

export const maximumToasts = 3

export const eventToastMilliseconds = 8_000

export const errorToastTitle = 'Something went wrong'

export const errorToastBody =
  'Your last action may not have finished. Check the page, then try again.'

const useToastQueue = defineStore('toasts', () => {
  const toasts = ref<ToastMessage[]>([])
  let nextId = 1

  function show(request: ToastRequest): number {
    const kind = request.kind ?? 'event'
    const toast: ToastMessage = {
      id: nextId,
      kind,
      title: request.title,
      body: request.body ?? null,
      meta: request.meta ?? null,
      reference: request.reference ?? null,
      durationMs:
        request.durationMs !== undefined
          ? request.durationMs
          : kind === 'error'
            ? null
            : eventToastMilliseconds,
    }
    nextId += 1
    toasts.value = [toast, ...toasts.value].slice(0, maximumToasts)
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

  function dismiss(id: number): void {
    toasts.value = toasts.value.filter((toast) => toast.id !== id)
  }

  function clear(): void {
    toasts.value = []
  }

  return { toasts, show, showError, dismiss, clear }
})

export function useToasts(): ReturnType<typeof useToastQueue> {
  return useToastQueue()
}
