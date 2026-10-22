<script setup lang="ts">
import { computed } from 'vue'

import AppIcon from './AppIcon.vue'
import { jobStatusAppearance, type StatusAppearance } from './status-appearance'

const props = withDefaults(
  defineProps<{
    status?: string
    shift?: 'on' | 'off' | null
  }>(),
  { status: '', shift: null },
)

const appearance = computed<StatusAppearance>(() => jobStatusAppearance(props.status))

const shiftLabel = computed(() => (props.shift === 'on' ? 'On shift' : 'Off shift'))
</script>

<template>
  <span v-if="shift" :class="['status-chip', 'status-chip--shift', `status-chip--${shift}-shift`]">
    <span class="status-chip__dot" aria-hidden="true"></span>{{ shiftLabel }}
  </span>
  <span v-else :class="['status-chip', `status-chip--${appearance.tone}`]">
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

.status-chip--shift {
  gap: var(--spacing-fs-8);
  padding: 0 var(--spacing-fs-10);
}

.status-chip--on-shift {
  --chip-bg: var(--fs-gradient-chip-completed);
  --chip-shadow: var(--shadow-fs-chip-completed);
  --chip-fg: var(--color-fs-completed);
  --chip-dot: var(--color-fs-teal-700);
}

.status-chip--off-shift {
  --chip-bg: var(--fs-gradient-chip-cancelled);
  --chip-shadow: var(--shadow-fs-chip-cancelled);
  --chip-fg: var(--color-fs-cancelled);
  --chip-dot: var(--color-fs-line-input);
}

.status-chip__dot {
  flex: none;
  width: 6px;
  height: 6px;
  border-radius: var(--radius-fs-pill);
  background: var(--chip-dot);
}

@media (forced-colors: active) {
  .status-chip {
    border: 1px solid CanvasText;
  }

  .status-chip__dot {
    forced-color-adjust: none;
    background: CanvasText;
  }
}
</style>
