<script setup lang="ts">
import { computed, useTemplateRef } from 'vue'
import { RouterLink } from 'vue-router'

import type { ApiError } from '@/shared/api/api-error'
import { formatJobNumber } from '@/shared/format/job-number'
import { tradeLabel } from '@/shared/format/labels'
import { currentOrNextJob, openJobsLabel } from '@/shared/format/technician-job'
import Avatar from '@/shared/ui/Avatar.vue'
import CountCapsule from '@/shared/ui/CountCapsule.vue'
import DataTable, { type DataTableColumn, type DataTableState } from '@/shared/ui/DataTable.vue'
import EmptyState from '@/shared/ui/EmptyState.vue'
import ErrorState from '@/shared/ui/ErrorState.vue'
import SectionCard from '@/shared/ui/SectionCard.vue'
import StatusChip from '@/shared/ui/StatusChip.vue'

import type { WorkloadTechnician } from '../dashboard-api'
import type { DashboardPartState } from '../dashboard-copy'

const props = withDefaults(
  defineProps<{
    technicians: readonly WorkloadTechnician[] | null
    state: DashboardPartState
    now: Date
    error?: ApiError | null
    retried?: boolean
  }>(),
  { error: null, retried: false },
)

const emit = defineEmits<{
  retry: []
}>()

const columns: readonly DataTableColumn[] = [
  {
    key: 'technician',
    label: 'Technician',
    skeleton: ['avatar-medium', 'technician-name', 'trade'],
    skeletonLayout: 'avatar-column',
  },
  { key: 'shift', label: 'Shift', skeleton: ['shift-chip'] },
  { key: 'open', label: 'Open jobs', skeleton: ['count'] },
  { key: 'job', label: 'Current or next job', skeleton: ['next-job'] },
]

const table = useTemplateRef<{ focusRegion(): void; readonly $el: unknown }>('table')

const list = useTemplateRef<HTMLElement>('list')

const errorState = useTemplateRef<InstanceType<typeof ErrorState>>('errorState')

const rows = computed(() =>
  (props.state === 'ready' ? (props.technicians ?? []) : []).map((technician) => ({
    technician,
    trade: tradeLabel(technician.trade),
    shift: !technician.isActive
      ? ('inactive' as const)
      : technician.isOnShift
        ? ('on' as const)
        : ('off' as const),
    job: currentOrNextJob(technician, props.now),
    openLabel: openJobsLabel(technician.openJobCount),
  })),
)

type WorkloadRow = (typeof rows.value)[number]

const tableState = computed<DataTableState>(() => {
  if (props.state !== 'ready') {
    return 'loading'
  }
  return rows.value.length === 0 ? 'empty' : 'content'
})

const failureMessage = computed(() => {
  const error = props.error
  if (error !== null && error.status === 429) {
    const seconds = error.retryAfterSeconds ?? 60
    return seconds === 1 ? 'Try again in 1 second.' : `Try again in ${seconds} seconds.`
  }
  return 'Check your connection, then try again.'
})

const failureReference = computed(() => {
  const error = props.error
  return error === null || error.status === 0 || error.status === 429 ? null : error.correlationId
})

function rowKey(row: WorkloadRow): string {
  return row.technician.id
}

function focusContent(): void {
  const root = table.value?.$el
  const scroll =
    root instanceof HTMLElement ? root.querySelector<HTMLElement>('.data-table__scroll') : null
  if (scroll !== null && getComputedStyle(scroll).display === 'none' && list.value !== null) {
    list.value.focus()
    return
  }
  table.value?.focusRegion()
}

function focusRetry(): void {
  errorState.value?.focusRetry()
}

defineExpose({ focusContent, focusRetry })
</script>

<template>
  <SectionCard heading="Technician workload" class="workload">
    <div v-if="state === 'error'" class="workload__failure">
      <ErrorState
        ref="errorState"
        title="Technicians could not be loaded."
        :message="failureMessage"
        :reference="failureReference"
        :role="retried ? 'alert' : 'status'"
        @retry="emit('retry')"
      />
    </div>
    <DataTable
      v-else
      ref="table"
      caption="Technician workload"
      :columns="columns"
      :rows="rows"
      :row-key="rowKey"
      :state="tableState"
      loading-label="Loading technicians"
      :min-width="640"
      in-card
      :list-below="640"
    >
      <template #cell="{ row, column }">
        <span v-if="column.key === 'technician'" class="workload__person">
          <Avatar :name="row.technician.displayName" :size="32" />
          <span class="workload__identity">
            <span class="workload__name">{{ row.technician.displayName }}</span>
            <span class="workload__secondary">{{ row.trade }}</span>
          </span>
        </span>
        <StatusChip v-else-if="column.key === 'shift'" :shift="row.shift" />
        <CountCapsule v-else-if="column.key === 'open'" :count="row.technician.openJobCount" />
        <template v-else>
          <span v-if="row.job.kind === 'current'" class="workload__job workload__single">
            <RouterLink
              class="workload__job-link"
              :to="{ name: 'job-detail', params: { jobId: row.job.job.id } }"
              >{{ formatJobNumber(row.job.job.number) }}</RouterLink
            >
            <StatusChip :status="row.job.job.status" />
          </span>
          <span v-else-if="row.job.kind === 'next'" class="workload__next workload__single"
            >Next:
            <RouterLink
              class="workload__job-link"
              :to="{ name: 'job-detail', params: { jobId: row.job.job.id } }"
              >{{ formatJobNumber(row.job.job.number) }}</RouterLink
            >
            {{ row.job.when }}</span
          >
          <span v-else class="workload__none">None</span>
        </template>
      </template>
      <template #empty>
        <EmptyState
          icon="users"
          :heading-level="3"
          title="No technicians yet"
          body="Technician accounts appear here once they exist."
        />
      </template>
      <template #list>
        <ul
          v-if="tableState === 'content'"
          ref="list"
          class="workload__people"
          aria-label="Technician workload"
          tabindex="-1"
        >
          <li v-for="row in rows" :key="row.technician.id" class="workload__person-row">
            <div class="workload__first-line">
              <Avatar :name="row.technician.displayName" :size="32" />
              <span class="workload__identity workload__identity--grow">
                <span class="workload__name">{{ row.technician.displayName }}</span>
                <span class="workload__secondary">{{ row.trade }}</span>
              </span>
              <StatusChip class="workload__shift" :shift="row.shift" />
            </div>
            <div class="workload__details">
              <span class="workload__count"
                ><CountCapsule :count="row.technician.openJobCount" />{{ row.openLabel }}</span
              >
              <span v-if="row.job.kind === 'current'" class="workload__job">
                <RouterLink
                  class="workload__job-link"
                  :to="{ name: 'job-detail', params: { jobId: row.job.job.id } }"
                  >{{ formatJobNumber(row.job.job.number) }}</RouterLink
                >
                <StatusChip :status="row.job.job.status" />
              </span>
              <span v-else-if="row.job.kind === 'next'" class="workload__next"
                >Next:
                <RouterLink
                  class="workload__job-link"
                  :to="{ name: 'job-detail', params: { jobId: row.job.job.id } }"
                  >{{ formatJobNumber(row.job.job.number) }}</RouterLink
                >
                {{ row.job.when }}</span
              >
              <span v-else class="workload__none">None</span>
            </div>
          </li>
        </ul>
        <div v-else-if="tableState === 'loading'" class="workload__skeletons" aria-hidden="true">
          <div v-for="item in 3" :key="item" class="workload__person-row">
            <div class="workload__first-line">
              <span class="workload__block workload__block--avatar"></span>
              <span class="workload__identity workload__identity--grow">
                <span class="workload__block workload__block--name"></span>
                <span class="workload__block workload__block--trade"></span>
              </span>
              <span class="workload__block workload__block--chip"></span>
            </div>
            <div class="workload__details">
              <span class="workload__block workload__block--count"></span>
              <span class="workload__block workload__block--job"></span>
            </div>
          </div>
        </div>
        <EmptyState
          v-else
          icon="users"
          :heading-level="3"
          title="No technicians yet"
          body="Technician accounts appear here once they exist."
        />
      </template>
    </DataTable>
  </SectionCard>
</template>

<style scoped>
.workload__failure {
  padding: 0 var(--spacing-fs-8) var(--spacing-fs-8);
}

.workload__person {
  display: flex;
  align-items: center;
  gap: var(--spacing-fs-12);
}

.workload__identity {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-fs-2);
  min-width: 0;
}

.workload__name {
  color: var(--color-fs-ink);
  font-size: var(--text-fs-table);
  font-weight: 600;
  line-height: 1.3;
}

.workload__secondary {
  color: var(--color-fs-muted);
  font-size: var(--text-fs-meta);
  line-height: 1.3;
}

.workload__job {
  display: inline-flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-fs-8);
}

.workload__single {
  white-space: nowrap;
}

.workload__job.workload__single {
  flex-wrap: nowrap;
}

.workload__job-link {
  color: var(--color-fs-teal-700);
  font-family: var(--font-mono);
  font-size: var(--text-fs-mono);
  font-weight: var(--text-fs-mono--font-weight);
  letter-spacing: var(--text-fs-mono--letter-spacing);
  font-variant-numeric: tabular-nums;
  text-underline-offset: 3px;
  white-space: nowrap;
}

.workload__next {
  color: var(--color-fs-ink-2);
  font-size: var(--text-fs-table);
  font-variant-numeric: tabular-nums;
}

.workload__none {
  color: var(--color-fs-muted);
}

.workload__people,
.workload__skeletons {
  margin: 0;
  padding: 0;
  list-style: none;
}

.workload__people:focus {
  outline: none;
}

.workload__people:focus-visible {
  outline: 2px solid var(--color-fs-focus);
  outline-offset: -2px;
}

.workload__person-row {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-fs-8);
  padding: var(--spacing-fs-14) var(--spacing-fs-20);
  border-bottom: 1px solid var(--color-fs-hairline-soft);
}

.workload__person-row:last-child {
  border-bottom: 0;
}

.workload__first-line {
  display: grid;
  grid-template-columns: 32px minmax(0, 1fr) auto;
  column-gap: var(--spacing-fs-12);
  row-gap: var(--spacing-fs-2);
  align-items: start;
}

.workload__first-line > :first-child {
  grid-row: 1 / span 2;
}

.workload__first-line > .workload__identity--grow {
  display: contents;
}

.workload__first-line .workload__name,
.workload__first-line .workload__block--name {
  grid-column: 2;
  grid-row: 1;
}

.workload__first-line .workload__shift,
.workload__first-line .workload__block--chip {
  grid-column: 3;
  grid-row: 1;
}

.workload__first-line .workload__secondary,
.workload__first-line .workload__block--trade {
  grid-column: 2 / -1;
  grid-row: 2;
}

.workload__details {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: var(--spacing-fs-6);
  padding-left: 44px;
  color: var(--color-fs-ink-2);
  font-size: var(--text-fs-meta);
  line-height: var(--text-fs-meta--line-height);
}

.workload__count {
  display: inline-flex;
  align-items: center;
  gap: var(--spacing-fs-6);
}

.workload__details .workload__next {
  font-size: var(--text-fs-meta);
}

.workload__block {
  display: block;
  max-width: 100%;
  border-radius: var(--radius-fs-pill);
  background: var(--fs-skeleton);
}

.workload__block--avatar {
  flex: none;
  width: 32px;
  height: 32px;
}

.workload__block--name {
  width: 120px;
  height: 14px;
}

.workload__block--trade {
  width: 72px;
  height: 12px;
}

.workload__block--chip {
  flex: none;
  width: 80px;
  height: 26px;
}

.workload__block--count {
  width: 26px;
  height: 24px;
}

.workload__block--job {
  width: 140px;
  height: 14px;
}

@media (forced-colors: active) {
  .workload__block {
    border: 1px solid CanvasText;
  }
}
</style>
