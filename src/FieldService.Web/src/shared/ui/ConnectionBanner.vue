<script lang="ts">
export type ConnectionNotice = 'reconnecting' | 'paused' | 'back-online' | 'refresh-failed'

export interface RefreshFailureCopy {
  readonly title: string
  readonly message: string
  readonly reference: string | null
}
</script>

<script setup lang="ts">
import { nextTick, useTemplateRef, watch } from 'vue'

import AppButton from './AppButton.vue'
import AppIcon from './AppIcon.vue'

const props = withDefaults(
  defineProps<{
    notice: ConnectionNotice | null
    time?: string | null
    failure?: RefreshFailureCopy | null
    busy?: boolean
  }>(),
  { time: null, failure: null, busy: false },
)

const emit = defineEmits<{
  refresh: []
}>()

const slot = useTemplateRef<HTMLElement>('slot')

function focusPageHeading(): void {
  document.querySelector<HTMLElement>('main h1')?.focus()
}

watch(
  () => props.notice,
  () => {
    const active = document.activeElement
    if (active === null || slot.value?.contains(active) !== true) {
      return
    }
    void nextTick(() => {
      const button = slot.value?.querySelector<HTMLElement>('button')
      if (button !== null && button !== undefined) {
        button.focus()
      } else {
        focusPageHeading()
      }
    })
  },
  { flush: 'pre' },
)
</script>

<template>
  <div
    ref="slot"
    :class="['connection-banner', { 'connection-banner--shown': notice !== null }]"
    role="status"
  >
    <div
      v-if="notice !== null"
      :class="['connection-banner__banner', `connection-banner__banner--${notice}`]"
      :data-notice="notice"
    >
      <span class="connection-banner__tile" aria-hidden="true">
        <AppIcon v-if="notice === 'reconnecting'" name="refresh" :size="18" />
        <AppIcon v-else-if="notice === 'back-online'" name="check-circle" :size="18" />
        <AppIcon v-else-if="notice === 'paused'" name="wifi-off" :size="18" />
        <AppIcon v-else name="alert-triangle" :size="18" />
      </span>
      <p v-if="notice === 'reconnecting'" class="connection-banner__text">
        <strong class="connection-banner__lead">Reconnecting to live updates.</strong
        ><span class="connection-banner__detail">{{
          ' Job statuses on this page may be out of date.'
        }}</span>
      </p>
      <p v-else-if="notice === 'back-online'" class="connection-banner__text">
        <strong class="connection-banner__lead">Back online.</strong
        ><span class="connection-banner__detail"
          >{{ ' Jobs refreshed at ' }}<span class="connection-banner__time">{{ time }}</span
          >.</span
        >
      </p>
      <div v-else-if="notice === 'paused'" class="connection-banner__row">
        <p class="connection-banner__text connection-banner__text--wrapping">
          <strong class="connection-banner__lead">Live updates are paused.</strong
          ><span v-if="time" class="connection-banner__detail"
            >{{ ' Showing data from ' }}<span class="connection-banner__time">{{ time }}</span
            >.</span
          >
        </p>
        <AppButton
          class="connection-banner__action"
          variant="secondary"
          size="small"
          icon="refresh"
          :busy="busy"
          busy-label="Refreshing…"
          @click="emit('refresh')"
          >Refresh now</AppButton
        >
      </div>
      <div v-else class="connection-banner__column">
        <p class="connection-banner__text">
          <strong class="connection-banner__lead">{{ failure?.title }}</strong
          ><span v-if="failure?.message" class="connection-banner__detail">{{
            ` ${failure.message}`
          }}</span>
        </p>
        <p v-if="failure?.reference" class="connection-banner__reference">
          Reference <span class="connection-banner__reference-id">{{ failure.reference }}</span>
        </p>
        <AppButton
          variant="secondary"
          size="small"
          icon="refresh"
          :busy="busy"
          busy-label="Refreshing…"
          @click="emit('refresh')"
          >Try again</AppButton
        >
      </div>
    </div>
  </div>
</template>

<style scoped>
.connection-banner {
  width: 100%;
  max-width: 80rem;
  margin: 0 auto;
}

.connection-banner--shown {
  margin-bottom: var(--spacing-fs-24);
}

.connection-banner__banner {
  display: flex;
  align-items: center;
  gap: var(--spacing-fs-12);
  padding: var(--spacing-fs-12) var(--spacing-fs-16) var(--spacing-fs-12) var(--spacing-fs-12);
  border: 1px solid transparent;
  border-radius: var(--radius-fs-banner);
  color: var(--color-fs-ink);
  font-size: var(--text-fs-table);
  line-height: var(--text-fs-table--line-height);
}

.connection-banner__tile {
  flex: none;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  border-radius: var(--radius-fs-control-sm);
}

.connection-banner__text {
  flex: 1;
  min-width: 0;
  margin: 0;
  text-wrap: pretty;
}

.connection-banner__lead {
  font-weight: 600;
}

.connection-banner__detail {
  color: var(--color-fs-ink-2);
}

.connection-banner__time {
  white-space: nowrap;
}

.connection-banner__row {
  display: flex;
  flex: 1;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-fs-10) var(--spacing-fs-16);
  min-width: 0;
}

.connection-banner__text--wrapping {
  flex: 1 1 220px;
}

.connection-banner__action {
  flex: none;
}

.connection-banner__column {
  display: flex;
  flex: 1;
  flex-direction: column;
  align-items: flex-start;
  gap: var(--spacing-fs-10);
  min-width: 0;
}

.connection-banner__reference {
  margin: 0;
  color: var(--color-fs-ink-2);
  font-size: var(--text-fs-meta);
  line-height: var(--text-fs-meta--line-height);
}

.connection-banner__reference-id {
  display: inline-block;
  max-width: 100%;
  font-family: var(--font-mono);
  font-weight: 500;
  overflow-wrap: anywhere;
}

.connection-banner__banner--reconnecting {
  border-color: var(--color-fs-reconnecting-border);
  background: var(--fs-gradient-reconnecting-banner);
  box-shadow: var(--shadow-fs-reconnecting-banner);
}

.connection-banner__banner--reconnecting .connection-banner__tile {
  background: var(--fs-gradient-accent);
  box-shadow: var(--shadow-fs-accent-tile);
  color: var(--color-fs-ink);
}

.connection-banner__banner--back-online {
  border-color: var(--color-fs-online-border);
  background: var(--fs-gradient-online-banner);
  box-shadow: var(--shadow-fs-online-banner);
}

.connection-banner__banner--back-online .connection-banner__tile {
  background: var(--fs-gradient-pass);
  color: var(--color-fs-on-dark);
}

.connection-banner__banner--paused {
  align-items: flex-start;
  padding: var(--spacing-fs-12);
  border-color: var(--fs-glass-g1-border);
  background: var(--fs-glass-g1-bg);
  -webkit-backdrop-filter: var(--fs-glass-g1-filter);
  backdrop-filter: var(--fs-glass-g1-filter);
  box-shadow: var(--shadow-fs-glass);
}

.connection-banner__banner--paused .connection-banner__tile {
  background: var(--fs-gradient-paused-tile);
  box-shadow: var(--shadow-fs-paused-tile);
  color: var(--color-fs-ink-2);
}

.connection-banner__banner--refresh-failed {
  align-items: flex-start;
  border-color: var(--color-fs-error-border);
  background: var(--fs-gradient-error-banner);
  box-shadow: var(--shadow-fs-error-banner);
}

.connection-banner__banner--refresh-failed .connection-banner__tile {
  background: var(--color-fs-danger);
  box-shadow: var(--shadow-fs-danger-tile);
  color: var(--color-fs-on-dark);
}

@supports not ((backdrop-filter: blur(1px)) or (-webkit-backdrop-filter: blur(1px))) {
  .connection-banner__banner--paused {
    background: var(--fs-glass-g3-bg);
  }
}

@media (prefers-reduced-transparency: reduce) {
  .connection-banner__banner--paused {
    background: var(--color-fs-surface);
    -webkit-backdrop-filter: none;
    backdrop-filter: none;
  }
}

@media (forced-colors: active) {
  .connection-banner__banner {
    border-color: CanvasText;
  }
}
</style>
