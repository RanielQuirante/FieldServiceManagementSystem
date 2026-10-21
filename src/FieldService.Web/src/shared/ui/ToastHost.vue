<script setup lang="ts">
import { storeToRefs } from 'pinia'
import { useTemplateRef } from 'vue'

import { useFocusClearance } from '@/shared/composables/use-focus-clearance'
import { useToasts } from '@/shared/composables/use-toasts'

import Toast from './Toast.vue'

withDefaults(
  defineProps<{
    placement?: 'main' | 'viewport'
  }>(),
  { placement: 'main' },
)

const queue = useToasts()

const { toasts } = storeToRefs(queue)

const stack = useTemplateRef<HTMLElement>('stack')

useFocusClearance(stack)
</script>

<template>
  <div
    :class="['toast-host', `toast-host--${placement}`]"
    role="region"
    aria-label="Notifications"
    aria-live="polite"
  >
    <div ref="stack" class="toast-host__stack">
      <TransitionGroup name="toast">
        <Toast
          v-for="toast in toasts"
          :key="toast.id"
          :toast="toast"
          @dismiss="queue.dismiss(toast.id)"
        />
      </TransitionGroup>
    </div>
  </div>
</template>

<style scoped>
.toast-host {
  z-index: 2;
  width: min(380px, 100%);
}

.toast-host__stack {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-fs-12);
}

.toast-host--main {
  position: sticky;
  top: var(--spacing-fs-16);
  align-self: flex-end;
  height: 0;
  margin-top: calc(var(--spacing-fs-12) * -1);
  margin-bottom: var(--spacing-fs-12);
}

.toast-host--viewport {
  position: fixed;
  top: var(--spacing-fs-16);
  right: var(--spacing-fs-16);
  z-index: 10;
  width: min(380px, calc(100% - 32px));
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
  transform: translateY(-8px);
}
</style>
