<script lang="ts">
export interface SelectOption {
  readonly value: string
  readonly label: string
  readonly disabled?: boolean
}

export interface SelectOptionGroup {
  readonly label: string
  readonly options: readonly SelectOption[]
}

export type SelectEntry = SelectOption | SelectOptionGroup

export function isOptionGroup(entry: SelectEntry): entry is SelectOptionGroup {
  return 'options' in entry
}
</script>

<script setup lang="ts">
import { computed, useId, useTemplateRef } from 'vue'

import AppIcon from './AppIcon.vue'

const props = withDefaults(
  defineProps<{
    label: string
    options: readonly SelectEntry[]
    id?: string
    name?: string
    placeholder?: string | null
    hiddenLabel?: boolean
    error?: string | null
    help?: string | null
    required?: boolean
    disabled?: boolean
  }>(),
  {
    id: undefined,
    name: undefined,
    placeholder: null,
    hiddenLabel: false,
    error: null,
    help: null,
    required: false,
    disabled: false,
  },
)

const model = defineModel<string>({ default: '' })

const generatedId = useId()

const selectId = computed(() => props.id ?? `${generatedId}-select`)

const errorId = computed(() => `${selectId.value}-error`)

const helpId = computed(() => `${selectId.value}-help`)

const describedBy = computed(() => {
  const ids = [props.error ? errorId.value : null, props.help ? helpId.value : null]
  const present = ids.filter((value): value is string => value !== null)
  return present.length > 0 ? present.join(' ') : undefined
})

const select = useTemplateRef<HTMLSelectElement>('select')

function focus(): void {
  select.value?.focus()
}

defineExpose({ focus })
</script>

<template>
  <div class="select-field">
    <label v-if="!hiddenLabel" :for="selectId" class="select-field__label">{{ label }}</label>
    <div class="select-field__control">
      <select
        :id="selectId"
        ref="select"
        v-model="model"
        class="select-field__select"
        :name="name"
        :required="required"
        :disabled="disabled"
        :aria-label="hiddenLabel ? label : undefined"
        :aria-invalid="error ? 'true' : undefined"
        :aria-describedby="describedBy"
      >
        <option v-if="placeholder !== null" value="" disabled>{{ placeholder }}</option>
        <template
          v-for="entry in options"
          :key="isOptionGroup(entry) ? `group:${entry.label}` : entry.value"
        >
          <optgroup v-if="isOptionGroup(entry)" :label="entry.label">
            <option
              v-for="option in entry.options"
              :key="option.value"
              :value="option.value"
              :disabled="option.disabled"
            >
              {{ option.label }}
            </option>
          </optgroup>
          <option v-else :value="entry.value" :disabled="entry.disabled">
            {{ entry.label }}
          </option>
        </template>
      </select>
      <AppIcon class="select-field__chevron" name="chevron-down" :size="16" />
    </div>
    <p v-if="error" :id="errorId" class="select-field__error">
      <AppIcon name="alert-triangle" :size="14" />{{ error }}
    </p>
    <p v-if="help" :id="helpId" class="select-field__help">{{ help }}</p>
  </div>
</template>

<style scoped>
.select-field {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-fs-8);
  min-width: 0;
}

.select-field__label {
  font-size: var(--text-fs-label);
  font-weight: var(--text-fs-label--font-weight);
  line-height: var(--text-fs-label--line-height);
  color: var(--color-fs-ink-2);
}

.select-field__control {
  position: relative;
  display: flex;
  min-width: 0;
}

.select-field__select {
  flex: 1;
  width: 100%;
  min-width: 0;
  height: 2.5rem;
  padding: 0 40px 0 14px;
  border: 1px solid var(--color-fs-line-input);
  border-radius: var(--radius-fs-control);
  background: var(--color-fs-surface);
  box-shadow: var(--shadow-fs-input);
  color: var(--color-fs-ink);
  font-family: inherit;
  font-size: 0.875rem;
  text-overflow: ellipsis;
  -webkit-appearance: none;
  appearance: none;
  cursor: pointer;
  transition:
    border-color var(--fs-duration-hover),
    box-shadow var(--fs-duration-hover);
}

.select-field__select[aria-invalid='true']:not(:focus) {
  border-color: var(--color-fs-danger);
  box-shadow: var(--shadow-fs-input-error);
}

.select-field__select:focus {
  border-color: var(--color-fs-teal-700);
  outline: 2px solid transparent;
  outline-offset: 2px;
  box-shadow: var(--shadow-fs-input-focus);
}

.select-field__select:disabled {
  background: var(--color-fs-disabled);
  color: var(--color-fs-disabled-text);
  cursor: not-allowed;
}

.select-field__chevron {
  position: absolute;
  top: 12px;
  right: 14px;
  color: var(--color-fs-muted);
  pointer-events: none;
}

.select-field__error {
  display: flex;
  align-items: center;
  gap: var(--spacing-fs-6);
  margin: 0;
  font-size: var(--text-fs-meta);
  font-weight: 500;
  line-height: var(--text-fs-meta--line-height);
  color: var(--color-fs-danger);
}

.select-field__help {
  margin: 0;
  font-size: var(--text-fs-meta);
  line-height: var(--text-fs-meta--line-height);
  color: var(--color-fs-muted);
  text-wrap: pretty;
}

@media (forced-colors: active) {
  .select-field__select:focus {
    outline-color: Highlight;
  }

  .select-field__chevron {
    color: CanvasText;
  }
}
</style>
