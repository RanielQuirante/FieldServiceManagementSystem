<script setup lang="ts">
import { computed } from 'vue'

import { dayOffset, formatShortDate, formatTime } from '@/shared/format/date-time'
import { historyActionLabel } from '@/shared/format/labels'

import type { JobHistoryEntry } from '../jobs-api'

const props = defineProps<{
  history: readonly JobHistoryEntry[]
  now: Date
}>()

interface TimelineEntry {
  readonly id: string
  readonly occurredAt: string
  readonly day: string | null
  readonly time: string
  readonly text: string
  readonly note: string | null
  readonly newest: boolean
}

function sentence(entry: JobHistoryEntry): string {
  const actor = entry.actor.displayName
  switch (entry.action) {
    case 'Created':
      return `Created by ${actor}`
    case 'Assigned':
      return entry.technician === null
        ? `Assigned by ${actor}`
        : `Assigned to ${entry.technician.displayName}`
    case 'Started':
      return `Started by ${actor}`
    case 'PutOnHold':
      return `Put on hold by ${actor}`
    case 'Resumed':
      return `Resumed by ${actor}`
    case 'Completed':
      return `Completed by ${actor}`
    case 'Cancelled':
      return `Cancelled by ${actor}`
    default:
      return `${historyActionLabel(entry.action)} by ${actor}`
  }
}

const entries = computed<TimelineEntry[]>(() =>
  props.history.map((entry, index) => ({
    id: entry.id,
    occurredAt: entry.occurredAt,
    day:
      dayOffset(entry.occurredAt, props.now) === 0
        ? null
        : formatShortDate(entry.occurredAt, props.now),
    time: formatTime(entry.occurredAt),
    text: sentence(entry),
    note: entry.note,
    newest: index === props.history.length - 1,
  })),
)
</script>

<template>
  <ol class="status-timeline">
    <li v-for="entry in entries" :key="entry.id" class="status-timeline__entry">
      <time class="status-timeline__time" :datetime="entry.occurredAt">
        <template v-if="entry.day">
          <span>{{ entry.day }}</span>
          <span class="visually-hidden">{{ ', ' }}</span>
        </template>
        <span>{{ entry.time }}</span>
      </time>
      <span
        :class="['status-timeline__dot', { 'status-timeline__dot--newest': entry.newest }]"
        aria-hidden="true"
      ></span>
      <span class="status-timeline__text">
        <span>{{ entry.text }}</span>
        <span v-if="entry.note" class="status-timeline__note">{{ `“${entry.note}”` }}</span>
      </span>
    </li>
  </ol>
</template>

<style scoped>
.status-timeline {
  display: grid;
  grid-template-columns: minmax(72px, max-content) 10px minmax(0, 1fr);
  gap: var(--spacing-fs-12);
  margin: 0;
  padding: 0;
  list-style: none;
}

.status-timeline__entry {
  display: grid;
  grid-column: 1 / -1;
  grid-template-columns: subgrid;
  align-items: center;
}

.status-timeline__time {
  display: flex;
  flex-direction: column;
  color: var(--color-fs-muted);
  font-family: var(--font-mono);
  font-size: var(--text-fs-mono);
  font-weight: var(--text-fs-mono--font-weight);
  line-height: var(--text-fs-mono--line-height);
  letter-spacing: var(--text-fs-mono--letter-spacing);
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}

.status-timeline__dot {
  width: 10px;
  height: 10px;
  border-radius: var(--radius-fs-pill);
  background: var(--color-fs-surface);
  box-shadow: var(--shadow-fs-timeline-dot);
}

.status-timeline__dot--newest {
  background: var(--fs-gradient-primary);
  box-shadow: var(--shadow-fs-timeline-dot-current);
}

.status-timeline__text {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-fs-2);
  min-width: 0;
  color: var(--color-fs-ink);
  font-size: var(--text-fs-table);
  line-height: 1.4;
  overflow-wrap: anywhere;
}

.status-timeline__note {
  color: var(--color-fs-ink-2);
  font-size: var(--text-fs-meta);
  line-height: var(--text-fs-meta--line-height);
  white-space: pre-line;
}

@media (forced-colors: active) {
  .status-timeline__dot {
    border: 2px solid CanvasText;
  }

  .status-timeline__dot--newest {
    background: CanvasText;
  }
}
</style>
