<script setup lang="ts">
import { computed } from 'vue'

import { jobPriorityLabel } from '@/shared/format/labels'

import AppIcon from './AppIcon.vue'
import type { IconName } from './icons'

const props = withDefaults(
  defineProps<{
    priority: string
    onDark?: boolean
  }>(),
  { onDark: false },
)

const glyphs: Readonly<Record<string, IconName>> = {
  Low: 'chevron-down',
  Normal: 'minus',
  High: 'chevron-up',
}

const isUrgent = computed(() => props.priority === 'Urgent')

const glyph = computed<IconName | null>(() => glyphs[props.priority] ?? null)

const tone = computed(() =>
  Object.prototype.hasOwnProperty.call(glyphs, props.priority)
    ? props.priority.toLowerCase()
    : 'unknown',
)
</script>

<template>
  <span v-if="isUrgent" :class="['priority-label', 'priority-label--urgent']"
    ><AppIcon name="alert-triangle" :size="14" :stroke-width="2.5" />{{
      jobPriorityLabel(priority)
    }}</span
  >
  <span
    v-else
    :class="['priority-label', `priority-label--${tone}`, { 'priority-label--on-dark': onDark }]"
    ><AppIcon v-if="glyph" :name="glyph" :size="16" />{{ jobPriorityLabel(priority) }}</span
  >
</template>

<style scoped>
.priority-label {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  color: var(--color-fs-muted);
  font-size: var(--text-fs-table);
  font-weight: 500;
  line-height: 1;
  white-space: nowrap;
}

.priority-label--normal {
  color: var(--color-fs-ink-2);
}

.priority-label--high {
  color: var(--color-fs-high);
  font-weight: 600;
}

.priority-label--on-dark {
  gap: var(--spacing-fs-6);
  height: 2rem;
  padding: 0 var(--spacing-fs-12) 0 var(--spacing-fs-10);
  border-radius: var(--radius-fs-pill);
  background: var(--fs-glass-g4-control-bg);
  box-shadow: var(--shadow-fs-g4-pill);
  color: var(--color-fs-on-dark);
  font-weight: 600;
}

.priority-label--urgent {
  height: 1.5rem;
  padding: 0 var(--spacing-fs-10) 0 var(--spacing-fs-8);
  border-radius: var(--radius-fs-pill);
  background: var(--fs-gradient-accent);
  box-shadow: var(--shadow-fs-accent);
  color: var(--color-fs-ink);
  font-size: 0.75rem;
  font-weight: 700;
  letter-spacing: 0.05em;
  text-transform: uppercase;
}

@media (forced-colors: active) {
  .priority-label--urgent,
  .priority-label--on-dark {
    border: 1px solid CanvasText;
  }
}
</style>
