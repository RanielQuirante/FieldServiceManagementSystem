<script setup lang="ts">
import { computed, onBeforeUnmount, shallowRef } from 'vue'
import { useRoute } from 'vue-router'

import { usePageTitle } from '@/shared/composables/use-page-title'
import { formatMoment } from '@/shared/format/date-time'
import { formatJobNumber } from '@/shared/format/job-number'
import AppButton from '@/shared/ui/AppButton.vue'
import EmptyState from '@/shared/ui/EmptyState.vue'
import ErrorState from '@/shared/ui/ErrorState.vue'
import LoadingState from '@/shared/ui/LoadingState.vue'
import PageHeader from '@/shared/ui/PageHeader.vue'
import PriorityLabel from '@/shared/ui/PriorityLabel.vue'
import SectionCard from '@/shared/ui/SectionCard.vue'
import StatusChip from '@/shared/ui/StatusChip.vue'

import JobDetailsCard from '../components/JobDetailsCard.vue'
import StatusTimeline from '../components/StatusTimeline.vue'
import { useJobsStore } from '../jobs-store'

const route = useRoute()

const store = useJobsStore()

const jobId = computed(() => {
  const value = route.params.jobId
  return Array.isArray(value) ? (value[0] ?? '') : (value ?? '')
})

const now = shallowRef(new Date())

const job = computed(() => store.job)

const retried = shallowRef(false)

usePageTitle(() => {
  if (store.jobStatus === 'ready' && job.value !== null) {
    return formatJobNumber(job.value.number)
  }
  return store.jobStatus === 'not-found' ? 'Job not found' : null
})

async function open(): Promise<void> {
  await store.openJob(jobId.value)
  now.value = new Date()
}

function retry(): void {
  retried.value = true
  void store.reloadJob(jobId.value).then(() => {
    now.value = new Date()
  })
}

void open()

onBeforeUnmount(() => {
  store.closeJob()
})
</script>

<template>
  <template v-if="store.jobStatus === 'ready' && job">
    <PageHeader :title="job.title" :job-number="formatJobNumber(job.number)">
      <template #meta>
        <StatusChip :status="job.status" />
        <PriorityLabel :priority="job.priority" />
        <span
          >Created {{ formatMoment(job.createdAt, now) }} by {{ job.createdBy.displayName }}</span
        >
      </template>
    </PageHeader>
    <div class="job-detail">
      <div class="job-detail__main">
        <JobDetailsCard :job="job" :now="now" />
        <SectionCard heading="Description">
          <p class="job-detail__description">{{ job.description }}</p>
        </SectionCard>
        <SectionCard heading="Status history">
          <div class="job-detail__history">
            <StatusTimeline :history="job.history" :now="now" />
          </div>
        </SectionCard>
      </div>
    </div>
  </template>
  <template v-else-if="store.jobStatus === 'not-found'">
    <PageHeader title="Job not found" />
    <SectionCard>
      <EmptyState
        icon="search"
        title="No job matches this link"
        body="The link may be mistyped or out of date. Every job, cancelled ones included, is in the jobs list."
      >
        <template #actions>
          <AppButton variant="secondary" icon="arrow-left" :to="{ name: 'jobs' }"
            >Back to jobs</AppButton
          >
        </template>
      </EmptyState>
    </SectionCard>
  </template>
  <template v-else-if="store.jobStatus === 'error'">
    <PageHeader title="Job details" />
    <ErrorState
      title="The job could not be loaded."
      message="Check your connection, then try again."
      :reference="store.jobError?.correlationId ?? null"
      :role="retried ? 'alert' : 'status'"
      @retry="retry"
    />
  </template>
  <template v-else>
    <PageHeader title="Job details" loading />
    <div class="job-detail" aria-hidden="true">
      <div class="job-detail__main">
        <SectionCard>
          <div class="job-detail__skeleton">
            <LoadingState
              :shapes="['heading', 'line', 'line-medium', 'line', 'line-short']"
              layout="stack"
            />
          </div>
        </SectionCard>
        <SectionCard>
          <div class="job-detail__skeleton">
            <LoadingState :shapes="['heading', 'line', 'line-medium']" layout="stack" />
          </div>
        </SectionCard>
      </div>
    </div>
    <LoadingState label="Loading job" />
  </template>
</template>

<style scoped>
.job-detail {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-start;
  gap: var(--spacing-fs-24);
}

.job-detail__main {
  display: flex;
  flex: 999 1 520px;
  flex-direction: column;
  gap: var(--spacing-fs-16);
  min-width: 0;
}

.job-detail__description {
  margin: 0;
  padding: 0 var(--spacing-fs-16) var(--spacing-fs-16);
  color: var(--color-fs-ink);
  font-size: var(--text-fs-body);
  line-height: var(--text-fs-body--line-height);
  overflow-wrap: anywhere;
  white-space: pre-line;
  text-wrap: pretty;
}

.job-detail__history {
  padding: var(--spacing-fs-4) var(--spacing-fs-16) var(--spacing-fs-16);
}

.job-detail__skeleton {
  padding: var(--spacing-fs-16);
}
</style>
