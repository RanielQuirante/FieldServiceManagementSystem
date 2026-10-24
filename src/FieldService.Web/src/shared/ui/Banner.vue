<script setup lang="ts">
import { computed } from 'vue'

import AppIcon from './AppIcon.vue'
import type { IconName } from './icons'

type BannerVariant = 'info' | 'success' | 'warning' | 'error'

const props = withDefaults(
  defineProps<{
    title: string
    variant?: BannerVariant
    message?: string | null
    detail?: string | null
    reference?: string | null
    icon?: IconName
    role?: 'status' | 'alert' | null
  }>(),
  {
    variant: 'info',
    message: null,
    detail: null,
    reference: null,
    icon: undefined,
    role: 'status',
  },
)

const defaultIcons: Readonly<Record<BannerVariant, IconName>> = {
  info: 'bell',
  success: 'check-circle',
  warning: 'refresh',
  error: 'alert-triangle',
}

const iconName = computed(() => props.icon ?? defaultIcons[props.variant])
</script>

<template>
  <div
    :role="role ?? undefined"
    :class="['banner', `banner--${variant}`, { 'banner--stacked': Boolean(detail) }]"
  >
    <span class="banner__tile" aria-hidden="true">
      <AppIcon :name="iconName" :size="18" />
    </span>
    <div class="banner__body">
      <div v-if="detail" class="banner__lines">
        <p class="banner__text">
          <strong class="banner__title">{{ title }}</strong
          ><span v-if="message" class="banner__message">{{ ` ${message}` }}</span>
        </p>
        <p class="banner__detail">{{ detail }}</p>
      </div>
      <p v-else class="banner__text">
        <strong class="banner__title">{{ title }}</strong
        ><span v-if="message" class="banner__message">{{ ` ${message}` }}</span>
      </p>
      <p v-if="reference" class="banner__reference">
        Reference <span class="banner__reference-id">{{ reference }}</span>
      </p>
      <slot name="actions" />
    </div>
  </div>
</template>

<style scoped>
.banner {
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

.banner__tile {
  flex: none;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  border-radius: var(--radius-fs-control-sm);
}

.banner__body {
  display: flex;
  flex: 1;
  flex-direction: column;
  align-items: flex-start;
  gap: var(--spacing-fs-10);
  min-width: 0;
}

.banner__text {
  margin: 0;
  text-wrap: pretty;
}

.banner__title {
  font-weight: 600;
}

.banner__message {
  color: var(--color-fs-ink-2);
}

.banner--stacked {
  align-items: flex-start;
}

.banner__lines {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-fs-4);
  min-width: 0;
}

.banner__detail {
  margin: 0;
  color: var(--color-fs-ink-2);
  white-space: pre-line;
  overflow-wrap: anywhere;
  text-wrap: pretty;
}

.banner__reference {
  margin: 0;
  font-size: var(--text-fs-meta);
  line-height: var(--text-fs-meta--line-height);
  color: var(--color-fs-ink-2);
}

.banner__reference-id {
  display: inline-block;
  max-width: 100%;
  font-family: var(--font-mono);
  font-weight: 500;
  overflow-wrap: anywhere;
}

.banner--info {
  padding: var(--spacing-fs-12);
  border-color: var(--fs-glass-g1-border);
  background: var(--fs-glass-g1-bg);
  -webkit-backdrop-filter: var(--fs-glass-g1-filter);
  backdrop-filter: var(--fs-glass-g1-filter);
  box-shadow: var(--shadow-fs-glass);
}

.banner--info .banner__tile {
  background: var(--fs-gradient-paused-tile);
  box-shadow: var(--shadow-fs-paused-tile);
  color: var(--color-fs-ink-2);
}

.banner--success {
  border-color: var(--color-fs-online-border);
  background: var(--fs-gradient-online-banner);
  box-shadow: var(--shadow-fs-online-banner);
}

.banner--success .banner__tile {
  background: var(--fs-gradient-pass);
  color: var(--color-fs-on-dark);
}

.banner--warning {
  border-color: var(--color-fs-reconnecting-border);
  background: var(--fs-gradient-reconnecting-banner);
  box-shadow: var(--shadow-fs-reconnecting-banner);
}

.banner--warning .banner__tile {
  background: var(--fs-gradient-accent);
  box-shadow: var(--shadow-fs-accent-tile);
  color: var(--color-fs-ink);
}

.banner--error {
  align-items: flex-start;
  border-color: var(--color-fs-error-border);
  background: var(--fs-gradient-error-banner);
  box-shadow: var(--shadow-fs-error-banner);
}

.banner--error .banner__tile {
  background: var(--color-fs-danger);
  box-shadow: var(--shadow-fs-danger-tile);
  color: var(--color-fs-on-dark);
}

@supports not ((backdrop-filter: blur(1px)) or (-webkit-backdrop-filter: blur(1px))) {
  .banner--info {
    background: var(--fs-glass-g3-bg);
  }
}

@media (prefers-reduced-transparency: reduce) {
  .banner--info {
    background: var(--color-fs-surface);
    -webkit-backdrop-filter: none;
    backdrop-filter: none;
  }
}

@media (forced-colors: active) {
  .banner {
    border-color: CanvasText;
  }
}
</style>
