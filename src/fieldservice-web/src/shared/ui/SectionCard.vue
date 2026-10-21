<script setup lang="ts">
import { computed, useId } from 'vue'

type SectionCardVariant = 'section' | 'plain' | 'toolbar' | 'form'

const props = withDefaults(
  defineProps<{
    variant?: SectionCardVariant
    heading?: string
    headingId?: string
  }>(),
  { variant: 'section', heading: undefined, headingId: undefined },
)

const generatedId = useId()

const titleId = computed(() => props.headingId ?? `${generatedId}-title`)

const classes = computed(() => ['section-card', `section-card--${props.variant}`])
</script>

<template>
  <section v-if="heading" :class="classes" :aria-labelledby="titleId">
    <header class="section-card__header">
      <h2 :id="titleId" class="section-card__title">{{ heading }}</h2>
      <slot name="header-end" />
    </header>
    <slot />
  </section>
  <div v-else :class="classes">
    <slot />
  </div>
</template>

<style scoped>
.section-card {
  display: flex;
  flex-direction: column;
  padding: var(--spacing-fs-8);
  border: 1px solid var(--fs-glass-g1-border);
  border-radius: var(--radius-fs-card);
  background: var(--fs-glass-g1-bg);
  -webkit-backdrop-filter: var(--fs-glass-g1-filter);
  backdrop-filter: var(--fs-glass-g1-filter);
  box-shadow: var(--shadow-fs-glass);
}

.section-card__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--spacing-fs-12);
  padding: var(--spacing-fs-14) var(--spacing-fs-16) var(--spacing-fs-12);
}

.section-card__title {
  margin: 0;
  font-size: var(--text-fs-card-heading);
  font-weight: var(--text-fs-card-heading--font-weight);
  line-height: var(--text-fs-card-heading--line-height);
  letter-spacing: var(--text-fs-card-heading--letter-spacing);
  color: var(--color-fs-ink);
  text-wrap: balance;
}

.section-card--plain {
  padding: clamp(var(--spacing-fs-24), 7vw, var(--spacing-fs-40));
  border-radius: var(--radius-fs-hero);
}

.section-card--toolbar {
  padding: var(--spacing-fs-12);
}

.section-card--form {
  gap: var(--spacing-fs-16);
  padding: 22px var(--spacing-fs-24) var(--spacing-fs-24);
}

.section-card--form .section-card__header {
  padding: 0;
}

@supports not ((backdrop-filter: blur(1px)) or (-webkit-backdrop-filter: blur(1px))) {
  .section-card {
    background: var(--fs-glass-g3-bg);
  }
}

@media (prefers-reduced-transparency: reduce) {
  .section-card {
    background: var(--color-fs-surface);
    -webkit-backdrop-filter: none;
    backdrop-filter: none;
  }
}

@media (forced-colors: active) {
  .section-card {
    border-color: CanvasText;
  }
}
</style>
