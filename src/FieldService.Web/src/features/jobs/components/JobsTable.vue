<script setup lang="ts">
import { useTemplateRef } from 'vue'
import { RouterLink } from 'vue-router'

import { formatScheduleDay, formatTimeWindow, formatUpdated } from '@/shared/format/date-time'
import { formatJobNumber } from '@/shared/format/job-number'
import Avatar from '@/shared/ui/Avatar.vue'
import DataTable, { type DataTableColumn, type DataTableState } from '@/shared/ui/DataTable.vue'
import PriorityLabel from '@/shared/ui/PriorityLabel.vue'
import StatusChip from '@/shared/ui/StatusChip.vue'

import type { JobSummary } from '../jobs-api'

withDefaults(
  defineProps<{
    jobs: readonly JobSummary[]
    state: DataTableState
    busy?: boolean
    now: Date
    flashedIds?: ReadonlySet<string>
  }>(),
  { busy: false, flashedIds: () => new Set<string>() },
)

const columns: readonly DataTableColumn[] = [
  { key: 'job', label: 'Job', skeleton: ['job-number', 'job-title'] },
  { key: 'customer', label: 'Customer and site', skeleton: ['customer', 'site'] },
  { key: 'priority', label: 'Priority', skeleton: ['short'] },
  { key: 'scheduled', label: 'Scheduled', skeleton: ['schedule'] },
  { key: 'technician', label: 'Technician', skeleton: ['avatar', 'name'], skeletonLayout: 'row' },
  { key: 'status', label: 'Status', skeleton: ['chip'] },
  { key: 'updated', label: 'Updated', skeleton: ['short'] },
]

function jobKey(job: JobSummary): string {
  return job.id
}

const table = useTemplateRef<{ focusRegion(): void }>('table')

function focusRegion(): void {
  table.value?.focusRegion()
}

defineExpose({ focusRegion })
</script>

<template>
  <DataTable
    ref="table"
    caption="Jobs"
    :columns="columns"
    :rows="jobs"
    :row-key="jobKey"
    :state="state"
    :busy="busy"
    loading-label="Loading jobs"
    :min-width="980"
    :updated-keys="flashedIds"
  >
    <template #cell="{ row, column }">
      <span v-if="column.key === 'job'" class="jobs-table__job">
        <span class="jobs-table__number">{{ formatJobNumber(row.number) }}</span>
        <RouterLink
          class="jobs-table__title"
          :to="{ name: 'job-detail', params: { jobId: row.id } }"
          >{{ row.title }}</RouterLink
        >
      </span>
      <span v-else-if="column.key === 'customer'" class="jobs-table__stack">
        <span>{{ row.customerName }}</span>
        <span class="jobs-table__secondary">{{ row.siteAddress }}</span>
      </span>
      <PriorityLabel v-else-if="column.key === 'priority'" :priority="row.priority" />
      <span v-else-if="column.key === 'scheduled'" class="jobs-table__stack jobs-table__schedule">
        <span>{{ formatScheduleDay(row.scheduledStart, now) }}</span>
        <span>{{ formatTimeWindow(row.scheduledStart, row.scheduledEnd, now) }}</span>
      </span>
      <template v-else-if="column.key === 'technician'">
        <span v-if="row.assignedTechnician" class="jobs-table__technician">
          <Avatar :name="row.assignedTechnician.displayName" :size="28" />
          <span>{{ row.assignedTechnician.displayName }}</span>
        </span>
        <span v-else class="jobs-table__unassigned">Unassigned</span>
      </template>
      <StatusChip v-else-if="column.key === 'status'" :status="row.status" />
      <time
        v-else-if="column.key === 'updated' && flashedIds.has(row.id)"
        class="jobs-table__just-now"
        :datetime="row.updatedAt"
        ><span class="jobs-table__live-dot" aria-hidden="true"></span>Just now</time
      >
      <time
        v-else-if="column.key === 'updated'"
        class="jobs-table__updated"
        :datetime="row.updatedAt"
        >{{ formatUpdated(row.updatedAt, now) }}</time
      >
    </template>
    <template #empty>
      <slot name="empty" />
    </template>
    <template #footer>
      <slot name="footer" />
    </template>
  </DataTable>
</template>

<style scoped>
.jobs-table__job,
.jobs-table__stack {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-fs-2);
}

.jobs-table__job {
  min-width: 11.25rem;
}

.jobs-table__number {
  color: var(--color-fs-muted);
  font-family: var(--font-mono);
  font-size: var(--text-fs-mono);
  font-weight: var(--text-fs-mono--font-weight);
  font-variant-numeric: tabular-nums;
}

.jobs-table__title {
  color: var(--color-fs-ink);
  font-weight: 600;
  text-decoration: none;
}

.jobs-table__title:hover {
  color: var(--color-fs-teal-700);
  text-decoration: underline;
}

.jobs-table__stack {
  white-space: nowrap;
}

.jobs-table__secondary {
  color: var(--color-fs-muted);
  font-size: var(--text-fs-meta);
}

.jobs-table__unassigned {
  color: var(--color-fs-muted);
}

.jobs-table__schedule {
  gap: 0;
  font-variant-numeric: tabular-nums;
}

.jobs-table__technician {
  display: flex;
  align-items: center;
  gap: var(--spacing-fs-10);
  white-space: nowrap;
}

.jobs-table__updated {
  color: var(--color-fs-muted);
  font-family: var(--font-mono);
  font-size: var(--text-fs-mono);
  font-weight: var(--text-fs-mono--font-weight);
  letter-spacing: var(--text-fs-mono--letter-spacing);
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}

.jobs-table__just-now {
  display: inline-flex;
  align-items: center;
  gap: var(--spacing-fs-6);
  color: var(--color-fs-just-now);
  font-weight: 600;
  white-space: nowrap;
}

.jobs-table__live-dot {
  flex: none;
  width: 8px;
  height: 8px;
  border-radius: var(--radius-fs-pill);
  background: var(--fs-gradient-accent);
  box-shadow: var(--shadow-fs-live-dot);
}
</style>
