<script lang="ts">
import type { SkeletonShape } from './LoadingState.vue'

export interface DataTableColumn {
  readonly key: string
  readonly label: string
  readonly skeleton?: readonly SkeletonShape[]
  readonly skeletonLayout?: 'column' | 'row'
}

export type DataTableState = 'loading' | 'content' | 'empty'
</script>

<script setup lang="ts" generic="Row">
import { computed, useTemplateRef } from 'vue'

import LoadingState from './LoadingState.vue'

const props = withDefaults(
  defineProps<{
    caption: string
    columns: readonly DataTableColumn[]
    rows: readonly Row[]
    rowKey: (row: Row) => string
    state?: DataTableState
    busy?: boolean
    loadingLabel?: string
    minWidth?: 0 | 640 | 720 | 900 | 980
    inCard?: boolean
    skeletonRows?: number
  }>(),
  {
    state: 'content',
    busy: false,
    loadingLabel: 'Loading',
    minWidth: 0,
    inCard: false,
    skeletonRows: 5,
  },
)

defineSlots<{
  cell(props: { row: Row; column: DataTableColumn }): unknown
  empty(): unknown
  footer(): unknown
}>()

const isDense = computed(() => props.columns.length >= 7)

const isBusy = computed(() => props.state === 'loading' || props.busy)

const skeletonRowNumbers = computed(() =>
  Array.from({ length: props.skeletonRows }, (_, index) => index),
)

const region = useTemplateRef<HTMLElement>('region')

function focusRegion(): void {
  region.value?.focus()
}

defineExpose({ focusRegion })
</script>

<template>
  <div :class="['data-table', { 'data-table--in-card': inCard, 'data-table--dense': isDense }]">
    <div ref="region" class="data-table__scroll" role="region" tabindex="0" :aria-label="caption">
      <table
        :class="['data-table__table', `data-table__table--min-${minWidth}`]"
        :aria-busy="isBusy ? 'true' : undefined"
      >
        <caption class="visually-hidden">
          {{
            caption
          }}
        </caption>
        <thead>
          <tr>
            <th v-for="column in columns" :key="column.key" scope="col">{{ column.label }}</th>
          </tr>
        </thead>
        <tbody>
          <template v-if="state === 'loading'">
            <tr
              v-for="number in skeletonRowNumbers"
              :key="number"
              class="data-table__row data-table__row--skeleton"
              aria-hidden="true"
            >
              <td v-for="column in columns" :key="column.key">
                <LoadingState
                  :shapes="column.skeleton ?? ['short']"
                  :layout="column.skeletonLayout ?? 'column'"
                />
              </td>
            </tr>
          </template>
          <tr v-else-if="state === 'empty'" class="data-table__row data-table__row--empty">
            <td :colspan="columns.length" class="data-table__empty-cell">
              <div class="data-table__empty">
                <slot name="empty" />
              </div>
            </td>
          </tr>
          <template v-else>
            <tr v-for="row in rows" :key="rowKey(row)" class="data-table__row">
              <td v-for="column in columns" :key="column.key">
                <slot name="cell" :row="row" :column="column" />
              </td>
            </tr>
          </template>
        </tbody>
      </table>
    </div>
    <div v-if="$slots.footer && state === 'content'" class="data-table__footer">
      <slot name="footer" />
    </div>
    <LoadingState v-if="isBusy" :label="loadingLabel" />
  </div>
</template>

<style scoped>
.data-table {
  display: flex;
  flex-direction: column;
  min-width: 0;
  overflow: hidden;
  border: 1px solid var(--fs-glass-g1-border);
  border-radius: var(--radius-fs-card);
  background: var(--fs-glass-g1-bg);
  -webkit-backdrop-filter: var(--fs-glass-g1-filter);
  backdrop-filter: var(--fs-glass-g1-filter);
  box-shadow: var(--shadow-fs-glass);
}

.data-table--in-card {
  border: 0;
  border-radius: 0;
  background: transparent;
  -webkit-backdrop-filter: none;
  backdrop-filter: none;
  box-shadow: none;
}

.data-table__scroll {
  overflow-x: auto;
  container-type: inline-size;
}

.data-table__scroll:focus-visible {
  outline-offset: -2px;
}

.data-table__table {
  width: 100%;
  border-collapse: separate;
  border-spacing: 0;
  font-size: var(--text-fs-table);
  line-height: var(--text-fs-table--line-height);
}

.data-table__table--min-640 {
  min-width: 40rem;
}

.data-table__table--min-720 {
  min-width: 45rem;
}

.data-table__table--min-900 {
  min-width: 56.25rem;
}

.data-table__table--min-980 {
  min-width: 61.25rem;
}

th {
  padding: 12px 16px;
  border-bottom: 1px solid var(--color-fs-hairline);
  background: var(--color-fs-table-header-bg);
  color: var(--color-fs-muted);
  font-size: var(--text-fs-table-header);
  font-weight: var(--text-fs-table-header--font-weight);
  line-height: var(--text-fs-table-header--line-height);
  letter-spacing: var(--text-fs-table-header--letter-spacing);
  text-align: left;
  text-transform: uppercase;
  white-space: nowrap;
}

.data-table--in-card th {
  background: transparent;
}

td {
  padding: 14px 16px;
  border-bottom: 1px solid var(--color-fs-hairline-soft);
  color: var(--color-fs-ink);
  vertical-align: middle;
}

.data-table--dense th {
  padding: 12px 10px;
}

.data-table--dense td {
  padding: 14px 10px;
}

th:first-child,
td:first-child {
  padding-left: 20px;
}

th:last-child,
td:last-child {
  padding-right: 20px;
}

.data-table__row:last-child td {
  border-bottom: 0;
}

.data-table__row:not(.data-table__row--skeleton, .data-table__row--empty):hover td {
  background: var(--color-fs-row-hover);
}

.data-table__row--empty > .data-table__empty-cell {
  padding: 0;
}

.data-table__empty {
  position: sticky;
  left: 0;
  width: 100cqw;
}

.data-table__footer {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: var(--spacing-fs-12);
  padding: var(--spacing-fs-12) var(--spacing-fs-20);
  border-top: 1px solid var(--color-fs-hairline);
  color: var(--color-fs-muted);
  font-size: var(--text-fs-meta);
  line-height: var(--text-fs-meta--line-height);
}

@supports not ((backdrop-filter: blur(1px)) or (-webkit-backdrop-filter: blur(1px))) {
  .data-table:not(.data-table--in-card) {
    background: var(--fs-glass-g3-bg);
  }
}

@media (prefers-reduced-transparency: reduce) {
  .data-table:not(.data-table--in-card) {
    background: var(--color-fs-surface);
    -webkit-backdrop-filter: none;
    backdrop-filter: none;
  }
}

@media (forced-colors: active) {
  .data-table:not(.data-table--in-card) {
    border-color: CanvasText;
  }

  th {
    border-bottom-color: CanvasText;
  }
}
</style>
