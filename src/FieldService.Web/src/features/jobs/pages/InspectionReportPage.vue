<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, shallowRef, useTemplateRef, watch } from 'vue'
import { useRoute } from 'vue-router'

import { useLiveRefetch } from '@/features/realtime/use-live-refetch'
import { usePageTitle } from '@/shared/composables/use-page-title'
import { formatJobNumber } from '@/shared/format/job-number'
import AppButton from '@/shared/ui/AppButton.vue'
import Banner from '@/shared/ui/Banner.vue'
import EmptyState from '@/shared/ui/EmptyState.vue'
import ErrorState from '@/shared/ui/ErrorState.vue'
import LoadingState from '@/shared/ui/LoadingState.vue'
import PageHeader from '@/shared/ui/PageHeader.vue'
import PriorityLabel from '@/shared/ui/PriorityLabel.vue'
import SectionCard from '@/shared/ui/SectionCard.vue'
import StatusChip from '@/shared/ui/StatusChip.vue'

import InspectionResultsTable from '../components/InspectionResultsTable.vue'
import InspectionSummaryStrip from '../components/InspectionSummaryStrip.vue'
import JobDetailsCard from '../components/JobDetailsCard.vue'
import StatusTimeline from '../components/StatusTimeline.vue'
import {
  isTimeOnSiteRunning,
  loadFailureMessage,
  millisecondsToNextMinute,
  reportMeta,
  reportNotice,
  technicianNotes,
} from '../inspection-report'
import { useJobsStore } from '../jobs-store'

const route = useRoute()

const store = useJobsStore()

const jobId = computed(() => {
  const value = route.params.jobId
  return Array.isArray(value) ? (value[0] ?? '') : (value ?? '')
})

const now = shallowRef(new Date())

const retried = shallowRef(false)

const errorState = useTemplateRef<InstanceType<typeof ErrorState>>('errorState')

const report = computed(() => (store.reportStatus === 'ready' ? store.report : null))

const job = computed(() => report.value?.job ?? null)

const inspection = computed(() => report.value?.inspection ?? null)

const jobNumber = computed(() => (job.value === null ? null : formatJobNumber(job.value.number)))

const meta = computed(() => (job.value === null ? '' : reportMeta(job.value, now.value)))

const notice = computed(() => (job.value === null ? null : reportNotice(job.value.status)))

const notes = computed(() => (inspection.value === null ? null : technicianNotes(inspection.value)))

const notesClass = computed(() => [
  'inspection-report__notes',
  { 'inspection-report__notes--empty': notes.value?.empty === true },
])

const failureMessage = computed(() => loadFailureMessage(store.reportError))

const failureReference = computed(() => {
  const error = store.reportError
  if (error === null || error.status === 0 || error.status === 429) {
    return null
  }
  return error.correlationId
})

const live = useLiveRefetch({
  refetch: () => store.refreshReport(),
  refetchWithJob: (updated) => store.reportRefetchFor(updated),
  page: {
    refreshFailedTitle: 'The inspection report could not be refreshed.',
    refreshedLabel: 'The report was refreshed at',
    showingFrom: (time) => `Showing it as of ${time}.`,
  },
})

usePageTitle(
  () => {
    if (jobNumber.value !== null) {
      return jobNumber.value
    }
    return store.reportStatus === 'not-found' ? 'Job not found' : null
  },
  () => (jobNumber.value === null ? null : `${jobNumber.value} inspection report`),
)

let clock: ReturnType<typeof setTimeout> | undefined

function stopClock(): void {
  if (clock !== undefined) {
    clearTimeout(clock)
    clock = undefined
  }
}

function startClock(): void {
  stopClock()
  const current = job.value
  if (current === null || current.startedAt === null || !isTimeOnSiteRunning(current)) {
    return
  }
  clock = setTimeout(() => {
    clock = undefined
    now.value = new Date()
    startClock()
  }, millisecondsToNextMinute(new Date()))
}

function retry(): void {
  retried.value = true
  void store.openReport(jobId.value)
}

watch(report, (current) => {
  now.value = new Date()
  if (current !== null) {
    live.markFresh()
  }
})

watch(job, startClock)

watch(
  () => store.reportStatus,
  async (status) => {
    if (status !== 'error' || !retried.value) {
      return
    }
    await nextTick()
    errorState.value?.focusRetry()
  },
)

void store.openReport(jobId.value)

onBeforeUnmount(() => {
  stopClock()
  store.closeReport()
})
</script>

<template>
  <template v-if="job && inspection && notes">
    <PageHeader :title="job.title" :job-number="jobNumber" number-suffix="Inspection report">
      <template #meta>
        <StatusChip :status="job.status" />
        <PriorityLabel :priority="job.priority" />
        <span>{{ meta }}</span>
      </template>
      <template #actions>
        <AppButton
          variant="secondary"
          size="small"
          icon="arrow-left"
          :to="{ name: 'job-detail', params: { jobId: job.id } }"
          >Job details</AppButton
        >
      </template>
    </PageHeader>
    <Banner
      v-if="notice"
      class="inspection-report__notice"
      :role="null"
      :icon="notice.icon"
      :title="notice.title"
      :message="notice.message"
    />
    <InspectionSummaryStrip :job="job" :inspection="inspection" :now="now" />
    <div class="inspection-report">
      <div class="inspection-report__main">
        <InspectionResultsTable :inspection="inspection" />
        <SectionCard heading="Technician notes">
          <p :class="notesClass">{{ notes.text }}</p>
        </SectionCard>
      </div>
      <div class="inspection-report__side">
        <JobDetailsCard :job="job" :now="now" variant="report" />
        <SectionCard heading="Status history">
          <div class="inspection-report__history">
            <StatusTimeline :history="job.history" :now="now" />
          </div>
        </SectionCard>
      </div>
    </div>
  </template>
  <template v-else-if="store.reportStatus === 'not-found'">
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
  <template v-else-if="store.reportStatus === 'error'">
    <PageHeader title="Inspection report" />
    <ErrorState
      ref="errorState"
      title="The inspection report could not be loaded."
      :message="failureMessage"
      :reference="failureReference"
      :role="retried ? 'alert' : 'status'"
      @retry="retry"
    />
  </template>
  <template v-else>
    <PageHeader title="Inspection report" loading />
    <InspectionSummaryStrip :now="now" loading />
    <div class="inspection-report">
      <div class="inspection-report__main">
        <InspectionResultsTable />
      </div>
      <div class="inspection-report__side">
        <SectionCard heading="Job details">
          <div class="inspection-report__skeleton">
            <LoadingState :shapes="['line', 'line-medium', 'line', 'line-short']" layout="stack" />
          </div>
        </SectionCard>
        <SectionCard heading="Status history">
          <div class="inspection-report__skeleton">
            <LoadingState :shapes="['line-medium', 'line-medium', 'line-short']" layout="stack" />
          </div>
        </SectionCard>
      </div>
    </div>
    <LoadingState label="Loading inspection report" />
  </template>
</template>

<style scoped>
.inspection-report {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-start;
  gap: var(--spacing-fs-24);
}

.inspection-report__main {
  display: flex;
  flex: 999 1 600px;
  flex-direction: column;
  gap: var(--spacing-fs-16);
  min-width: 0;
}

.inspection-report__side {
  display: grid;
  flex: 1 1 380px;
  grid-template-columns: repeat(auto-fit, minmax(min(380px, 100%), 1fr));
  align-items: start;
  gap: var(--spacing-fs-16);
  min-width: 0;
}

.inspection-report__notes {
  margin: 0;
  padding: 0 var(--spacing-fs-16) var(--spacing-fs-16);
  color: var(--color-fs-ink);
  font-size: var(--text-fs-body);
  line-height: var(--text-fs-body--line-height);
  overflow-wrap: anywhere;
  white-space: pre-line;
  text-wrap: pretty;
}

.inspection-report__notes--empty {
  color: var(--color-fs-muted);
}

.inspection-report__history {
  padding: var(--spacing-fs-4) var(--spacing-fs-16) var(--spacing-fs-16);
}

.inspection-report__skeleton {
  padding: var(--spacing-fs-4) var(--spacing-fs-16) var(--spacing-fs-16);
}
</style>
