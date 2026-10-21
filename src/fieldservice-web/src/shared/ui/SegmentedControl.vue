<script lang="ts">
import type { IconName } from './icons'

export interface SegmentOption {
  readonly value: string
  readonly label: string
  readonly icon?: IconName
  readonly iconTone?: 'muted' | 'ink-2' | 'high'
  readonly disabled?: boolean
}
</script>

<script setup lang="ts">
import { computed, useId, useTemplateRef } from 'vue'

import AppIcon from './AppIcon.vue'

const props = withDefaults(
  defineProps<{
    legend: string
    options: readonly SegmentOption[]
    name?: string
    id?: string
    error?: string | null
    disabled?: boolean
  }>(),
  { name: undefined, id: undefined, error: null, disabled: false },
)

const model = defineModel<string>({ default: '' })

const generatedId = useId()

const baseId = computed(() => props.id ?? `${generatedId}-segment`)

const groupName = computed(() => props.name ?? baseId.value)

const errorId = computed(() => `${baseId.value}-error`)

const group = useTemplateRef<HTMLFieldSetElement>('group')

function optionId(value: string): string {
  return `${baseId.value}-${value.toLowerCase()}`
}

function focus(): void {
  const target =
    group.value?.querySelector<HTMLInputElement>('input:checked') ??
    group.value?.querySelector<HTMLInputElement>('input')
  target?.focus()
}

defineExpose({ focus })
</script>

<template>
  <fieldset
    ref="group"
    class="segmented-control"
    :disabled="disabled"
    :aria-describedby="error ? errorId : undefined"
  >
    <legend class="segmented-control__legend">{{ legend }}</legend>
    <div class="segmented-control__track">
      <label
        v-for="option in options"
        :key="option.value"
        :for="optionId(option.value)"
        :class="[
          'segmented-control__segment',
          {
            'segmented-control__segment--selected': model === option.value,
            'segmented-control__segment--disabled': option.disabled,
          },
        ]"
      >
        <input
          :id="optionId(option.value)"
          v-model="model"
          class="segmented-control__input"
          type="radio"
          :name="groupName"
          :value="option.value"
          :disabled="option.disabled"
          :aria-invalid="error ? 'true' : undefined"
        />
        <AppIcon
          v-if="option.icon"
          :class="`segmented-control__icon--${option.iconTone ?? 'ink-2'}`"
          :name="option.icon"
          :size="16"
        />{{ option.label }}
      </label>
    </div>
    <p v-if="error" :id="errorId" class="segmented-control__error">
      <AppIcon name="alert-triangle" :size="14" />{{ error }}
    </p>
  </fieldset>
</template>

<style scoped>
.segmented-control {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-fs-8);
  min-width: 0;
  margin: 0;
  padding: 0;
  border: 0;
}

.segmented-control__legend {
  padding: 0 0 var(--spacing-fs-12);
  font-size: var(--text-fs-label);
  font-weight: var(--text-fs-label--font-weight);
  line-height: var(--text-fs-label--line-height);
  color: var(--color-fs-ink-2);
}

.segmented-control__track {
  display: flex;
  flex-wrap: wrap;
  gap: var(--spacing-fs-4);
  padding: var(--spacing-fs-4);
  border-radius: var(--radius-fs-well);
  background: var(--color-fs-track);
  box-shadow: var(--shadow-fs-track);
}

.segmented-control__segment {
  position: relative;
  display: inline-flex;
  flex: 1 1 6.5rem;
  align-items: center;
  justify-content: center;
  gap: var(--spacing-fs-6);
  min-height: 2.25rem;
  padding: 0 var(--spacing-fs-12);
  border: 1px solid transparent;
  border-radius: var(--radius-fs-control-sm);
  color: var(--color-fs-ink-2);
  font-size: var(--text-fs-table);
  font-weight: 500;
  cursor: pointer;
  transition:
    background-color var(--fs-duration-hover),
    box-shadow var(--fs-duration-hover);
}

.segmented-control__segment--selected {
  background: var(--color-fs-surface);
  box-shadow: var(--shadow-fs-segment);
  color: var(--color-fs-ink);
  font-weight: 600;
}

.segmented-control__segment:has(.segmented-control__input:focus-visible) {
  outline: 2px solid var(--color-fs-focus);
  outline-offset: 2px;
}

.segmented-control__segment--disabled {
  color: var(--color-fs-disabled-text);
  cursor: not-allowed;
}

.segmented-control__input {
  position: absolute;
  width: 1px;
  height: 1px;
  margin: 0;
  opacity: 0;
}

.segmented-control__icon--muted {
  color: var(--color-fs-muted);
}

.segmented-control__icon--ink-2 {
  color: var(--color-fs-ink-2);
}

.segmented-control__icon--high {
  color: var(--color-fs-high);
}

.segmented-control__error {
  display: flex;
  align-items: center;
  gap: var(--spacing-fs-6);
  margin: 0;
  font-size: var(--text-fs-meta);
  font-weight: 500;
  line-height: var(--text-fs-meta--line-height);
  color: var(--color-fs-danger);
}

@media (forced-colors: active) {
  .segmented-control__segment--selected {
    border-color: Highlight;
  }

  .segmented-control__segment:has(.segmented-control__input:focus-visible) {
    outline-color: Highlight;
  }
}
</style>
