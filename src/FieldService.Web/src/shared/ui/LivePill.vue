<script lang="ts">
export type LivePillState = 'connecting' | 'live' | 'reconnecting' | 'paused'
</script>

<script setup lang="ts">
import { computed } from 'vue'

import { formatTime } from '@/shared/format/date-time'

const props = withDefaults(
  defineProps<{
    state: LivePillState
    updatedAt?: Date | null
    tone?: 'light' | 'dark'
  }>(),
  { updatedAt: null, tone: 'light' },
)

const stateLabels: Readonly<Record<Exclude<LivePillState, 'live'>, string>> = {
  connecting: 'Connecting…',
  reconnecting: 'Reconnecting…',
  paused: 'Live updates paused',
}

const amber = computed(() => props.state === 'live' || props.state === 'reconnecting')

const time = computed(() => (props.updatedAt === null ? null : formatTime(props.updatedAt)))

const label = computed(() => (props.state === 'live' ? 'Live' : stateLabels[props.state]))
</script>

<template>
  <span
    :class="['live-pill', `live-pill--${tone}`, { 'live-pill--amber': amber }]"
    :data-state="state"
  >
    <span class="live-pill__dot" aria-hidden="true"></span>
    <span class="live-pill__text"
      >{{ label
      }}<template v-if="state === 'live' && time"
        ><span aria-hidden="true">{{ ' · ' }}</span
        ><span class="visually-hidden">{{ ', ' }}</span
        >{{ `updated ${time}` }}</template
      ></span
    >
  </span>
</template>

<style scoped>
.live-pill {
  display: inline-flex;
  align-items: center;
  gap: var(--spacing-fs-8);
  height: 2rem;
  padding: 0 var(--spacing-fs-12) 0 var(--spacing-fs-10);
  border: 1px solid var(--color-fs-live-pill-border);
  border-radius: var(--radius-fs-pill);
  background: var(--fs-glass-g1-bg);
  -webkit-backdrop-filter: blur(var(--blur-fs-dark));
  backdrop-filter: blur(var(--blur-fs-dark));
  box-shadow: var(--shadow-fs-live-pill);
  color: var(--color-fs-ink);
  font-size: var(--text-fs-meta);
  font-weight: 600;
  line-height: 1;
  white-space: nowrap;
}

.live-pill--dark {
  border-color: transparent;
  background: var(--fs-glass-g4-control-bg);
  box-shadow: var(--shadow-fs-g4-live-pill);
  color: var(--color-fs-on-dark);
}

.live-pill__dot {
  flex: none;
  width: 8px;
  height: 8px;
  border-radius: var(--radius-fs-pill);
  background: var(--color-fs-line-input);
}

.live-pill--amber .live-pill__dot {
  background: var(--fs-gradient-accent);
  box-shadow: var(--shadow-fs-live-dot);
}

@supports not ((backdrop-filter: blur(1px)) or (-webkit-backdrop-filter: blur(1px))) {
  .live-pill--light {
    background: var(--color-fs-surface);
  }
}

@media (prefers-reduced-transparency: reduce) {
  .live-pill--light {
    background: var(--color-fs-surface);
    -webkit-backdrop-filter: none;
    backdrop-filter: none;
  }
}

@media (forced-colors: active) {
  .live-pill {
    border-color: CanvasText;
  }
}
</style>
