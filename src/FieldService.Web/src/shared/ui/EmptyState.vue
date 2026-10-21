<script setup lang="ts">
import { computed } from 'vue'

import IconTile from './IconTile.vue'
import type { IconName } from './icons'

const props = withDefaults(
  defineProps<{
    title: string
    body?: string | null
    icon?: IconName
    headingLevel?: 2 | 3
  }>(),
  { body: null, icon: 'search', headingLevel: 2 },
)

const headingTag = computed(() => `h${props.headingLevel}`)
</script>

<template>
  <div class="empty-state">
    <IconTile :icon="icon" :size="56" />
    <div class="empty-state__text">
      <component :is="headingTag" class="empty-state__title">{{ title }}</component>
      <p v-if="body" class="empty-state__body">{{ body }}</p>
    </div>
    <div v-if="$slots.actions" class="empty-state__actions">
      <slot name="actions" />
    </div>
  </div>
</template>

<style scoped>
.empty-state {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: var(--spacing-fs-16);
  padding: var(--spacing-fs-32) var(--spacing-fs-24);
}

.empty-state__text {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-fs-6);
}

.empty-state__title {
  margin: 0;
  color: var(--color-fs-ink);
  font-size: var(--text-fs-section);
  font-weight: var(--text-fs-section--font-weight);
  line-height: var(--text-fs-section--line-height);
  letter-spacing: var(--text-fs-section--letter-spacing);
  text-wrap: balance;
}

.empty-state__body {
  max-width: 32.5rem;
  margin: 0;
  color: var(--color-fs-ink-2);
  font-size: var(--text-fs-body);
  line-height: var(--text-fs-body--line-height);
  text-wrap: pretty;
}

.empty-state__actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--spacing-fs-12);
}
</style>
