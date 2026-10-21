<script setup lang="ts">
import { computed, ref, useId, useTemplateRef } from 'vue'

import AppIcon from './AppIcon.vue'
import type { IconName } from './icons'

type TextFieldType = 'text' | 'email' | 'tel' | 'password' | 'search' | 'date' | 'time'

const props = withDefaults(
  defineProps<{
    label: string
    type?: TextFieldType
    id?: string
    name?: string
    icon?: IconName
    error?: string | null
    help?: string | null
    optional?: boolean
    required?: boolean
    disabled?: boolean
    placeholder?: string
    autocomplete?: string
    maxlength?: number
    pattern?: string
    trailingIcon?: IconName
  }>(),
  {
    type: 'text',
    id: undefined,
    name: undefined,
    icon: undefined,
    error: null,
    help: null,
    optional: false,
    required: false,
    disabled: false,
    placeholder: undefined,
    autocomplete: undefined,
    maxlength: undefined,
    pattern: undefined,
    trailingIcon: undefined,
  },
)

const model = defineModel<string>({ default: '' })

const generatedId = useId()

const inputId = computed(() => props.id ?? `${generatedId}-input`)

const errorId = computed(() => `${inputId.value}-error`)

const helpId = computed(() => `${inputId.value}-help`)

const describedBy = computed(() => {
  const ids = [props.error ? errorId.value : null, props.help ? helpId.value : null]
  const present = ids.filter((value): value is string => value !== null)
  return present.length > 0 ? present.join(' ') : undefined
})

const isPassword = computed(() => props.type === 'password')

const isSearch = computed(() => props.type === 'search')

const isCredential = computed(() => props.type === 'email' || props.type === 'password')

const passwordShown = ref(false)

const inputType = computed(() => (isPassword.value && passwordShown.value ? 'text' : props.type))

const leadingIcon = computed<IconName | undefined>(
  () => props.icon ?? (isSearch.value ? 'search' : undefined),
)

const isPicker = computed(() => props.type === 'date' || props.type === 'time')

const usesTabularNumbers = computed(() => isPicker.value || props.type === 'tel')

const input = useTemplateRef<HTMLInputElement>('input')

function focus(): void {
  input.value?.focus()
}

function validity(): ValidityState | null {
  return input.value?.validity ?? null
}

defineExpose({ focus, validity })
</script>

<template>
  <div class="text-field">
    <label v-if="!isSearch" :for="inputId" class="text-field__label">
      {{ label }}<span v-if="optional" class="text-field__optional">{{ ' Optional' }}</span>
    </label>
    <div class="text-field__control">
      <AppIcon v-if="leadingIcon" class="text-field__icon" :name="leadingIcon" :size="18" />
      <input
        :id="inputId"
        ref="input"
        v-model="model"
        :class="[
          'text-field__input',
          {
            'text-field__input--with-icon': leadingIcon !== undefined,
            'text-field__input--with-toggle': isPassword,
            'text-field__input--with-trailing': trailingIcon !== undefined,
            'text-field__input--picker': isPicker,
            'text-field__input--tabular': usesTabularNumbers,
          },
        ]"
        :type="inputType"
        :name="name"
        :required="required"
        :disabled="disabled"
        :placeholder="isSearch ? (placeholder ?? label) : placeholder"
        :autocomplete="autocomplete"
        :maxlength="maxlength"
        :pattern="pattern"
        :aria-label="isSearch ? label : undefined"
        :aria-invalid="error ? 'true' : undefined"
        :aria-describedby="describedBy"
        :spellcheck.attr="isCredential ? 'false' : undefined"
        :autocapitalize.attr="isCredential ? 'off' : undefined"
        :autocorrect.attr="isCredential ? 'off' : undefined"
      />
      <AppIcon v-if="trailingIcon" class="text-field__trailing" :name="trailingIcon" :size="16" />
      <button
        v-if="isPassword"
        type="button"
        class="text-field__toggle"
        aria-label="Show password"
        :aria-pressed="passwordShown ? 'true' : 'false'"
        :disabled="disabled"
        @click="passwordShown = !passwordShown"
      >
        <AppIcon name="eye" :size="18" />
      </button>
    </div>
    <p v-if="error" :id="errorId" class="text-field__error">
      <AppIcon name="alert-triangle" :size="14" />{{ error }}
    </p>
    <p v-if="help" :id="helpId" class="text-field__help">{{ help }}</p>
  </div>
</template>

<style scoped>
.text-field {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-fs-8);
  min-width: 0;
}

.text-field__label {
  display: flex;
  align-items: baseline;
  gap: var(--spacing-fs-8);
  font-size: var(--text-fs-label);
  font-weight: var(--text-fs-label--font-weight);
  line-height: var(--text-fs-label--line-height);
  color: var(--color-fs-ink-2);
}

.text-field__optional {
  font-weight: 500;
  color: var(--color-fs-muted);
}

.text-field__control {
  position: relative;
  display: flex;
}

.text-field__icon {
  position: absolute;
  top: 11px;
  left: 13px;
  color: var(--color-fs-muted);
  pointer-events: none;
}

.text-field__input {
  flex: 1;
  width: 100%;
  min-width: 0;
  height: 2.5rem;
  padding: 0 14px;
  border: 1px solid var(--color-fs-line-input);
  border-radius: var(--radius-fs-control);
  background: var(--color-fs-surface);
  box-shadow: var(--shadow-fs-input);
  color: var(--color-fs-ink);
  font-family: inherit;
  font-size: 0.875rem;
  transition:
    border-color var(--fs-duration-hover),
    box-shadow var(--fs-duration-hover);
}

.text-field__input[type='search'] {
  -webkit-appearance: none;
  appearance: none;
}

.text-field__input--with-icon {
  padding-left: 40px;
}

.text-field__input--with-toggle {
  padding-right: 44px;
}

.text-field__input--with-trailing {
  padding-right: 40px;
}

.text-field__input--tabular {
  font-variant-numeric: tabular-nums;
}

.text-field__trailing {
  display: none;
  position: absolute;
  top: 12px;
  right: 14px;
  color: var(--color-fs-muted);
  pointer-events: none;
}

@supports selector(::-webkit-calendar-picker-indicator) {
  .text-field__trailing {
    display: block;
  }

  .text-field__input--picker::-webkit-calendar-picker-indicator {
    position: absolute;
    top: 0;
    right: 0;
    width: 40px;
    height: 100%;
    margin: 0;
    padding: 0;
    opacity: 0;
    cursor: pointer;
  }
}

.text-field__input[aria-invalid='true']:not(:focus-within) {
  border-color: var(--color-fs-danger);
  box-shadow: var(--shadow-fs-input-error);
}

.text-field__input:focus-within {
  border-color: var(--color-fs-teal-700);
  outline: 2px solid transparent;
  outline-offset: 2px;
  box-shadow: var(--shadow-fs-input-focus);
}

.text-field__input--picker:focus-within:not(:focus) + .text-field__trailing {
  border-radius: 4px;
  outline: 2px solid var(--color-fs-focus);
  outline-offset: 2px;
  color: var(--color-fs-teal-700);
}

.text-field__input:disabled {
  background: var(--color-fs-disabled);
  color: var(--color-fs-disabled-text);
  cursor: not-allowed;
}

.text-field__toggle {
  position: absolute;
  top: 2px;
  right: 2px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 36px;
  height: 36px;
  padding: 0;
  border: 0;
  border-radius: var(--radius-fs-control-sm);
  background: transparent;
  color: var(--color-fs-ink-2);
  font-family: inherit;
  cursor: pointer;
}

.text-field__toggle:disabled {
  color: var(--color-fs-disabled-text);
  cursor: not-allowed;
}

.text-field__error {
  display: flex;
  align-items: center;
  gap: var(--spacing-fs-6);
  margin: 0;
  font-size: var(--text-fs-meta);
  font-weight: 500;
  line-height: var(--text-fs-meta--line-height);
  color: var(--color-fs-danger);
}

.text-field__help {
  margin: 0;
  font-size: var(--text-fs-meta);
  line-height: var(--text-fs-meta--line-height);
  color: var(--color-fs-muted);
  text-wrap: pretty;
}

@media (forced-colors: active) {
  .text-field__input:focus-within {
    outline-color: Highlight;
  }

  .text-field__trailing {
    color: CanvasText;
  }

  .text-field__input--picker:focus-within:not(:focus) + .text-field__trailing {
    outline-color: Highlight;
  }
}
</style>
