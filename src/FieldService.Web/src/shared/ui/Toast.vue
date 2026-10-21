<script setup lang="ts">
import { onBeforeUnmount, onMounted } from 'vue'

import type { ToastMessage } from '@/shared/composables/use-toasts'

import AppIcon from './AppIcon.vue'
import IconButton from './IconButton.vue'

const props = defineProps<{
  toast: ToastMessage
}>()

const emit = defineEmits<{
  dismiss: []
}>()

let timer: ReturnType<typeof setTimeout> | undefined
let remaining = props.toast.durationMs
let startedAt = 0
let hovered = false
let focused = false

function stopTimer(): void {
  if (timer !== undefined) {
    clearTimeout(timer)
    timer = undefined
  }
}

function startTimer(): void {
  if (remaining === null || timer !== undefined || hovered || focused) {
    return
  }
  startedAt = Date.now()
  timer = setTimeout(() => {
    timer = undefined
    emit('dismiss')
  }, remaining)
}

function pause(): void {
  if (timer === undefined || remaining === null) {
    return
  }
  stopTimer()
  remaining = Math.max(remaining - (Date.now() - startedAt), 0)
}

function onPointerEnter(): void {
  pause()
  hovered = true
}

function onPointerLeave(): void {
  hovered = false
  startTimer()
}

function onFocusIn(): void {
  pause()
  focused = true
}

function onFocusOut(event: FocusEvent): void {
  const next = event.relatedTarget
  if (next instanceof Node && (event.currentTarget as HTMLElement).contains(next)) {
    return
  }
  focused = false
  startTimer()
}

onMounted(startTimer)

onBeforeUnmount(stopTimer)
</script>

<template>
  <div
    :class="['toast', `toast--${toast.kind}`]"
    :role="toast.kind === 'error' ? 'alert' : 'status'"
    data-surface="dark"
    @mouseenter="onPointerEnter"
    @mouseleave="onPointerLeave"
    @focusin="onFocusIn"
    @focusout="onFocusOut"
  >
    <span v-if="toast.kind === 'error'" class="toast__tile" aria-hidden="true">
      <AppIcon name="alert-triangle" :size="18" />
    </span>
    <span v-else class="toast__dot" aria-hidden="true"></span>
    <span class="toast__text">
      <span v-if="toast.meta" class="toast__meta">{{ toast.meta }}</span>
      <span class="toast__title">{{ toast.title }}</span>
      <span v-if="toast.body" class="toast__body">{{ toast.body }}</span>
      <span v-if="toast.reference" class="toast__reference"
        >Reference <span class="toast__reference-id">{{ toast.reference }}</span></span
      >
    </span>
    <IconButton label="Dismiss notification" icon="x" variant="on-dark" @click="emit('dismiss')" />
  </div>
</template>

<style scoped>
.toast {
  display: flex;
  flex: none;
  align-items: flex-start;
  gap: var(--spacing-fs-12);
  width: 100%;
  padding: var(--spacing-fs-14) var(--spacing-fs-8) var(--spacing-fs-14) var(--spacing-fs-16);
  border: 1px solid var(--color-fs-toast-border);
  border-radius: var(--radius-fs-toast);
  background: var(--fs-gradient-toast);
  -webkit-backdrop-filter: blur(var(--blur-fs-glass));
  backdrop-filter: blur(var(--blur-fs-glass));
  box-shadow: var(--shadow-fs-toast);
  color: var(--color-fs-on-dark);
}

.toast__dot {
  flex: none;
  width: 10px;
  height: 10px;
  margin-top: 5px;
  border-radius: var(--radius-fs-pill);
  background: var(--fs-gradient-accent);
  box-shadow: var(--shadow-fs-toast-dot);
}

.toast__tile {
  flex: none;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  border-radius: var(--radius-fs-control-sm);
  background: var(--color-fs-danger);
  box-shadow: var(--shadow-fs-danger-tile);
  color: var(--color-fs-on-dark);
}

.toast__text {
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: var(--spacing-fs-4);
  min-width: 0;
}

.toast__meta {
  color: var(--color-fs-on-dark-2);
  font-family: var(--font-mono);
  font-size: var(--text-fs-mono-sm);
  font-weight: var(--text-fs-mono-sm--font-weight);
  font-variant-numeric: tabular-nums;
}

.toast__title {
  font-size: var(--text-fs-body);
  font-weight: 600;
  line-height: 1.35;
}

.toast__body {
  color: var(--color-fs-on-dark-2);
  font-size: var(--text-fs-table);
  line-height: var(--text-fs-table--line-height);
  text-wrap: pretty;
}

.toast__reference {
  color: var(--color-fs-on-dark-2);
  font-size: 0.75rem;
  line-height: 1.4;
}

.toast__reference-id {
  display: inline-block;
  max-width: 100%;
  font-family: var(--font-mono);
  font-weight: 500;
  overflow-wrap: anywhere;
}

@media (forced-colors: active) {
  .toast {
    border-color: CanvasText;
  }
}
</style>
