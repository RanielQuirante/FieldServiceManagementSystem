<script lang="ts">
export function rangeSummary(
  page: number,
  pageSize: number,
  totalCount: number,
  noun: string,
  nounSingular: string,
): string {
  const label = totalCount === 1 ? nounSingular : noun
  const first = totalCount === 0 ? 0 : Math.min((page - 1) * pageSize + 1, totalCount)
  const last = Math.min(page * pageSize, totalCount)
  if (first === last) {
    return `Showing ${last} of ${totalCount} ${label}`
  }
  return `Showing ${first}–${last} of ${totalCount} ${label}`
}
</script>

<script setup lang="ts">
import { computed, nextTick, useTemplateRef, watch } from 'vue'

import AppIcon from './AppIcon.vue'

const props = withDefaults(
  defineProps<{
    page: number
    pageSize: number
    totalCount: number
    noun?: string
    nounSingular?: string
  }>(),
  { noun: 'items', nounSingular: 'item' },
)

const emit = defineEmits<{
  change: [page: number]
}>()

const previousButton = useTemplateRef<HTMLButtonElement>('previousButton')
const nextButton = useTemplateRef<HTMLButtonElement>('nextButton')

const pageCount = computed(() => Math.max(Math.ceil(props.totalCount / props.pageSize), 1))

const hasPrevious = computed(() => props.page > 1)

const hasNext = computed(() => props.page < pageCount.value)

const summary = computed(() =>
  rangeSummary(props.page, props.pageSize, props.totalCount, props.noun, props.nounSingular),
)

let pendingFocus: 'previous' | 'next' | null = null

function go(direction: 'previous' | 'next'): void {
  pendingFocus = direction
  emit('change', direction === 'previous' ? props.page - 1 : props.page + 1)
}

watch(
  () => props.page,
  async () => {
    const moved = pendingFocus
    pendingFocus = null
    await nextTick()
    if (moved === 'next' && !hasNext.value && hasPrevious.value) {
      previousButton.value?.focus()
    } else if (moved === 'previous' && !hasPrevious.value && hasNext.value) {
      nextButton.value?.focus()
    }
  },
)
</script>

<template>
  <div class="pagination">
    <p class="pagination__summary">{{ summary }}</p>
    <nav class="pagination__nav" aria-label="Pagination">
      <button
        ref="previousButton"
        type="button"
        class="pagination__button"
        :disabled="!hasPrevious"
        @click="go('previous')"
      >
        <AppIcon name="chevron-left" :size="16" />Previous
      </button>
      <button
        ref="nextButton"
        type="button"
        class="pagination__button"
        :disabled="!hasNext"
        @click="go('next')"
      >
        Next<AppIcon name="chevron-right" :size="16" />
      </button>
    </nav>
  </div>
</template>

<style scoped>
.pagination {
  display: flex;
  flex: 1;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: var(--spacing-fs-12);
}

.pagination__summary {
  margin: 0;
  font-variant-numeric: tabular-nums;
}

.pagination__nav {
  display: flex;
  flex-wrap: wrap;
  gap: var(--spacing-fs-8);
}

.pagination__button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: var(--spacing-fs-6);
  min-height: 2rem;
  padding: 0 var(--spacing-fs-12);
  border: 1px solid var(--color-fs-secondary-border);
  border-radius: var(--radius-fs-control-sm);
  background: var(--color-fs-secondary-bg);
  -webkit-backdrop-filter: blur(var(--blur-fs-dark));
  backdrop-filter: blur(var(--blur-fs-dark));
  box-shadow: var(--shadow-fs-secondary);
  color: var(--color-fs-ink);
  font-family: inherit;
  font-size: var(--text-fs-button-sm);
  font-weight: var(--text-fs-button-sm--font-weight);
  line-height: 1;
  white-space: nowrap;
  cursor: pointer;
  transition: background-color var(--fs-duration-hover);
}

.pagination__button:hover {
  background: var(--color-fs-secondary-hover);
}

.pagination__button:not(:disabled) .app-icon {
  color: var(--color-fs-teal-700);
}

.pagination__button:disabled {
  border-color: transparent;
  background: var(--color-fs-disabled);
  -webkit-backdrop-filter: none;
  backdrop-filter: none;
  box-shadow: none;
  color: var(--color-fs-disabled-text);
  cursor: not-allowed;
}

@media (forced-colors: active) {
  .pagination__button {
    border-color: ButtonText;
  }

  .pagination__button:disabled {
    border-color: GrayText;
    color: GrayText;
  }
}
</style>
