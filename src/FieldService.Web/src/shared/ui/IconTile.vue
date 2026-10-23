<script setup lang="ts">
import { computed } from 'vue'

import AppIcon from './AppIcon.vue'
import type { IconName, IconSize } from './icons'
import type { StatusTone } from './status-appearance'

const props = withDefaults(
  defineProps<{
    icon: IconName | null
    size?: 36 | 40 | 44 | 56
    tone?: 'neutral' | 'fail' | StatusTone
  }>(),
  { size: 36, tone: 'neutral' },
)

const iconSizes: Readonly<Record<36 | 40 | 44 | 56, IconSize>> = { 36: 18, 40: 20, 44: 22, 56: 28 }

const iconSize = computed(() => iconSizes[props.size])
</script>

<template>
  <span :class="['icon-tile', `icon-tile--${size}`, `icon-tile--${tone}`]" aria-hidden="true">
    <AppIcon v-if="icon" :name="icon" :size="iconSize" />
  </span>
</template>

<style scoped>
.icon-tile {
  flex: none;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 2.25rem;
  height: 2.25rem;
  border-radius: var(--radius-fs-control);
  background: var(--tile-bg);
  box-shadow: var(--tile-shadow);
  color: var(--tile-fg);
}

.icon-tile--40 {
  width: 2.5rem;
  height: 2.5rem;
}

.icon-tile--44 {
  width: 2.75rem;
  height: 2.75rem;
  border-radius: var(--radius-fs-well);
}

.icon-tile--56 {
  width: 3.5rem;
  height: 3.5rem;
  border-radius: var(--radius-fs-toast);
}

.icon-tile--neutral {
  --tile-bg: var(--fs-tint-neutral);
  --tile-shadow: var(--shadow-fs-tile);
  --tile-fg: var(--color-fs-teal-700);
}

.icon-tile--unassigned {
  --tile-bg: var(--fs-gradient-chip-unassigned);
  --tile-shadow: var(--shadow-fs-tile-unassigned);
  --tile-fg: var(--color-fs-unassigned);

  border: 1px dashed var(--color-fs-line-input);
}

.icon-tile--assigned {
  --tile-bg: var(--fs-gradient-chip-assigned);
  --tile-shadow: var(--shadow-fs-tile-assigned);
  --tile-fg: var(--color-fs-assigned);
}

.icon-tile--in-progress {
  --tile-bg: var(--fs-gradient-chip-in-progress);
  --tile-shadow: var(--shadow-fs-tile-in-progress);
  --tile-fg: var(--color-fs-in-progress);
}

.icon-tile--on-hold {
  --tile-bg: var(--fs-gradient-chip-on-hold);
  --tile-shadow: var(--shadow-fs-tile-on-hold);
  --tile-fg: var(--color-fs-on-hold);
}

.icon-tile--completed {
  --tile-bg: var(--fs-gradient-chip-completed);
  --tile-shadow: var(--shadow-fs-tile-completed);
  --tile-fg: var(--color-fs-completed);
}

.icon-tile--cancelled,
.icon-tile--unknown {
  --tile-bg: var(--fs-gradient-chip-cancelled);
  --tile-shadow: var(--shadow-fs-tile-cancelled);
  --tile-fg: var(--color-fs-cancelled);
}

.icon-tile--fail {
  --tile-bg: var(--fs-gradient-chip-fail);
  --tile-shadow: var(--shadow-fs-tile-fail);
  --tile-fg: var(--color-fs-fail);
}

@media (forced-colors: active) {
  .icon-tile {
    border: 1px solid CanvasText;
  }
}
</style>
