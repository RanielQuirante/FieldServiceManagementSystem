<script setup lang="ts">
import AppIcon from './AppIcon.vue'
import type { IconName, IconSize } from './icons'

const props = withDefaults(
  defineProps<{
    label: string
    icon: IconName
    iconSize?: IconSize
    variant?: 'light' | 'on-dark'
    disabled?: boolean
    busy?: boolean
  }>(),
  { iconSize: 18, variant: 'light', disabled: false, busy: false },
)

function blockWhileBusy(event: MouseEvent): void {
  if (props.busy) {
    event.preventDefault()
    event.stopImmediatePropagation()
  }
}
</script>

<template>
  <button
    type="button"
    :class="['icon-button', `icon-button--${variant}`]"
    :aria-label="label"
    :disabled="disabled"
    :aria-disabled="busy ? 'true' : undefined"
    @click="blockWhileBusy"
  >
    <AppIcon :name="icon" :size="iconSize" />
  </button>
</template>

<style scoped>
.icon-button {
  flex: none;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 2.25rem;
  height: 2.25rem;
  padding: 0;
  border: 1px solid transparent;
  border-radius: var(--radius-fs-control-sm);
  background: transparent;
  color: var(--color-fs-muted);
  font-family: inherit;
  cursor: pointer;
  transition: background-color var(--fs-duration-hover);
}

.icon-button--light:hover {
  background: var(--color-fs-quiet-hover);
  color: var(--color-fs-teal-700);
}

.icon-button--on-dark {
  color: var(--color-fs-on-dark-2);
}

.icon-button--on-dark:hover {
  background: var(--fs-glass-g4-control-bg);
}

.icon-button:disabled,
.icon-button[aria-disabled='true'] {
  background: transparent;
  color: var(--color-fs-disabled-text);
  cursor: not-allowed;
}

@media (forced-colors: active) {
  .icon-button {
    border-color: ButtonText;
  }

  .icon-button:disabled,
  .icon-button[aria-disabled='true'] {
    border-color: GrayText;
    color: GrayText;
  }
}
</style>
