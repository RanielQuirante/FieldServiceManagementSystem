<script setup lang="ts">
import { computed } from 'vue'

import AppIcon from './AppIcon.vue'
import { jobStatusAppearance, type StatusAppearance } from './status-appearance'

const props = defineProps<{
  status: string
}>()

const appearance = computed<StatusAppearance>(() => jobStatusAppearance(props.status))
</script>

<template>
  <span :class="['status-chip', `status-chip--${appearance.tone}`]">
    <AppIcon :name="appearance.icon" :size="14" />{{ appearance.label }}
  </span>
</template>

<style scoped>
.status-chip {
  display: inline-flex;
  align-items: center;
  gap: var(--spacing-fs-6);
  height: 1.625rem;
  padding: 0 var(--spacing-fs-10) 0 var(--spacing-fs-8);
  border-radius: var(--radius-fs-pill);
  background: var(--chip-bg);
  box-shadow: var(--chip-shadow);
  color: var(--chip-fg);
  font-size: 0.8125rem;
  font-weight: 600;
  line-height: 1;
  white-space: nowrap;
}

.status-chip--unassigned {
  --chip-bg: var(--fs-gradient-chip-unassigned);
  --chip-shadow: var(--shadow-fs-chip-unassigned);
  --chip-fg: var(--color-fs-unassigned);

  border: 1px dashed var(--color-fs-line-input);
}

.status-chip--assigned {
  --chip-bg: var(--fs-gradient-chip-assigned);
  --chip-shadow: var(--shadow-fs-chip-assigned);
  --chip-fg: var(--color-fs-assigned);
}

.status-chip--in-progress {
  --chip-bg: var(--fs-gradient-chip-in-progress);
  --chip-shadow: var(--shadow-fs-chip-in-progress);
  --chip-fg: var(--color-fs-in-progress);
}

.status-chip--on-hold {
  --chip-bg: var(--fs-gradient-chip-on-hold);
  --chip-shadow: var(--shadow-fs-chip-on-hold);
  --chip-fg: var(--color-fs-on-hold);
}

.status-chip--completed {
  --chip-bg: var(--fs-gradient-chip-completed);
  --chip-shadow: var(--shadow-fs-chip-completed);
  --chip-fg: var(--color-fs-completed);
}

.status-chip--cancelled,
.status-chip--unknown {
  --chip-bg: var(--fs-gradient-chip-cancelled);
  --chip-shadow: var(--shadow-fs-chip-cancelled);
  --chip-fg: var(--color-fs-cancelled);
}

@media (forced-colors: active) {
  .status-chip {
    border: 1px solid CanvasText;
  }
}
</style>
