<script setup lang="ts">
import { onMounted, useId, useTemplateRef, watch } from 'vue'

import AppButton from './AppButton.vue'
import IconTile from './IconTile.vue'
import type { IconName } from './icons'

const props = withDefaults(
  defineProps<{
    open: boolean
    title: string
    describedBy?: string
    icon?: IconName
    dismissLabel: string
    confirmLabel: string
    confirmBusyLabel: string
    confirmIcon?: IconName
    busy?: boolean
  }>(),
  { describedBy: undefined, icon: 'x-circle', confirmIcon: 'x-circle', busy: false },
)

const emit = defineEmits<{
  dismiss: []
  confirm: []
}>()

defineSlots<{
  description?(): unknown
  default?(): unknown
  banner?(): unknown
}>()

const titleId = `${useId()}-title`

const dialog = useTemplateRef<HTMLDialogElement>('dialog')

const banner = useTemplateRef<HTMLElement>('banner')

const actions = useTemplateRef<HTMLElement>('actions')

const confirmButton = useTemplateRef<InstanceType<typeof AppButton>>('confirmButton')

function sync(open: boolean): void {
  const element = dialog.value
  if (element === null) {
    return
  }
  if (open && !element.open) {
    element.showModal()
  } else if (!open && element.open) {
    element.close()
  }
}

watch(() => props.open, sync, { flush: 'post' })

onMounted(() => {
  sync(props.open)
})

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape' && props.busy) {
    event.preventDefault()
  }
}

function onCancel(event: Event): void {
  if (props.busy) {
    event.preventDefault()
  }
}

function onClose(): void {
  if (!props.open) {
    return
  }
  if (props.busy) {
    dialog.value?.showModal()
    confirmButton.value?.focus()
    return
  }
  emit('dismiss')
}

function dismiss(): void {
  if (!props.busy) {
    emit('dismiss')
  }
}

function confirm(): void {
  if (!props.busy) {
    emit('confirm')
  }
}

function focusConfirm(): void {
  confirmButton.value?.focus()
}

function reveal(element: HTMLElement | null): void {
  if (element !== null && typeof element.scrollIntoView === 'function') {
    element.scrollIntoView({ block: 'nearest' })
  }
}

function revealBannerAndActions(): void {
  reveal(banner.value)
  reveal(actions.value)
}

defineExpose({ focusConfirm, revealBannerAndActions })
</script>

<template>
  <dialog
    ref="dialog"
    class="confirm-dialog"
    :aria-labelledby="titleId"
    :aria-describedby="describedBy"
    @keydown="onKeydown"
    @cancel="onCancel"
    @close="onClose"
  >
    <div class="confirm-dialog__head">
      <IconTile :icon="icon" :size="40" tone="fail" />
      <div class="confirm-dialog__titles">
        <h2 :id="titleId" class="confirm-dialog__title">{{ title }}</h2>
        <slot name="description" />
      </div>
    </div>
    <slot />
    <div v-if="$slots.banner" ref="banner" class="confirm-dialog__banner">
      <slot name="banner" />
    </div>
    <div ref="actions" class="confirm-dialog__actions">
      <AppButton variant="secondary" @click="dismiss">{{ dismissLabel }}</AppButton>
      <AppButton
        ref="confirmButton"
        variant="danger"
        :icon="confirmIcon"
        :busy="busy"
        :busy-label="confirmBusyLabel"
        @click="confirm"
        >{{ confirmLabel }}</AppButton
      >
    </div>
  </dialog>
</template>

<style scoped>
.confirm-dialog {
  flex-direction: column;
  gap: var(--spacing-fs-20);
  width: min(440px, calc(100% - 32px));
  max-width: none;
  max-height: calc(100dvh - 32px);
  padding: var(--spacing-fs-24);
  overflow-y: auto;
  border: 1px solid var(--fs-glass-sheet-border);
  border-radius: var(--radius-fs-card);
  background: var(--fs-glass-sheet-bg);
  box-shadow: var(--shadow-fs-dialog);
  color: var(--color-fs-ink);
  container-type: inline-size;
}

.confirm-dialog[open] {
  display: flex;
}

.confirm-dialog::backdrop {
  background: var(--color-fs-overlay);
}

.confirm-dialog__head {
  display: flex;
  align-items: flex-start;
  gap: var(--spacing-fs-14);
}

.confirm-dialog__titles {
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: var(--spacing-fs-6);
  min-width: 0;
}

.confirm-dialog__title {
  margin: 0;
  color: var(--color-fs-ink);
  font-size: 1.25rem;
  font-weight: 600;
  line-height: 1.3;
  letter-spacing: -0.015em;
}

.confirm-dialog__banner {
  display: flex;
  flex-direction: column;
}

.confirm-dialog__actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: var(--spacing-fs-12);
}

@container (width < 350px) {
  .confirm-dialog__actions {
    flex-direction: column;
    align-items: stretch;
  }
}

@media (forced-colors: active) {
  .confirm-dialog {
    border-color: CanvasText;
  }
}
</style>
