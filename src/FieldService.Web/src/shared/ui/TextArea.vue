<script setup lang="ts">
import { computed, useId, useTemplateRef } from 'vue'

import AppIcon from './AppIcon.vue'

const props = withDefaults(
  defineProps<{
    label: string
    id?: string
    name?: string
    error?: string | null
    help?: string | null
    count?: string | null
    optional?: boolean
    required?: boolean
    ariaRequired?: boolean
    disabled?: boolean
    readonly?: boolean
    placeholder?: string
    maxlength?: number
    rows?: number
  }>(),
  {
    id: undefined,
    name: undefined,
    error: null,
    help: null,
    count: null,
    optional: false,
    required: false,
    ariaRequired: false,
    disabled: false,
    readonly: false,
    placeholder: undefined,
    maxlength: undefined,
    rows: 3,
  },
)

const model = defineModel<string>({ default: '' })

const generatedId = useId()

const areaId = computed(() => props.id ?? `${generatedId}-textarea`)

const errorId = computed(() => `${areaId.value}-error`)

const helpId = computed(() => `${areaId.value}-help`)

const countId = computed(() => `${areaId.value}-count`)

const describedBy = computed(() => {
  const ids = [
    props.error ? errorId.value : null,
    props.help ? helpId.value : null,
    props.count ? countId.value : null,
  ]
  const present = ids.filter((value): value is string => value !== null)
  return present.length > 0 ? present.join(' ') : undefined
})

const area = useTemplateRef<HTMLTextAreaElement>('area')

function focus(): void {
  area.value?.focus()
}

defineExpose({ focus })
</script>

<template>
  <div class="text-area">
    <label :for="areaId" class="text-area__label">
      {{ label }}<span v-if="optional" class="text-area__optional">{{ ' Optional' }}</span>
    </label>
    <textarea
      :id="areaId"
      ref="area"
      v-model="model"
      class="text-area__input"
      :name="name"
      :rows="rows"
      :required="required"
      :disabled="disabled"
      :readonly="readonly"
      :placeholder="placeholder"
      :maxlength="maxlength"
      :aria-required="ariaRequired ? 'true' : undefined"
      :aria-invalid="error ? 'true' : undefined"
      :aria-describedby="describedBy"
    ></textarea>
    <p v-if="error" :id="errorId" class="text-area__error">
      <AppIcon name="alert-triangle" :size="14" />{{ error }}
    </p>
    <p v-if="help" :id="helpId" class="text-area__help">{{ help }}</p>
    <p v-if="count" :id="countId" class="text-area__count">{{ count }}</p>
  </div>
</template>

<style scoped>
.text-area {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-fs-8);
  min-width: 0;
}

.text-area__label {
  display: flex;
  align-items: baseline;
  gap: var(--spacing-fs-8);
  font-size: var(--text-fs-label);
  font-weight: var(--text-fs-label--font-weight);
  line-height: var(--text-fs-label--line-height);
  color: var(--color-fs-ink-2);
}

.text-area__optional {
  font-weight: 500;
  color: var(--color-fs-muted);
}

.text-area__input {
  display: block;
  width: 100%;
  min-width: 0;
  min-height: 6rem;
  padding: 10px 14px;
  border: 1px solid var(--color-fs-line-input);
  border-radius: var(--radius-fs-control);
  background: var(--color-fs-surface);
  box-shadow: var(--shadow-fs-input);
  color: var(--color-fs-ink);
  font-family: inherit;
  font-size: 0.875rem;
  line-height: 1.5;
  resize: vertical;
  transition:
    border-color var(--fs-duration-hover),
    box-shadow var(--fs-duration-hover);
}

.text-area__input[aria-invalid='true']:not(:focus) {
  border-color: var(--color-fs-danger);
  box-shadow: var(--shadow-fs-input-error);
}

.text-area__input:focus {
  border-color: var(--color-fs-teal-700);
  outline: 2px solid transparent;
  outline-offset: 2px;
  box-shadow: var(--shadow-fs-input-focus);
}

.text-area__input:disabled {
  background: var(--color-fs-disabled);
  color: var(--color-fs-disabled-text);
  cursor: not-allowed;
}

.text-area__error {
  display: flex;
  align-items: center;
  gap: var(--spacing-fs-6);
  margin: 0;
  font-size: var(--text-fs-meta);
  font-weight: 500;
  line-height: var(--text-fs-meta--line-height);
  color: var(--color-fs-danger);
}

.text-area__help,
.text-area__count {
  margin: 0;
  font-size: var(--text-fs-meta);
  line-height: var(--text-fs-meta--line-height);
  color: var(--color-fs-muted);
  text-wrap: pretty;
}

.text-area__count {
  font-variant-numeric: tabular-nums;
}

@media (forced-colors: active) {
  .text-area__input:focus {
    outline-color: Highlight;
  }
}
</style>
