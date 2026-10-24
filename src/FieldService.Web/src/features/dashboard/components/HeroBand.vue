<script setup lang="ts">
import { computed, onMounted, useId, useTemplateRef } from 'vue'

import { formatLongDate } from '@/shared/format/date-time'

import type { DashboardSummary } from '../dashboard-api'
import {
  greetingText,
  heroSentence,
  onShiftFigure,
  type DashboardPartState,
} from '../dashboard-copy'

const props = defineProps<{
  now: Date
  today: Date
  displayName: string | null
  summary: DashboardSummary | null
  state: DashboardPartState
}>()

const titleId = `${useId()}-hero-title`

const heading = useTemplateRef<HTMLHeadingElement>('heading')

const ready = computed(() => (props.state === 'ready' ? props.summary : null))

const greeting = computed(() => greetingText(props.now, props.displayName))

const sentence = computed(() =>
  ready.value === null ? null : heroSentence(ready.value.openJobs, ready.value.range),
)

const onShift = computed(() =>
  ready.value === null ? null : onShiftFigure(ready.value.technicians),
)

const assigned = computed(() =>
  ready.value === null ? null : String(ready.value.openJobs.withTechnician),
)

function focusHeading(): void {
  document.documentElement.scrollTop = 0
  heading.value?.focus({ preventScroll: true })
}

onMounted(focusHeading)

defineExpose({ focusHeading })
</script>

<template>
  <section class="hero-band" data-surface="dark" :aria-labelledby="titleId">
    <div class="hero-band__layout">
      <div class="hero-band__text">
        <span class="hero-band__eyebrow">{{ formatLongDate(today) }}</span>
        <h1 :id="titleId" ref="heading" class="hero-band__title" tabindex="-1">
          {{ greeting }}
        </h1>
        <p v-if="sentence !== null" class="hero-band__sentence">{{ sentence }}</p>
        <span
          v-else-if="state === 'loading'"
          class="hero-band__sentence-skeleton"
          aria-hidden="true"
        ></span>
      </div>
      <div v-if="state !== 'error'" class="hero-band__tiles">
        <div class="hero-band__tile">
          <span v-if="onShift !== null" class="hero-band__value">{{ onShift }}</span>
          <span v-else class="hero-band__value-skeleton" aria-hidden="true"></span>
          <span class="hero-band__caption">technicians on shift</span>
        </div>
        <div class="hero-band__tile">
          <span v-if="assigned !== null" class="hero-band__value">{{ assigned }}</span>
          <span v-else class="hero-band__value-skeleton" aria-hidden="true"></span>
          <span class="hero-band__caption">open jobs assigned</span>
        </div>
      </div>
    </div>
  </section>
</template>

<style scoped>
.hero-band {
  border-radius: var(--radius-fs-hero);
  background: var(--fs-glow-band), var(--fs-gradient-hero);
  box-shadow: var(--shadow-fs-hero);
  color: var(--color-fs-on-dark);
  container-type: inline-size;
}

.hero-band__layout {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  justify-content: space-between;
  gap: var(--spacing-fs-24);
  padding: var(--spacing-fs-28) var(--spacing-fs-32);
}

.hero-band__text {
  display: flex;
  flex: 1 1 420px;
  flex-direction: column;
  gap: var(--spacing-fs-8);
  min-width: 0;
}

.hero-band__eyebrow {
  color: var(--color-fs-on-dark-2);
  font-size: var(--text-fs-eyebrow);
  font-weight: var(--text-fs-eyebrow--font-weight);
  line-height: var(--text-fs-eyebrow--line-height);
  letter-spacing: var(--text-fs-eyebrow--letter-spacing);
  text-transform: uppercase;
}

.hero-band__title {
  margin: 0;
  color: var(--color-fs-on-dark);
  font-size: var(--text-fs-hero);
  font-weight: var(--text-fs-hero--font-weight);
  line-height: var(--text-fs-hero--line-height);
  letter-spacing: var(--text-fs-hero--letter-spacing);
  text-wrap: balance;
}

.hero-band__title:focus {
  outline: none;
}

.hero-band__sentence {
  margin: 0;
  color: var(--color-fs-on-dark-2);
  font-size: 1rem;
  line-height: 1.5;
  text-wrap: pretty;
}

.hero-band__sentence-skeleton {
  display: block;
  width: min(280px, 60%);
  height: 16px;
  border-radius: 8px;
  background: var(--fs-glass-g4-control-bg);
}

.hero-band__tiles {
  display: flex;
  flex-wrap: wrap;
  gap: var(--spacing-fs-12);
}

.hero-band__tile {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-fs-6);
  min-width: 168px;
  padding: var(--spacing-fs-14) 18px;
  border-radius: var(--radius-fs-banner);
  background: var(--fs-glass-g4-bg);
  -webkit-backdrop-filter: var(--fs-glass-g4-filter);
  backdrop-filter: var(--fs-glass-g4-filter);
  box-shadow: var(--shadow-fs-g4);
}

.hero-band__value {
  font-size: var(--text-fs-hero-stat);
  font-weight: var(--text-fs-hero-stat--font-weight);
  line-height: var(--text-fs-hero-stat--line-height);
  letter-spacing: var(--text-fs-hero-stat--letter-spacing);
  font-variant-numeric: tabular-nums;
}

.hero-band__value-skeleton {
  display: block;
  width: 64px;
  height: 28px;
  border-radius: var(--radius-fs-pill);
  background: var(--fs-glass-g4-control-bg);
}

.hero-band__caption {
  color: var(--color-fs-on-dark-2);
  font-size: var(--text-fs-meta);
  line-height: 1.3;
}

@container (width < 560px) {
  .hero-band__layout {
    gap: var(--spacing-fs-20);
    padding: var(--spacing-fs-24) var(--spacing-fs-20);
  }

  .hero-band__title {
    font-size: 1.75rem;
    line-height: 1.15;
  }

  .hero-band__tiles {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: var(--spacing-fs-12);
    width: 100%;
  }

  .hero-band__tile {
    min-width: 0;
  }
}

@media (forced-colors: active) {
  .hero-band,
  .hero-band__tile {
    border: 1px solid CanvasText;
  }

  .hero-band__sentence-skeleton,
  .hero-band__value-skeleton {
    border: 1px solid CanvasText;
  }
}
</style>
