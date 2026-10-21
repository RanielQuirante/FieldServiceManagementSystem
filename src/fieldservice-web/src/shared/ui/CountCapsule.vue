<script setup lang="ts">
withDefaults(
  defineProps<{
    count?: number | null
    variant?: 'default' | 'in-segment' | 'on-gradient' | 'new'
  }>(),
  { count: null, variant: 'default' },
)
</script>

<template>
  <span v-if="variant === 'new'" class="count-capsule count-capsule--new">New</span>
  <span v-else :class="['count-capsule', `count-capsule--${variant}`]">{{ count }}</span>
</template>

<style scoped>
.count-capsule {
  flex: none;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 1.625rem;
  height: 1.5rem;
  padding: 0 var(--spacing-fs-8);
  border-radius: var(--radius-fs-pill);
  background: var(--color-fs-track);
  color: var(--color-fs-ink);
  font-family: var(--font-mono);
  font-size: 0.8125rem;
  font-weight: 600;
  line-height: 1;
  font-variant-numeric: tabular-nums;
}

.count-capsule--in-segment {
  background: var(--color-fs-capsule-selected);
}

.count-capsule--on-gradient {
  background: var(--color-fs-surface);
}

.count-capsule--new {
  min-width: 0;
  height: 1.25rem;
  background: var(--fs-gradient-accent);
  box-shadow: var(--shadow-fs-accent);
  font-family: var(--font-sans);
  font-size: 0.6875rem;
  font-weight: 700;
  letter-spacing: 0.06em;
  text-transform: uppercase;
}

@media (forced-colors: active) {
  .count-capsule {
    border: 1px solid CanvasText;
  }
}
</style>
