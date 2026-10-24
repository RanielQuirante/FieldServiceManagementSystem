<script setup lang="ts">
import { computed, useTemplateRef } from 'vue'
import { RouterLink, type RouteLocationRaw } from 'vue-router'

import AppIcon from './AppIcon.vue'
import type { IconName } from './icons'

type ButtonVariant =
  'primary' | 'secondary' | 'quiet' | 'danger' | 'primary-on-dark' | 'secondary-on-dark'

type ButtonSize = 'regular' | 'small' | 'large'

const props = withDefaults(
  defineProps<{
    variant?: ButtonVariant
    size?: ButtonSize
    type?: 'button' | 'submit'
    to?: RouteLocationRaw
    icon?: IconName
    trailingIcon?: IconName
    disabled?: boolean
    ariaDisabled?: boolean
    busy?: boolean
    busyLabel?: string
    block?: boolean
  }>(),
  {
    variant: 'primary',
    size: 'regular',
    type: 'button',
    to: undefined,
    icon: undefined,
    trailingIcon: undefined,
    disabled: false,
    ariaDisabled: false,
    busy: false,
    busyLabel: undefined,
    block: false,
  },
)

const classes = computed(() => [
  'app-button',
  `app-button--${props.variant}`,
  `app-button--${props.size}`,
  { 'app-button--block': props.block, 'app-button--busy': props.busy && !props.disabled },
])

const iconSize = computed(() => (props.size === 'small' ? 16 : 18))

const showsBusyLabel = computed(() => props.busy && props.busyLabel !== undefined)

const linkInactive = computed(() => props.disabled || props.busy || props.ariaDisabled)

const announcedDisabled = computed(() => props.busy || props.ariaDisabled)

function blockWhileBusy(event: MouseEvent): void {
  if (announcedDisabled.value) {
    event.preventDefault()
    event.stopImmediatePropagation()
  }
}

function followLink(event: MouseEvent, navigate: (event?: MouseEvent) => unknown): void {
  if (linkInactive.value) {
    event.preventDefault()
    event.stopImmediatePropagation()
    return
  }
  void navigate(event)
}

const control = useTemplateRef<HTMLElement>('control')

function focus(): void {
  control.value?.focus()
}

defineExpose({ focus })
</script>

<template>
  <RouterLink v-if="to !== undefined" v-slot="{ href, navigate }" :to="to" custom>
    <a
      ref="control"
      :href="href"
      :class="classes"
      :aria-disabled="linkInactive ? 'true' : undefined"
      @click="(event: MouseEvent) => followLink(event, navigate)"
    >
      <AppIcon v-if="icon" :name="icon" :size="iconSize" />
      <span v-if="showsBusyLabel">{{ busyLabel }}</span>
      <slot v-else />
      <AppIcon v-if="trailingIcon" :name="trailingIcon" :size="16" />
    </a>
  </RouterLink>
  <button
    v-else
    ref="control"
    :type="type"
    :class="classes"
    :disabled="disabled"
    :aria-disabled="announcedDisabled ? 'true' : undefined"
    @click="blockWhileBusy"
  >
    <AppIcon v-if="icon" :name="icon" :size="iconSize" />
    <span v-if="showsBusyLabel">{{ busyLabel }}</span>
    <slot v-else />
    <AppIcon v-if="trailingIcon" :name="trailingIcon" :size="16" />
  </button>
</template>

<style scoped>
.app-button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: var(--spacing-fs-8);
  min-height: 2.5rem;
  padding: 0 var(--spacing-fs-16);
  border: 1px solid transparent;
  border-radius: var(--radius-fs-control);
  font-family: inherit;
  font-size: var(--text-fs-button);
  font-weight: var(--text-fs-button--font-weight);
  line-height: var(--text-fs-button--line-height);
  letter-spacing: var(--text-fs-button--letter-spacing);
  white-space: nowrap;
  text-decoration: none;
  cursor: pointer;
  transition:
    background-color var(--fs-duration-hover),
    box-shadow var(--fs-duration-hover),
    color var(--fs-duration-hover);
}

.app-button--small {
  gap: var(--spacing-fs-6);
  min-height: 2rem;
  padding: 0 var(--spacing-fs-12);
  border-radius: var(--radius-fs-control-sm);
  font-size: var(--text-fs-button-sm);
  font-weight: var(--text-fs-button-sm--font-weight);
}

.app-button--large {
  min-height: 2.75rem;
}

.app-button--block {
  display: flex;
  width: 100%;
}

.app-button--primary {
  background: var(--fs-gloss), var(--fs-gradient-primary);
  box-shadow: var(--shadow-fs-primary);
  color: var(--color-fs-on-dark);
}

.app-button--primary:active:not(.app-button--busy) {
  background: var(--fs-gloss), var(--fs-gradient-primary-pressed);
}

.app-button--secondary,
.app-button--danger {
  border-color: var(--color-fs-secondary-border);
  background: var(--color-fs-secondary-bg);
  -webkit-backdrop-filter: blur(var(--blur-fs-dark));
  backdrop-filter: blur(var(--blur-fs-dark));
  box-shadow: var(--shadow-fs-secondary);
  color: var(--color-fs-ink);
}

.app-button--danger {
  border-color: var(--color-fs-danger-border);
  color: var(--color-fs-danger);
}

.app-button--secondary .app-icon {
  color: var(--color-fs-teal-700);
}

.app-button--secondary:hover:not(.app-button--busy),
.app-button--danger:hover:not(.app-button--busy) {
  background: var(--color-fs-secondary-hover);
}

.app-button--quiet {
  min-height: 2.25rem;
  padding: 0 var(--spacing-fs-10);
  border-radius: var(--radius-fs-control-sm);
  background: transparent;
  color: var(--color-fs-teal-700);
}

.app-button--quiet:hover:not(.app-button--busy) {
  background: var(--color-fs-quiet-hover);
}

.app-button--primary-on-dark {
  background: var(--fs-gradient-primary-on-dark);
  box-shadow: var(--shadow-fs-primary-on-dark);
  color: var(--color-fs-ink);
}

.app-button--secondary-on-dark {
  background: var(--fs-glass-g4-control-bg);
  -webkit-backdrop-filter: blur(var(--blur-fs-dark));
  backdrop-filter: blur(var(--blur-fs-dark));
  box-shadow: var(--shadow-fs-g4-secondary);
  color: var(--color-fs-on-dark);
}

.app-button:disabled,
.app-button[aria-disabled='true']:not(.app-button--busy) {
  border-color: transparent;
  background: var(--color-fs-disabled);
  -webkit-backdrop-filter: none;
  backdrop-filter: none;
  box-shadow: none;
  color: var(--color-fs-disabled-text);
  cursor: not-allowed;
}

.app-button:disabled .app-icon,
.app-button[aria-disabled='true']:not(.app-button--busy) .app-icon {
  color: currentColor;
}

.app-button--busy {
  cursor: progress;
}

@media (forced-colors: active) {
  .app-button {
    border-color: ButtonText;
  }

  .app-button:disabled,
  .app-button[aria-disabled='true']:not(.app-button--busy) {
    border-color: GrayText;
    color: GrayText;
  }
}
</style>
