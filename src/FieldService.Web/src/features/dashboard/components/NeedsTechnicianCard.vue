<script setup lang="ts">
import { computed } from 'vue'

import { separator } from '@/shared/format/date-time'
import { formatJobNumber } from '@/shared/format/job-number'
import AppButton from '@/shared/ui/AppButton.vue'
import CountCapsule from '@/shared/ui/CountCapsule.vue'
import EmptyState from '@/shared/ui/EmptyState.vue'
import PriorityLabel from '@/shared/ui/PriorityLabel.vue'
import SectionCard from '@/shared/ui/SectionCard.vue'

import type { DashboardSummary } from '../dashboard-api'
import { scheduleParts, viewAllLabel, type DashboardPartState } from '../dashboard-copy'

const props = defineProps<{
  summary: DashboardSummary | null
  state: DashboardPartState
  now: Date
}>()

const ready = computed(() => (props.state === 'ready' ? props.summary : null))

const unassigned = computed(() => ready.value?.openJobs.unassigned ?? 0)

const rows = computed(() =>
  (ready.value?.needsTechnician ?? []).map((job) => ({
    ...job,
    numberText: formatJobNumber(job.number),
    schedule: scheduleParts(job, props.now),
  })),
)

const capsuleWords = computed(() => (unassigned.value === 1 ? 'unassigned job' : 'unassigned jobs'))

const showsViewAll = computed(() => ready.value !== null && unassigned.value > rows.value.length)
</script>

<template>
  <SectionCard heading="Needs a technician" class="needs-technician">
    <template v-if="ready" #header-end>
      <CountCapsule :count="unassigned" :hidden-words="capsuleWords" />
    </template>
    <template v-if="ready">
      <ul v-if="rows.length > 0" class="needs-technician__list">
        <li v-for="job in rows" :key="job.id" class="needs-technician__row">
          <div class="needs-technician__text">
            <div class="needs-technician__line">
              <span class="needs-technician__number">{{ job.numberText }}</span>
              <PriorityLabel :priority="job.priority" />
            </div>
            <span class="needs-technician__title">{{ job.title }}</span>
            <span class="needs-technician__meta"
              >{{ job.customerName }}<span aria-hidden="true">{{ separator }}</span
              ><span class="visually-hidden">{{ ', ' }}</span
              ><span class="needs-technician__schedule"
                ><span class="needs-technician__schedule-part">{{ job.schedule.day }}</span
                >{{ ' '
                }}<span class="needs-technician__schedule-part">{{
                  job.schedule.window
                }}</span></span
              ></span
            >
          </div>
          <AppButton
            class="needs-technician__assign"
            variant="secondary"
            size="small"
            icon="user-check"
            :to="{ name: 'job-detail', params: { jobId: job.id } }"
            :aria-label="`Assign ${job.numberText}`"
            >Assign</AppButton
          >
        </li>
      </ul>
      <EmptyState
        v-else
        icon="check-circle"
        :heading-level="3"
        title="Every open job has a technician"
        body="Jobs without a technician appear here, earliest first."
      />
      <div v-if="showsViewAll" class="needs-technician__more">
        <AppButton
          variant="quiet"
          trailing-icon="chevron-right"
          :to="{ name: 'jobs', query: { status: 'Unassigned' } }"
          >{{ viewAllLabel(unassigned) }}</AppButton
        >
      </div>
    </template>
    <div v-else-if="state === 'loading'" class="needs-technician__skeletons" aria-hidden="true">
      <div v-for="row in 3" :key="row" class="needs-technician__skeleton">
        <span class="needs-technician__skeleton-text">
          <span class="needs-technician__block needs-technician__block--number"></span>
          <span class="needs-technician__block needs-technician__block--title"></span>
          <span class="needs-technician__block needs-technician__block--meta"></span>
        </span>
        <span class="needs-technician__block needs-technician__block--link"></span>
      </div>
    </div>
    <p v-else class="needs-technician__failure">This list could not be loaded.</p>
  </SectionCard>
</template>

<style scoped>
.needs-technician__list {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-fs-4);
  margin: 0;
  padding: 0 0 var(--spacing-fs-4);
  list-style: none;
}

.needs-technician__row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-fs-12) var(--spacing-fs-14);
  padding: var(--spacing-fs-12);
  border-radius: var(--radius-fs-control);
}

.needs-technician__text {
  display: flex;
  flex: 1 1 220px;
  flex-direction: column;
  gap: var(--spacing-fs-4);
  min-width: 0;
}

.needs-technician__line {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-fs-10);
}

.needs-technician__number {
  color: var(--color-fs-muted);
  font-family: var(--font-mono);
  font-size: var(--text-fs-mono);
  font-weight: var(--text-fs-mono--font-weight);
  line-height: var(--text-fs-mono--line-height);
  letter-spacing: var(--text-fs-mono--letter-spacing);
  font-variant-numeric: tabular-nums;
}

.needs-technician__title {
  color: var(--color-fs-ink);
  font-size: var(--text-fs-body);
  font-weight: 600;
  line-height: 1.4;
  overflow-wrap: anywhere;
  text-wrap: pretty;
}

.needs-technician__meta {
  color: var(--color-fs-muted);
  font-size: var(--text-fs-meta);
  line-height: var(--text-fs-meta--line-height);
  font-variant-numeric: tabular-nums;
  overflow-wrap: anywhere;
}

.needs-technician__schedule {
  display: inline-block;
  max-width: 100%;
}

.needs-technician__schedule-part {
  white-space: nowrap;
}

.needs-technician__assign {
  flex: none;
}

.needs-technician__more {
  display: flex;
  padding: 0 var(--spacing-fs-2) var(--spacing-fs-8);
}

.needs-technician__skeletons {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-fs-4);
  padding: 0 0 var(--spacing-fs-4);
}

.needs-technician__skeleton {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-fs-12) var(--spacing-fs-14);
  padding: var(--spacing-fs-12);
}

.needs-technician__skeleton-text {
  display: flex;
  flex: 1 1 220px;
  flex-direction: column;
  gap: var(--spacing-fs-6);
  min-width: 0;
}

.needs-technician__block {
  display: block;
  max-width: 100%;
  border-radius: var(--radius-fs-pill);
  background: var(--fs-skeleton);
}

.needs-technician__block--number {
  width: 64px;
  height: 12px;
}

.needs-technician__block--title {
  width: 220px;
  height: 15px;
}

.needs-technician__block--meta {
  width: 180px;
  height: 12px;
}

.needs-technician__block--link {
  flex: none;
  width: 84px;
  height: 32px;
  border-radius: var(--radius-fs-control-sm);
}

.needs-technician__failure {
  margin: 0;
  padding: 0 var(--spacing-fs-16) var(--spacing-fs-16);
  color: var(--color-fs-ink-2);
  font-size: var(--text-fs-table);
  line-height: var(--text-fs-table--line-height);
}

@media (forced-colors: active) {
  .needs-technician__block {
    border: 1px solid CanvasText;
  }
}
</style>
