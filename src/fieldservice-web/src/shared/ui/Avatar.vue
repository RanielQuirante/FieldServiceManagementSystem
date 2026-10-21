<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(
  defineProps<{
    name: string
    size?: 28 | 32 | 36 | 40
    self?: boolean
  }>(),
  { size: 32, self: false },
)

const initials = computed(() => {
  const words = props.name
    .trim()
    .split(/\s+/)
    .filter((word) => word.length > 0)
  const first = words[0]?.charAt(0) ?? ''
  const last = words.length > 1 ? (words[words.length - 1]?.charAt(0) ?? '') : ''
  return `${first}${last}`.toUpperCase()
})
</script>

<template>
  <span :class="['avatar', `avatar--${size}`, { 'avatar--self': self }]" aria-hidden="true">{{
    initials
  }}</span>
</template>

<style scoped>
.avatar {
  flex: none;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 2rem;
  height: 2rem;
  border-radius: var(--radius-fs-pill);
  background: var(--fs-tint-neutral);
  box-shadow: var(--shadow-fs-avatar);
  color: var(--color-fs-navy-800);
  font-size: 0.75rem;
  font-weight: 600;
  line-height: 1;
  letter-spacing: 0.02em;
}

.avatar--28 {
  width: 1.75rem;
  height: 1.75rem;
  font-size: 0.6875rem;
}

.avatar--36 {
  width: 2.25rem;
  height: 2.25rem;
  font-size: 0.8125rem;
}

.avatar--40 {
  width: 2.5rem;
  height: 2.5rem;
  font-size: 0.875rem;
}

.avatar--self {
  background: var(--fs-gradient-primary);
  box-shadow: var(--shadow-fs-avatar-self);
  color: var(--color-fs-on-dark);
}

@media (forced-colors: active) {
  .avatar {
    border: 1px solid CanvasText;
  }
}
</style>
