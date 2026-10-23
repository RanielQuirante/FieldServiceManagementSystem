<script setup lang="ts">
import { computed } from 'vue'

import DataTable, { type DataTableColumn } from '@/shared/ui/DataTable.vue'
import LoadingState from '@/shared/ui/LoadingState.vue'
import SectionCard from '@/shared/ui/SectionCard.vue'
import StatusChip from '@/shared/ui/StatusChip.vue'

import { inspectionHeading, reportRows, type ReportRow } from '../inspection-report'
import type { Inspection } from '../jobs-api'

const props = withDefaults(
  defineProps<{
    inspection?: Inspection | null
  }>(),
  { inspection: null },
)

const columns: readonly DataTableColumn[] = [
  { key: 'number', label: '#', skeleton: ['item-number'] },
  { key: 'item', label: 'Item', skeleton: ['item-prompt'] },
  { key: 'result', label: 'Result', skeleton: ['result-chip'] },
  { key: 'reading', label: 'Reading', skeleton: ['reading'] },
  { key: 'note', label: 'Note', skeleton: ['note'] },
]

const rows = computed<readonly ReportRow[]>(() =>
  props.inspection === null ? [] : reportRows(props.inspection),
)

function rowKey(row: ReportRow): string {
  return row.id
}
</script>

<template>
  <SectionCard v-if="inspection" :heading="inspectionHeading(inspection)">
    <DataTable
      caption="Inspection results"
      :columns="columns"
      :rows="rows"
      :row-key="rowKey"
      :min-width="640"
      in-card
    >
      <template #cell="{ row, column }">
        <span v-if="column.key === 'number'" class="inspection-results__number">{{
          row.number
        }}</span>
        <span v-else-if="column.key === 'item'" class="inspection-results__prompt">{{
          row.prompt
        }}</span>
        <template v-else-if="column.key === 'result'">
          <StatusChip v-if="row.result" :result="row.result" />
          <span v-else class="inspection-results__not-answered">Not answered</span>
        </template>
        <template v-else-if="column.key === 'reading'">
          <span v-if="row.readings.length > 0" class="inspection-results__readings">
            <span
              v-for="reading in row.readings"
              :key="reading.id"
              class="inspection-results__reading"
              ><span class="inspection-results__reading-label">{{ reading.label }}</span
              >{{ ' '
              }}<span class="inspection-results__reading-value">{{ reading.value }}</span></span
            >
          </span>
          <span v-else class="inspection-results__none">—</span>
        </template>
        <template v-else-if="column.key === 'note'">
          <span v-if="row.note" class="inspection-results__note">{{ row.note }}</span>
          <span v-else class="inspection-results__none">—</span>
        </template>
      </template>
    </DataTable>
  </SectionCard>
  <SectionCard v-else>
    <div class="inspection-results__loading-header">
      <LoadingState :shapes="['heading']" />
    </div>
    <DataTable
      caption="Inspection results"
      :columns="columns"
      :rows="rows"
      :row-key="rowKey"
      :min-width="640"
      state="loading"
      :loading-label="null"
      in-card
    />
  </SectionCard>
</template>

<style scoped>
.inspection-results__number {
  color: var(--color-fs-muted);
  font-family: var(--font-mono);
  font-size: var(--text-fs-mono);
  font-weight: var(--text-fs-mono--font-weight);
  letter-spacing: var(--text-fs-mono--letter-spacing);
  font-variant-numeric: tabular-nums;
}

.inspection-results__prompt {
  font-weight: 600;
  overflow-wrap: anywhere;
}

.inspection-results__not-answered {
  color: var(--color-fs-muted);
  font-weight: 500;
  white-space: nowrap;
}

.inspection-results__readings {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-fs-6);
}

.inspection-results__reading {
  display: flex;
  flex-direction: column;
}

.inspection-results__reading-label {
  color: var(--color-fs-muted);
  font-size: var(--text-fs-meta);
  line-height: var(--text-fs-meta--line-height);
}

.inspection-results__reading-value {
  color: var(--color-fs-ink);
  font-family: var(--font-mono);
  font-size: var(--text-fs-mono);
  font-weight: var(--text-fs-mono--font-weight);
  letter-spacing: var(--text-fs-mono--letter-spacing);
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}

.inspection-results__note {
  color: var(--color-fs-ink-2);
  white-space: pre-line;
  overflow-wrap: anywhere;
  text-wrap: pretty;
}

.inspection-results__none {
  color: var(--color-fs-muted);
}

.inspection-results__loading-header {
  padding: var(--spacing-fs-14) var(--spacing-fs-16) var(--spacing-fs-12);
}
</style>
