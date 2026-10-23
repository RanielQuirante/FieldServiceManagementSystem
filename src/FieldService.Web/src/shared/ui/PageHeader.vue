<script setup lang="ts">
import { onMounted, useTemplateRef } from 'vue'

import LoadingState from './LoadingState.vue'

const props = withDefaults(
  defineProps<{
    title: string
    subline?: string | null
    jobNumber?: string | null
    numberSuffix?: string | null
    loading?: boolean
    focusOnMount?: boolean
    headingId?: string
  }>(),
  {
    subline: null,
    jobNumber: null,
    numberSuffix: null,
    loading: false,
    focusOnMount: true,
    headingId: undefined,
  },
)

const heading = useTemplateRef<HTMLHeadingElement>('heading')

function focusHeading(): void {
  document.documentElement.scrollTop = 0
  heading.value?.focus({ preventScroll: true })
}

onMounted(() => {
  if (props.focusOnMount) {
    focusHeading()
  }
})

defineExpose({ focusHeading })
</script>

<template>
  <header :class="['page-header', { 'page-header--loading': loading }]">
    <div class="page-header__text">
      <LoadingState v-if="loading" :shapes="['page-number']" />
      <span v-else-if="jobNumber && numberSuffix" class="page-header__eyebrow"
        ><span class="page-header__number">{{ jobNumber }}</span
        ><span aria-hidden="true">{{ ' · ' }}</span
        ><span class="visually-hidden">{{ ', ' }}</span
        ><span>{{ numberSuffix }}</span></span
      >
      <span v-else-if="jobNumber" class="page-header__number">{{ jobNumber }}</span>
      <h1 :id="headingId" ref="heading" class="page-header__title" tabindex="-1">
        <template v-if="loading">
          <span class="visually-hidden">{{ title }}</span>
          <LoadingState :shapes="['page-title']" />
        </template>
        <template v-else>{{ title }}</template>
      </h1>
      <p v-if="subline && !loading" class="page-header__subline">{{ subline }}</p>
      <div v-if="$slots.meta && !loading" class="page-header__meta">
        <slot name="meta" />
      </div>
    </div>
    <div v-if="$slots.actions" class="page-header__actions">
      <slot name="actions" />
    </div>
  </header>
</template>

<style scoped>
.page-header {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  justify-content: space-between;
  gap: var(--spacing-fs-16);
}

.page-header__text {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-fs-6);
  min-width: 0;
}

.page-header__number {
  color: var(--color-fs-muted);
  font-family: var(--font-mono);
  font-size: 0.875rem;
  font-weight: 500;
  letter-spacing: var(--text-fs-mono--letter-spacing);
  font-variant-numeric: tabular-nums;
}

.page-header__eyebrow {
  color: var(--color-fs-muted);
  font-size: 0.875rem;
  font-weight: 500;
  line-height: 1.4;
}

.page-header__title {
  align-self: flex-start;
  margin: 0;
  color: var(--color-fs-ink);
  font-size: var(--text-fs-page-title);
  font-weight: var(--text-fs-page-title--font-weight);
  line-height: var(--text-fs-page-title--line-height);
  letter-spacing: var(--text-fs-page-title--letter-spacing);
  overflow-wrap: anywhere;
  text-wrap: balance;
}

.page-header--loading .page-header__title {
  align-self: stretch;
}

.page-header__title:focus {
  outline: none;
}

@supports (background-clip: text) or (-webkit-background-clip: text) {
  .page-header__title {
    background: var(--fs-gradient-primary);
    -webkit-background-clip: text;
    background-clip: text;
    -webkit-text-fill-color: transparent;
  }
}

.page-header__subline {
  margin: 0;
  color: var(--color-fs-muted);
  font-size: var(--text-fs-body);
  line-height: 1.4;
}

.page-header__meta {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-fs-10);
  color: var(--color-fs-muted);
  font-size: var(--text-fs-meta);
  line-height: var(--text-fs-meta--line-height);
  font-variant-numeric: tabular-nums;
}

.page-header__actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-fs-12);
}

@media (forced-colors: active) {
  .page-header__title {
    background: none;
    -webkit-text-fill-color: currentColor;
  }
}
</style>
