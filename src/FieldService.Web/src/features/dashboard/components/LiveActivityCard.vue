<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink } from 'vue-router'

import { formatJobNumber } from '@/shared/format/job-number'
import Avatar from '@/shared/ui/Avatar.vue'
import CountCapsule from '@/shared/ui/CountCapsule.vue'
import EmptyState from '@/shared/ui/EmptyState.vue'
import SectionCard from '@/shared/ui/SectionCard.vue'
import StatusChip from '@/shared/ui/StatusChip.vue'

import type { DashboardSummary } from '../dashboard-api'
import {
  activityNote,
  activitySentence,
  activityTime,
  type DashboardPartState,
} from '../dashboard-copy'

const props = withDefaults(
  defineProps<{
    summary: DashboardSummary | null
    state: DashboardPartState
    now: Date
    newIds?: ReadonlySet<string>
  }>(),
  { newIds: () => new Set<string>() },
)

const ready = computed(() => (props.state === 'ready' ? props.summary : null))

const entries = computed(() =>
  (ready.value?.recentActivity ?? []).map((entry) => ({
    entry,
    sentence: activitySentence(entry),
    note: activityNote(entry),
    time: activityTime(entry, props.now),
    jobNumber: formatJobNumber(entry.jobNumber),
    isNew: props.newIds.has(entry.id),
  })),
)
</script>

<template>
  <SectionCard heading="Live activity" class="live-activity">
    <div class="live-activity__body">
      <template v-if="ready">
        <ol v-if="entries.length > 0" class="live-activity__list">
          <li
            v-for="item in entries"
            :key="item.entry.id"
            :class="['live-activity__row', { 'live-activity__row--new': item.isNew }]"
            :data-entry-id="item.entry.id"
          >
            <Avatar :name="item.entry.actor.displayName" :size="32" />
            <div class="live-activity__text">
              <p class="live-activity__sentence">
                <span class="live-activity__actor">{{ item.sentence.actor }}</span
                >{{ item.sentence.before
                }}<RouterLink
                  class="live-activity__job"
                  :to="{ name: 'job-detail', params: { jobId: item.entry.jobId } }"
                  >{{ item.jobNumber }}</RouterLink
                >{{ item.sentence.after }}
              </p>
              <p v-if="item.note" class="live-activity__note">{{ item.note }}</p>
              <span class="live-activity__meta">
                <time class="live-activity__time" :datetime="item.entry.occurredAt">{{
                  item.time
                }}</time>
                <CountCapsule v-if="item.isNew" variant="new" />
                <StatusChip
                  class="live-activity__chip live-activity__chip--inline"
                  :status="item.entry.toStatus"
                />
              </span>
            </div>
            <StatusChip
              class="live-activity__chip live-activity__chip--end"
              :status="item.entry.toStatus"
            />
          </li>
        </ol>
        <EmptyState
          v-else
          icon="activity"
          :heading-level="3"
          title="No activity yet today"
          body="New jobs, assignments and status changes appear here as they happen."
        />
      </template>
      <div v-else-if="state === 'loading'" class="live-activity__skeletons" aria-hidden="true">
        <div v-for="row in 4" :key="row" class="live-activity__row">
          <span class="live-activity__block live-activity__block--avatar"></span>
          <span class="live-activity__skeleton-text">
            <span class="live-activity__block live-activity__block--sentence"></span>
            <span class="live-activity__block live-activity__block--time"></span>
          </span>
          <span class="live-activity__block live-activity__block--chip"></span>
        </div>
      </div>
      <p v-else class="live-activity__failure">Activity could not be loaded.</p>
    </div>
  </SectionCard>
</template>

<style scoped>
.live-activity__body {
  container-type: inline-size;
}

.live-activity__list,
.live-activity__skeletons {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-fs-4);
  margin: 0;
  padding: 0 0 var(--spacing-fs-4);
  list-style: none;
}

.live-activity__row {
  display: grid;
  grid-template-columns: 32px minmax(0, 1fr) auto;
  align-items: start;
  gap: var(--spacing-fs-14);
  padding: var(--spacing-fs-10) var(--spacing-fs-12);
  border-radius: var(--radius-fs-control);
}

.live-activity__row--new {
  background: var(--fs-flash-row);
}

.live-activity__text {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-fs-2);
  min-width: 0;
}

.live-activity__sentence {
  margin: 0;
  color: var(--color-fs-ink);
  font-size: var(--text-fs-table);
  line-height: var(--text-fs-table--line-height);
  overflow-wrap: anywhere;
  text-wrap: pretty;
}

.live-activity__actor {
  font-weight: 600;
}

.live-activity__job {
  color: var(--color-fs-teal-700);
  font-family: var(--font-mono);
  font-size: var(--text-fs-mono);
  font-weight: var(--text-fs-mono--font-weight);
  letter-spacing: var(--text-fs-mono--letter-spacing);
  font-variant-numeric: tabular-nums;
  text-underline-offset: 3px;
  white-space: nowrap;
}

.live-activity__note {
  display: -webkit-box;
  margin: 0;
  overflow: hidden;
  color: var(--color-fs-ink-2);
  font-size: var(--text-fs-meta);
  line-height: var(--text-fs-meta--line-height);
  overflow-wrap: anywhere;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 2;
  line-clamp: 2;
}

.live-activity__meta {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-fs-8);
}

.live-activity__time {
  color: var(--color-fs-muted);
  font-family: var(--font-mono);
  font-size: var(--text-fs-mono-sm);
  font-weight: var(--text-fs-mono-sm--font-weight);
  line-height: var(--text-fs-mono-sm--line-height);
  letter-spacing: var(--text-fs-mono-sm--letter-spacing);
  font-variant-numeric: tabular-nums;
}

.live-activity__row .live-activity__chip--inline {
  display: none;
}

.live-activity__skeleton-text {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-fs-6);
  min-width: 0;
}

.live-activity__block {
  display: block;
  max-width: 100%;
  border-radius: var(--radius-fs-pill);
  background: var(--fs-skeleton);
}

.live-activity__block--avatar {
  width: 32px;
  height: 32px;
}

.live-activity__block--sentence {
  width: 70%;
  height: 14px;
}

.live-activity__block--time {
  width: 56px;
  height: 12px;
}

.live-activity__block--chip {
  width: 96px;
  height: 26px;
}

.live-activity__failure {
  margin: 0;
  padding: 0 var(--spacing-fs-16) var(--spacing-fs-16);
  color: var(--color-fs-ink-2);
  font-size: var(--text-fs-table);
  line-height: var(--text-fs-table--line-height);
}

@container (width < 422px) {
  .live-activity__row {
    grid-template-columns: 32px minmax(0, 1fr);
  }

  .live-activity__row .live-activity__chip--end,
  .live-activity__row .live-activity__block--chip {
    display: none;
  }

  .live-activity__row .live-activity__chip--inline {
    display: inline-flex;
  }
}

@media (forced-colors: active) {
  .live-activity__row--new {
    background: none;
  }

  .live-activity__block {
    border: 1px solid CanvasText;
  }
}
</style>
