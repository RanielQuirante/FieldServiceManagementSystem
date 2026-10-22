<script lang="ts">
export type SkeletonShape =
  | 'job-number'
  | 'job-title'
  | 'customer'
  | 'site'
  | 'short'
  | 'schedule'
  | 'avatar'
  | 'avatar-large'
  | 'radio'
  | 'name'
  | 'technician-name'
  | 'technician-detail'
  | 'chip'
  | 'page-number'
  | 'page-title'
  | 'heading'
  | 'line'
  | 'line-medium'
  | 'line-short'
</script>

<script setup lang="ts">
withDefaults(
  defineProps<{
    label?: string | null
    shapes?: readonly SkeletonShape[]
    layout?: 'column' | 'row' | 'stack'
  }>(),
  { label: null, shapes: () => [], layout: 'column' },
)
</script>

<template>
  <span
    v-if="shapes.length > 0"
    :class="['loading-state', `loading-state--${layout}`]"
    aria-hidden="true"
  >
    <span
      v-for="(shape, index) in shapes"
      :key="index"
      :class="['loading-state__block', `loading-state__block--${shape}`]"
    ></span>
  </span>
  <p v-if="label" class="visually-hidden" role="status">{{ label }}</p>
</template>

<style scoped>
.loading-state {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-fs-6);
  min-width: 0;
}

.loading-state--row {
  flex-direction: row;
  align-items: center;
  gap: var(--spacing-fs-8);
}

.loading-state--stack {
  gap: var(--spacing-fs-12);
}

.loading-state__block {
  display: block;
  flex: none;
  max-width: 100%;
  height: 0.875rem;
  border-radius: var(--radius-fs-pill);
  background: var(--fs-skeleton);
}

.loading-state__block--job-number {
  width: 4rem;
  height: 0.75rem;
}

.loading-state__block--job-title {
  width: 11.25rem;
}

.loading-state__block--customer {
  width: 9.375rem;
}

.loading-state__block--site {
  width: 6.875rem;
  height: 0.75rem;
}

.loading-state__block--short {
  width: 3.5rem;
}

.loading-state__block--schedule {
  width: 8.25rem;
}

.loading-state__block--avatar {
  width: 1.75rem;
  height: 1.75rem;
}

.loading-state__block--avatar-large {
  width: 2.25rem;
  height: 2.25rem;
}

.loading-state__block--radio {
  width: 1.125rem;
  height: 1.125rem;
}

.loading-state__block--name {
  width: 5.5rem;
}

.loading-state__block--technician-name {
  width: 7.5rem;
}

.loading-state__block--technician-detail {
  width: 11.25rem;
  height: 0.75rem;
}

.loading-state__block--chip {
  width: 6rem;
  height: 1.625rem;
}

.loading-state__block--page-number {
  width: 4rem;
}

.loading-state__block--page-title {
  width: 20rem;
  height: 2rem;
}

.loading-state__block--heading {
  width: 8rem;
  height: 1.125rem;
}

.loading-state__block--line {
  width: 100%;
}

.loading-state__block--line-medium {
  width: 72%;
}

.loading-state__block--line-short {
  width: 48%;
}

@media (forced-colors: active) {
  .loading-state__block {
    border: 1px solid CanvasText;
  }
}
</style>
