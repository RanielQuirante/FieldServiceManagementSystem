<script setup lang="ts">
import { computed, useTemplateRef } from 'vue'
import { RouterLink } from 'vue-router'

import { separator } from '@/shared/format/date-time'
import { formatJobNumber } from '@/shared/format/job-number'
import { tradeLabel } from '@/shared/format/labels'
import {
  currentOrNextJob,
  emailParts,
  openJobsLabel,
  telephoneHref,
} from '@/shared/format/technician-job'
import AppButton from '@/shared/ui/AppButton.vue'
import AppIcon from '@/shared/ui/AppIcon.vue'
import Avatar from '@/shared/ui/Avatar.vue'
import CountCapsule from '@/shared/ui/CountCapsule.vue'
import DataTable, { type DataTableColumn, type DataTableState } from '@/shared/ui/DataTable.vue'
import EmptyState from '@/shared/ui/EmptyState.vue'
import StatusChip from '@/shared/ui/StatusChip.vue'

import { isOnShift } from '../technician-filters'
import type { Technician } from '../technicians-api'

const props = defineProps<{
  technicians: readonly Technician[]
  state: DataTableState
  noMatches: boolean
  now: Date
}>()

const emit = defineEmits<{
  clear: []
}>()

const columns: readonly DataTableColumn[] = [
  {
    key: 'technician',
    label: 'Technician',
    skeleton: ['avatar-medium', 'technician-name', 'technician-detail'],
    skeletonLayout: 'avatar-column',
  },
  { key: 'trade', label: 'Trade', skeleton: ['trade-line'] },
  { key: 'shift', label: 'Shift', skeleton: ['shift-chip'] },
  { key: 'open', label: 'Open jobs', skeleton: ['count'] },
  { key: 'job', label: 'Current or next job', skeleton: ['next-job'] },
  { key: 'phone', label: 'Phone', skeleton: ['phone'] },
]

const table = useTemplateRef<{ focusRegion(): void; readonly $el: unknown }>('table')

const list = useTemplateRef<HTMLElement>('list')

const rows = computed(() =>
  props.technicians.map((technician) => ({
    technician,
    email: emailParts(technician.email),
    trade: tradeLabel(technician.trade),
    shift: !technician.isActive
      ? ('inactive' as const)
      : isOnShift(technician)
        ? ('on' as const)
        : ('off' as const),
    job: currentOrNextJob(technician, props.now),
    openLabel: openJobsLabel(technician.openJobCount),
    telephone: telephoneHref(technician.phoneNumber),
  })),
)

type TechnicianRow = (typeof rows.value)[number]

function rowKey(row: TechnicianRow): string {
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

defineExpose({ focusContent })
</script>

<template>
  <DataTable
    ref="table"
    caption="Technicians"
    :columns="columns"
    :rows="rows"
    :row-key="rowKey"
    :state="state"
    loading-label="Loading technicians"
    :min-width="900"
    :list-below="900"
  >
    <template #cell="{ row, column }">
      <span v-if="column.key === 'technician'" class="technicians-table__person">
        <Avatar :name="row.technician.displayName" :size="32" />
        <span class="technicians-table__identity">
          <span class="technicians-table__name">{{ row.technician.displayName }}</span>
          <span class="technicians-table__email"
            ><span class="technicians-table__email-part">{{ row.email.local }}</span
            ><wbr /><span class="technicians-table__email-part">{{ row.email.domain }}</span></span
          >
        </span>
      </span>
      <span v-else-if="column.key === 'trade'" class="technicians-table__single">{{
        row.trade
      }}</span>
      <StatusChip v-else-if="column.key === 'shift'" :shift="row.shift" />
      <CountCapsule v-else-if="column.key === 'open'" :count="row.technician.openJobCount" />
      <template v-else-if="column.key === 'job'">
        <span
          v-if="row.job.kind === 'current'"
          class="technicians-table__job technicians-table__single"
        >
          <RouterLink
            class="technicians-table__job-link"
            :to="{ name: 'job-detail', params: { jobId: row.job.job.id } }"
            >{{ formatJobNumber(row.job.job.number) }}</RouterLink
          >
          <StatusChip :status="row.job.job.status" />
        </span>
        <span
          v-else-if="row.job.kind === 'next'"
          class="technicians-table__next technicians-table__single"
          >Next:
          <RouterLink
            class="technicians-table__job-link"
            :to="{ name: 'job-detail', params: { jobId: row.job.job.id } }"
            >{{ formatJobNumber(row.job.job.number) }}</RouterLink
          >
          {{ row.job.when }}</span
        >
        <span v-else class="technicians-table__none">None</span>
      </template>
      <span v-else class="technicians-table__phone">{{ row.technician.phoneNumber }}</span>
    </template>
    <template #empty>
      <EmptyState
        v-if="noMatches"
        icon="search"
        title="No technicians match these filters"
        body="Change the search or the shift filter, or clear them to see every technician."
      >
        <template #actions>
          <AppButton variant="secondary" icon="x" @click="emit('clear')">Clear filters</AppButton>
        </template>
      </EmptyState>
      <EmptyState
        v-else
        icon="users"
        title="No technicians yet"
        body="Technician accounts appear here once they exist."
      />
    </template>
    <template #list>
      <ul
        v-if="state === 'content'"
        ref="list"
        class="technicians-table__people"
        aria-label="Technicians"
        tabindex="-1"
      >
        <li v-for="row in rows" :key="row.technician.id" class="technicians-table__person-row">
          <div class="technicians-table__first-line">
            <Avatar :name="row.technician.displayName" :size="32" />
            <span class="technicians-table__identity technicians-table__identity--grow">
              <span class="technicians-table__name">{{ row.technician.displayName }}</span>
              <span class="technicians-table__email"
                ><span class="technicians-table__email-part">{{ row.email.local }}</span
                ><wbr /><span class="technicians-table__email-part">{{
                  row.email.domain
                }}</span></span
              >
            </span>
            <StatusChip class="technicians-table__shift" :shift="row.shift" />
          </div>
          <div class="technicians-table__details">
            <span class="technicians-table__count"
              >{{ row.trade }}<span aria-hidden="true">{{ separator }}</span
              ><span class="visually-hidden">{{ ', ' }}</span
              ><CountCapsule :count="row.technician.openJobCount" />{{ row.openLabel }}</span
            >
            <span v-if="row.job.kind === 'current'" class="technicians-table__job">
              <RouterLink
                class="technicians-table__job-link"
                :to="{ name: 'job-detail', params: { jobId: row.job.job.id } }"
                >{{ formatJobNumber(row.job.job.number) }}</RouterLink
              >
              <StatusChip :status="row.job.job.status" />
            </span>
            <span v-else-if="row.job.kind === 'next'" class="technicians-table__next"
              >Next:
              <RouterLink
                class="technicians-table__job-link"
                :to="{ name: 'job-detail', params: { jobId: row.job.job.id } }"
                >{{ formatJobNumber(row.job.job.number) }}</RouterLink
              >
              {{ row.job.when }}</span
            >
            <span v-else class="technicians-table__none">None</span>
            <a
              v-if="row.telephone"
              class="technicians-table__call"
              :href="row.telephone"
              :aria-label="`Call ${row.technician.displayName}, ${row.technician.phoneNumber}`"
              ><AppIcon name="phone" :size="14" />{{ row.technician.phoneNumber }}</a
            >
          </div>
        </li>
      </ul>
      <div v-else-if="state === 'loading'" class="technicians-table__skeletons" aria-hidden="true">
        <div v-for="item in 3" :key="item" class="technicians-table__person-row">
          <div class="technicians-table__first-line">
            <span class="technicians-table__block technicians-table__block--avatar"></span>
            <span class="technicians-table__identity technicians-table__identity--grow">
              <span class="technicians-table__block technicians-table__block--name"></span>
              <span class="technicians-table__block technicians-table__block--email"></span>
            </span>
            <span class="technicians-table__block technicians-table__block--chip"></span>
          </div>
          <div class="technicians-table__details">
            <span class="technicians-table__block technicians-table__block--line"></span>
            <span class="technicians-table__block technicians-table__block--phone"></span>
          </div>
        </div>
      </div>
      <template v-else>
        <EmptyState
          v-if="noMatches"
          icon="search"
          title="No technicians match these filters"
          body="Change the search or the shift filter, or clear them to see every technician."
        >
          <template #actions>
            <AppButton variant="secondary" icon="x" @click="emit('clear')">Clear filters</AppButton>
          </template>
        </EmptyState>
        <EmptyState
          v-else
          icon="users"
          title="No technicians yet"
          body="Technician accounts appear here once they exist."
        />
      </template>
    </template>
  </DataTable>
</template>

<style scoped>
.technicians-table__person {
  display: flex;
  align-items: center;
  gap: var(--spacing-fs-12);
}

.technicians-table__identity {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-fs-2);
  min-width: 0;
}

.technicians-table__name {
  color: var(--color-fs-ink);
  font-size: var(--text-fs-table);
  font-weight: 600;
  line-height: 1.2;
}

.technicians-table__email {
  color: var(--color-fs-muted);
  font-family: var(--font-mono);
  font-size: var(--text-fs-mono-sm);
  font-weight: var(--text-fs-mono-sm--font-weight);
  line-height: 1.2;
  letter-spacing: var(--text-fs-mono-sm--letter-spacing);
  overflow-wrap: normal;
  word-break: normal;
}

.technicians-table__single {
  white-space: nowrap;
}

.technicians-table__job.technicians-table__single {
  flex-wrap: nowrap;
}

.technicians-table__email-part {
  white-space: nowrap;
}

.technicians-table__job {
  display: inline-flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-fs-8);
}

.technicians-table__job-link {
  color: var(--color-fs-teal-700);
  font-family: var(--font-mono);
  font-size: var(--text-fs-mono);
  font-weight: var(--text-fs-mono--font-weight);
  letter-spacing: var(--text-fs-mono--letter-spacing);
  font-variant-numeric: tabular-nums;
  text-underline-offset: 3px;
  white-space: nowrap;
}

.technicians-table__next {
  color: var(--color-fs-ink-2);
  font-variant-numeric: tabular-nums;
}

.technicians-table__none {
  color: var(--color-fs-muted);
}

.technicians-table__phone {
  color: var(--color-fs-ink);
  font-family: var(--font-mono);
  font-size: var(--text-fs-mono);
  font-weight: var(--text-fs-mono--font-weight);
  letter-spacing: var(--text-fs-mono--letter-spacing);
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}

.technicians-table__people,
.technicians-table__skeletons {
  margin: 0;
  padding: 0;
  list-style: none;
}

.technicians-table__people:focus {
  outline: none;
}

.technicians-table__people:focus-visible {
  outline: 2px solid var(--color-fs-focus);
  outline-offset: -2px;
}

.technicians-table__person-row {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-fs-8);
  padding: var(--spacing-fs-14) var(--spacing-fs-20);
  border-bottom: 1px solid var(--color-fs-hairline-soft);
}

.technicians-table__person-row:last-child {
  border-bottom: 0;
}

.technicians-table__first-line {
  display: grid;
  grid-template-columns: 32px minmax(0, 1fr) auto;
  column-gap: var(--spacing-fs-12);
  row-gap: var(--spacing-fs-2);
  align-items: start;
}

.technicians-table__first-line > :first-child {
  grid-row: 1 / span 2;
}

.technicians-table__first-line > .technicians-table__identity--grow {
  display: contents;
}

.technicians-table__first-line .technicians-table__name,
.technicians-table__first-line .technicians-table__block--name {
  grid-column: 2;
  grid-row: 1;
}

.technicians-table__first-line .technicians-table__shift,
.technicians-table__first-line .technicians-table__block--chip {
  grid-column: 3;
  grid-row: 1;
}

.technicians-table__first-line .technicians-table__email,
.technicians-table__first-line .technicians-table__block--email {
  grid-column: 2 / -1;
  grid-row: 2;
}

.technicians-table__details {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: var(--spacing-fs-6);
  padding-left: 44px;
  color: var(--color-fs-ink-2);
  font-size: var(--text-fs-meta);
  line-height: var(--text-fs-meta--line-height);
}

.technicians-table__count {
  display: inline-flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-fs-6);
}

.technicians-table__call {
  display: inline-flex;
  align-items: center;
  gap: var(--spacing-fs-6);
  color: var(--color-fs-teal-700);
  font-family: var(--font-mono);
  font-size: var(--text-fs-mono);
  font-weight: var(--text-fs-mono--font-weight);
  font-variant-numeric: tabular-nums;
  text-underline-offset: 3px;
  white-space: nowrap;
}

.technicians-table__block {
  display: block;
  max-width: 100%;
  border-radius: var(--radius-fs-pill);
  background: var(--fs-skeleton);
}

.technicians-table__block--avatar {
  flex: none;
  width: 32px;
  height: 32px;
}

.technicians-table__block--name {
  width: 120px;
  height: 14px;
}

.technicians-table__block--email {
  width: 180px;
  height: 12px;
}

.technicians-table__block--chip {
  flex: none;
  width: 80px;
  height: 26px;
}

.technicians-table__block--line {
  width: 140px;
  height: 14px;
}

.technicians-table__block--phone {
  width: 104px;
  height: 14px;
}

@media (forced-colors: active) {
  .technicians-table__block {
    border: 1px solid CanvasText;
  }
}
</style>
