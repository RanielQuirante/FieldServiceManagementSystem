<script setup lang="ts">
import { storeToRefs } from 'pinia'
import { computed, nextTick, onBeforeUnmount, onMounted, useTemplateRef, watch } from 'vue'

import { useFocusClearance } from '@/shared/composables/use-focus-clearance'
import { useToasts, wideToastLayoutQuery } from '@/shared/composables/use-toasts'

import Toast from './Toast.vue'

const props = withDefaults(
  defineProps<{
    placement?: 'shell' | 'sign-in'
  }>(),
  { placement: 'shell' },
)

const queue = useToasts()

const { toasts } = storeToRefs(queue)

const region = useTemplateRef<HTMLElement>('region')

const stack = useTemplateRef<HTMLElement>('stack')

const announcements = computed(() =>
  toasts.value.filter((toast) => toast.kind === 'event' && toast.announcement !== null),
)

useFocusClearance(stack, props.placement === 'shell' ? '--spacing-fs-56' : '--spacing-fs-48')

let focusOrigin: HTMLElement | null = null

let layoutQuery: MediaQueryList | null = null

function onDocumentFocusIn(event: FocusEvent): void {
  const target = event.target
  if (target instanceof HTMLElement && region.value?.contains(target) !== true) {
    focusOrigin = target
  }
}

function dismissButtonOf(id: number): HTMLElement | null {
  return stack.value?.querySelector<HTMLElement>(`[data-toast-id="${String(id)}"] button`) ?? null
}

function focusAfterDismissal(target: number | null): void {
  const button = target === null ? null : dismissButtonOf(target)
  if (button !== null) {
    button.focus()
    return
  }
  if (focusOrigin !== null && focusOrigin.isConnected) {
    focusOrigin.focus()
    if (document.activeElement === focusOrigin) {
      return
    }
  }
  document.getElementById('main')?.focus()
}

function focusedToastId(): number | null {
  const active = document.activeElement
  if (!(active instanceof HTMLElement) || stack.value?.contains(active) !== true) {
    return null
  }
  const holder = active.closest<HTMLElement>('[data-toast-id]')
  const id = Number(holder?.dataset.toastId)
  return Number.isInteger(id) ? id : null
}

watch(
  () => toasts.value.map((toast) => toast.id),
  (ids, previous) => {
    const focused = focusedToastId()
    if (focused === null || ids.includes(focused)) {
      return
    }
    const index = previous.indexOf(focused)
    const newer = previous.slice(index + 1).find((id) => ids.includes(id))
    const older = previous
      .slice(0, Math.max(index, 0))
      .reverse()
      .find((id) => ids.includes(id))
    const target = newer ?? older ?? null
    void nextTick(() => {
      focusAfterDismissal(target)
    })
  },
  { flush: 'pre' },
)

function applyLayout(): void {
  queue.setLayout(layoutQuery === null || layoutQuery.matches ? 'wide' : 'narrow')
}

onMounted(() => {
  document.addEventListener('focusin', onDocumentFocusIn)
  const active = document.activeElement
  if (active instanceof HTMLElement && active !== document.body) {
    focusOrigin = active
  }
  if (typeof window.matchMedia !== 'function') {
    return
  }
  layoutQuery = window.matchMedia(wideToastLayoutQuery)
  applyLayout()
  layoutQuery.addEventListener('change', applyLayout)
})

onBeforeUnmount(() => {
  document.removeEventListener('focusin', onDocumentFocusIn)
  layoutQuery?.removeEventListener('change', applyLayout)
  layoutQuery = null
})
</script>

<template>
  <div
    ref="region"
    :class="['toast-host', `toast-host--${placement}`]"
    role="region"
    aria-label="Notifications"
  >
    <div class="visually-hidden" role="status" aria-atomic="false">
      <p v-for="toast in announcements" :key="toast.id">{{ toast.announcement }}</p>
    </div>
    <div ref="stack" class="toast-host__stack">
      <TransitionGroup name="toast">
        <Toast
          v-for="toast in toasts"
          :key="toast.id"
          :toast="toast"
          @dismiss="queue.dismiss(toast.id)"
          @hold="queue.hold(toast.id)"
          @release="queue.release(toast.id)"
        />
      </TransitionGroup>
    </div>
  </div>
</template>

<style scoped>
.toast-host {
  position: fixed;
  right: clamp(16px, 3vw, 40px);
  bottom: var(--spacing-fs-16);
  z-index: 30;
  width: min(380px, calc(100vw - 32px));
}

.toast-host__stack {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-fs-12);
}

@media (width < 600px) {
  .toast-host {
    right: var(--spacing-fs-16);
    bottom: calc(var(--spacing-fs-16) + env(safe-area-inset-bottom));
    left: var(--spacing-fs-16);
    width: auto;
  }
}

.toast-enter-active,
.toast-leave-active {
  transition:
    opacity var(--fs-duration-sheet),
    transform var(--fs-duration-sheet);
}

.toast-enter-from,
.toast-leave-to {
  opacity: 0;
  transform: translateY(8px);
}
</style>
