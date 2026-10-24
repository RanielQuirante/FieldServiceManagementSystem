<script setup lang="ts">
import { computed } from 'vue'

import { formatMomentUnbroken, formatScheduledUnbroken, separator } from '@/shared/format/date-time'
import { tradeLabel } from '@/shared/format/labels'
import Avatar from '@/shared/ui/Avatar.vue'
import SectionCard from '@/shared/ui/SectionCard.vue'

import type { JobDetails } from '../jobs-api'

const props = withDefaults(
  defineProps<{
    job: JobDetails
    now: Date
    variant?: 'page' | 'report'
  }>(),
  { variant: 'page' },
)

const isReport = computed(() => props.variant === 'report')

const created = computed(
  () =>
    `${formatMomentUnbroken(props.job.createdAt, props.now)} by ${props.job.createdBy.displayName}`,
)

const assignedSince = computed(() =>
  props.job.assignedAt === null
    ? null
    : `Assigned ${formatMomentUnbroken(props.job.assignedAt, props.now)}`,
)

const hasContact = computed(() => props.job.contactName !== null || props.job.contactPhone !== null)
</script>

<template>
  <SectionCard heading="Job details">
    <dl :class="['job-details', { 'job-details--report': isReport }]">
      <div class="job-details__row">
        <dt>Customer</dt>
        <dd>{{ job.customerName }}</dd>
      </div>
      <div class="job-details__row">
        <dt>Site</dt>
        <dd>{{ job.siteAddress }}</dd>
      </div>
      <div v-if="job.siteAccessNotes" class="job-details__row">
        <dt>Access</dt>
        <dd class="job-details__prose">{{ job.siteAccessNotes }}</dd>
      </div>
      <div v-if="hasContact" class="job-details__row">
        <dt>Contact</dt>
        <dd>
          <span v-if="job.contactName" class="job-details__person">
            <span>{{ job.contactName }}</span>
            <span v-if="job.contactPhone" class="job-details__secondary job-details__phone">{{
              job.contactPhone
            }}</span>
          </span>
          <span v-else class="job-details__phone">{{ job.contactPhone }}</span>
        </dd>
      </div>
      <div class="job-details__row">
        <dt>Scheduled</dt>
        <dd class="job-details__numeric">
          {{ formatScheduledUnbroken(job.scheduledStart, job.scheduledEnd, now) }}
        </dd>
      </div>
      <div v-if="job.equipment" class="job-details__row">
        <dt>Equipment</dt>
        <dd>{{ job.equipment }}</dd>
      </div>
      <div class="job-details__row">
        <dt>Technician</dt>
        <dd>
          <span v-if="job.assignedTechnician && isReport" class="job-details__technician">
            <Avatar :name="job.assignedTechnician.displayName" :size="28" />
            <span>{{ job.assignedTechnician.displayName }}</span>
          </span>
          <span v-else-if="job.assignedTechnician" class="job-details__technician">
            <Avatar :name="job.assignedTechnician.displayName" :size="28" />
            <span class="job-details__person">
              <span>{{ job.assignedTechnician.displayName }}</span>
              <span class="job-details__secondary"
                >{{ tradeLabel(job.assignedTechnician.trade)
                }}<template v-if="assignedSince">{{
                  `${separator}${assignedSince}`
                }}</template></span
              >
            </span>
          </span>
          <span v-else class="job-details__muted">Unassigned</span>
        </dd>
      </div>
      <div class="job-details__row">
        <dt>Checklist</dt>
        <dd>{{ job.checklistTemplate.name }}</dd>
      </div>
      <div v-if="!isReport" class="job-details__row">
        <dt>Created</dt>
        <dd class="job-details__numeric">{{ created }}</dd>
      </div>
    </dl>
  </SectionCard>
</template>

<style scoped>
.job-details {
  display: flex;
  flex-direction: column;
  margin: 0;
  padding: 0 var(--spacing-fs-16) var(--spacing-fs-8);
  container-type: inline-size;
}

.job-details__row {
  display: grid;
  grid-template-columns: 148px minmax(0, 1fr);
  gap: var(--spacing-fs-16);
  padding: var(--spacing-fs-12) 0;
  border-top: 1px solid var(--color-fs-hairline-soft);
}

dt {
  color: var(--color-fs-muted);
  font-size: var(--text-fs-meta);
  font-weight: 500;
  line-height: 1.5;
}

dd {
  min-width: 0;
  margin: 0;
  color: var(--color-fs-ink);
  font-size: var(--text-fs-table);
  line-height: 1.5;
  overflow-wrap: anywhere;
}

.job-details__prose {
  white-space: pre-line;
  text-wrap: pretty;
}

.job-details__numeric,
.job-details__phone {
  font-variant-numeric: tabular-nums;
}

.job-details__phone {
  white-space: nowrap;
}

.job-details__technician {
  display: flex;
  align-items: center;
  gap: var(--spacing-fs-10);
}

.job-details__person {
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.job-details__secondary,
.job-details__muted {
  color: var(--color-fs-muted);
}

.job-details__secondary {
  font-size: var(--text-fs-meta);
  line-height: var(--text-fs-meta--line-height);
  font-variant-numeric: tabular-nums;
}

.job-details--report .job-details__row {
  grid-template-columns: 112px minmax(0, 1fr);
}

.job-details--report .job-details__technician {
  gap: var(--spacing-fs-8);
}

@container (max-width: 420px) {
  .job-details:not(.job-details--report) .job-details__row {
    grid-template-columns: minmax(0, 1fr);
    gap: var(--spacing-fs-4);
  }
}

@container (max-width: 280px) {
  .job-details--report .job-details__row {
    grid-template-columns: minmax(0, 1fr);
    gap: var(--spacing-fs-4);
  }
}
</style>
