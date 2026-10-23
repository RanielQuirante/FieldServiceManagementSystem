<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink, type RouteLocationRaw } from 'vue-router'

import type { JobStatus } from '@/shared/format/labels'

import type { IconName } from './icons'
import IconTile from './IconTile.vue'
import { jobStatusAppearance, type StatusTone } from './status-appearance'

const props = withDefaults(
  defineProps<{
    label: string | null
    icon: IconName | null
    tone?: JobStatus | 'neutral'
    value?: string | null
    subline?: string | readonly string[] | null
    to?: RouteLocationRaw
    loading?: boolean
  }>(),
  { tone: 'neutral', value: null, subline: null, to: undefined, loading: false },
)

defineSlots<{
  value?(): unknown
}>()

const tileTone = computed<'neutral' | StatusTone>(() =>
  props.tone === 'neutral' ? 'neutral' : jobStatusAppearance(props.tone).tone,
)

const sublineParts = computed<readonly string[]>(() => {
  if (props.subline === null) {
    return []
  }
  return typeof props.subline === 'string' ? [props.subline] : props.subline
})

const valueText = computed(() => props.value ?? '—')

const unavailable = computed(() => props.value === null)

const linkAttributes = computed(() =>
  props.to === undefined
    ? {}
    : {
        to: props.to,
        'aria-label': [props.label ?? '', valueText.value, ...sublineParts.value]
          .filter((part) => part.length > 0)
          .join(', '),
      },
)

const element = computed(() => (props.to === undefined ? 'div' : RouterLink))
</script>

<template>
  <component
    :is="element"
    v-bind="linkAttributes"
    :class="['kpi-tile', `kpi-tile--${tileTone}`, { 'kpi-tile--link': to !== undefined }]"
  >
    <span class="kpi-tile__head">
      <span v-if="label === null" class="kpi-tile__label-skeleton" aria-hidden="true"></span>
      <span v-else class="kpi-tile__label">{{ label }}</span>
      <IconTile :icon="icon" :size="40" :tone="tileTone" />
    </span>
    <span v-if="loading" class="kpi-tile__skeleton" aria-hidden="true">
      <span class="kpi-tile__skeleton-value"></span>
      <span class="kpi-tile__skeleton-subline"></span>
    </span>
    <span v-else class="kpi-tile__body">
      <span v-if="$slots.value" class="kpi-tile__content"><slot name="value" /></span>
      <span v-else :class="['kpi-tile__value', { 'kpi-tile__value--unavailable': unavailable }]">{{
        valueText
      }}</span>
      <span v-if="sublineParts.length > 0" class="kpi-tile__subline"
        ><template v-for="(part, index) in sublineParts" :key="index"
          ><template v-if="index > 0"
            ><span aria-hidden="true">{{ ' · ' }}</span
            ><span class="visually-hidden">{{ ', ' }}</span></template
          >{{ part }}</template
        ></span
      >
    </span>
  </component>
</template>

<style scoped>
.kpi-tile {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-fs-14);
  min-width: 0;
  padding: var(--spacing-fs-20);
  border: 1px solid var(--fs-glass-g1-border);
  border-radius: var(--radius-fs-card);
  background: var(--kpi-bg);
  -webkit-backdrop-filter: var(--fs-glass-g1-filter);
  backdrop-filter: var(--fs-glass-g1-filter);
  box-shadow: var(--shadow-fs-glass);
  color: var(--color-fs-ink);
  text-decoration: none;
}

.kpi-tile--neutral {
  --kpi-bg: var(--fs-gradient-kpi-neutral);
}

.kpi-tile--unassigned {
  --kpi-bg: var(--fs-gradient-kpi-unassigned);
}

.kpi-tile--assigned {
  --kpi-bg: var(--fs-gradient-kpi-assigned);
}

.kpi-tile--in-progress {
  --kpi-bg: var(--fs-gradient-kpi-in-progress);
}

.kpi-tile--on-hold {
  --kpi-bg: var(--fs-gradient-kpi-on-hold);
}

.kpi-tile--completed {
  --kpi-bg: var(--fs-gradient-kpi-completed);
}

.kpi-tile--cancelled,
.kpi-tile--unknown {
  --kpi-bg: var(--fs-gradient-kpi-cancelled);
}

.kpi-tile__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--spacing-fs-12);
}

.kpi-tile__label {
  min-width: 0;
  color: var(--color-fs-ink-2);
  font-size: var(--text-fs-label);
  font-weight: var(--text-fs-label--font-weight);
  line-height: var(--text-fs-label--line-height);
  letter-spacing: var(--text-fs-label--letter-spacing);
}

.kpi-tile__body {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-fs-4);
  min-width: 0;
}

.kpi-tile__value {
  font-size: var(--text-fs-kpi);
  font-weight: var(--text-fs-kpi--font-weight);
  line-height: var(--text-fs-kpi--line-height);
  letter-spacing: var(--text-fs-kpi--letter-spacing);
  font-variant-numeric: tabular-nums;
}

.kpi-tile__value--unavailable {
  color: var(--color-fs-muted);
}

.kpi-tile__content {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-fs-8);
  min-height: 2.25rem;
}

.kpi-tile__subline {
  color: var(--color-fs-muted);
  font-size: var(--text-fs-meta);
  line-height: var(--text-fs-meta--line-height);
  font-variant-numeric: tabular-nums;
}

.kpi-tile__skeleton {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-fs-8);
}

.kpi-tile__label-skeleton,
.kpi-tile__skeleton-value,
.kpi-tile__skeleton-subline {
  display: block;
  max-width: 100%;
  border-radius: var(--radius-fs-pill);
  background: var(--fs-skeleton);
}

.kpi-tile__label-skeleton {
  width: 4.5rem;
  height: 0.8125rem;
}

.kpi-tile__skeleton-value {
  width: 3rem;
  height: 1.75rem;
}

.kpi-tile__skeleton-subline {
  width: 72%;
  height: 0.875rem;
}

@supports not ((backdrop-filter: blur(1px)) or (-webkit-backdrop-filter: blur(1px))) {
  .kpi-tile {
    background: var(--fs-glass-g3-bg);
  }
}

@media (prefers-reduced-transparency: reduce) {
  .kpi-tile {
    background: var(--color-fs-surface);
    -webkit-backdrop-filter: none;
    backdrop-filter: none;
  }
}

@media (forced-colors: active) {
  .kpi-tile {
    border-color: CanvasText;
  }

  .kpi-tile__label-skeleton,
  .kpi-tile__skeleton-value,
  .kpi-tile__skeleton-subline {
    border: 1px solid CanvasText;
  }
}
</style>
