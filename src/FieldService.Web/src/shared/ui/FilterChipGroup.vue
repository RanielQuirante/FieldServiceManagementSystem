<script lang="ts">
import type { IconName } from './icons'
import type { StatusTone } from './status-appearance'

export interface FilterChipOption {
  readonly value: string
  readonly label: string
  readonly icon?: IconName
  readonly tone?: StatusTone
  readonly count?: number | null
}
</script>

<script setup lang="ts">
import AppIcon from './AppIcon.vue'
import CountCapsule from './CountCapsule.vue'

defineProps<{
  label: string
  options: readonly FilterChipOption[]
}>()

const model = defineModel<string>({ required: true })

function choose(value: string): void {
  if (model.value !== value) {
    model.value = value
  }
}
</script>

<template>
  <div class="filter-chip-group" role="group" :aria-label="label">
    <button
      v-for="option in options"
      :key="option.value"
      type="button"
      :class="[
        'filter-chip',
        model === option.value ? 'filter-chip--selected' : 'filter-chip--unselected',
      ]"
      :aria-pressed="model === option.value ? 'true' : 'false'"
      @click="choose(option.value)"
    >
      <AppIcon
        v-if="option.icon"
        :class="['filter-chip__icon', `filter-chip__icon--${option.tone ?? 'unknown'}`]"
        :name="option.icon"
        :size="14"
      />
      <span>{{ option.label }}</span>
      <template v-if="option.count !== undefined && option.count !== null">
        <span class="visually-hidden">{{ ', ' }}</span>
        <CountCapsule
          :count="option.count"
          :variant="model === option.value ? 'on-gradient' : 'default'"
        />
      </template>
    </button>
  </div>
</template>

<style scoped>
.filter-chip-group {
  display: flex;
  flex-wrap: wrap;
  gap: var(--spacing-fs-8);
}

.filter-chip {
  display: inline-flex;
  align-items: center;
  gap: var(--spacing-fs-8);
  height: 2.25rem;
  padding: 0 var(--spacing-fs-6) 0 var(--spacing-fs-10);
  border: 1px solid transparent;
  border-radius: var(--radius-fs-pill);
  font-family: inherit;
  font-size: var(--text-fs-table);
  white-space: nowrap;
  cursor: pointer;
  transition:
    background-color var(--fs-duration-hover),
    box-shadow var(--fs-duration-hover);
}

.filter-chip--unselected {
  border-color: var(--color-fs-chip-border);
  background: var(--color-fs-chip-bg);
  box-shadow: var(--shadow-fs-e1);
  color: var(--color-fs-ink-2);
  font-weight: 500;
}

.filter-chip--unselected:hover {
  background: var(--color-fs-secondary-hover);
}

.filter-chip--selected {
  padding-left: var(--spacing-fs-14);
  background: var(--fs-gloss), var(--fs-gradient-primary);
  box-shadow: var(--shadow-fs-primary);
  color: var(--color-fs-on-dark);
  font-weight: 600;
}

.filter-chip--selected:has(.filter-chip__icon) {
  padding-left: var(--spacing-fs-10);
}

.filter-chip--unselected .filter-chip__icon--unassigned {
  color: var(--color-fs-unassigned);
}

.filter-chip--unselected .filter-chip__icon--assigned {
  color: var(--color-fs-assigned);
}

.filter-chip--unselected .filter-chip__icon--in-progress {
  color: var(--color-fs-in-progress);
}

.filter-chip--unselected .filter-chip__icon--on-hold {
  color: var(--color-fs-on-hold);
}

.filter-chip--unselected .filter-chip__icon--completed {
  color: var(--color-fs-completed);
}

.filter-chip--unselected .filter-chip__icon--cancelled,
.filter-chip--unselected .filter-chip__icon--unknown {
  color: var(--color-fs-cancelled);
}

@media (forced-colors: active) {
  .filter-chip {
    border-color: ButtonText;
  }

  .filter-chip--selected {
    border-color: Highlight;
    border-width: 2px;
  }
}
</style>
