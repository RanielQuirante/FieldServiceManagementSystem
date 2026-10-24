<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, shallowRef, useTemplateRef, watch } from 'vue'
import { useRoute } from 'vue-router'

import { useLiveRefetch } from '@/features/realtime/use-live-refetch'
import { usePageTitle } from '@/shared/composables/use-page-title'
import { formatMomentUnbroken } from '@/shared/format/date-time'
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

import AssignTechnicianCard, { type JobNoticeCopy } from '../components/AssignTechnicianCard.vue'
import CancelJobDialog, { type CancelDialogResult } from '../components/CancelJobDialog.vue'
import JobDetailsCard from '../components/JobDetailsCard.vue'
import StatusTimeline from '../components/StatusTimeline.vue'
import { cancelledNotice, jobNoticeText } from '../job-cancellation'
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

const header = useTemplateRef<InstanceType<typeof PageHeader>>('header')

const notice = computed<JobNoticeCopy | null>(() => {
  const current = store.jobNotice
  if (current === null) {
    return null
  }
  return { id: current.id, ...jobNoticeText(current.code, current.jobNumber) }
})

const panelShown = computed(() => job.value?.affordances.canAssign === true)

const canCancel = computed(() => job.value?.affordances.canCancel === true)

const cancelled = computed(() =>
  job.value === null ? null : cancelledNotice(job.value, now.value),
)

const openStatuses: readonly string[] = ['Unassigned', 'Assigned', 'InProgress', 'OnHold']

const liveLineShown = computed(() => job.value !== null && openStatuses.includes(job.value.status))

const side = useTemplateRef<HTMLElement>('side')

const cancelTrigger = useTemplateRef<InstanceType<typeof AppButton>>('cancelTrigger')

const cancelDialog = useTemplateRef<InstanceType<typeof CancelJobDialog>>('cancelDialog')

const cancelOpen = shallowRef(false)

const cancelReason = shallowRef('')

const cancelAnnouncement = shallowRef('')

const live = useLiveRefetch({
  refetch: () => store.refreshOpenJob(),
  applyJob: (updated) => {
    store.applyLiveJob(updated)
  },
  page: {
    refreshFailedTitle: 'The job could not be refreshed.',
    refreshedLabel: 'This job was refreshed at',
    showingFrom: (time) => `Showing this job as of ${time}.`,
  },
})

const noticeInPanel = computed(
  () => store.jobNotice?.code === 'concurrency.conflict' && panelShown.value,
)

const pageNotice = computed(() => (noticeInPanel.value ? null : notice.value))

const panelNotice = computed(() => (noticeInPanel.value ? notice.value : null))

usePageTitle(() => {
  if (store.jobStatus === 'ready' && job.value !== null) {
    return formatJobNumber(job.value.number)
  }
  return store.jobStatus === 'not-found' ? 'Job not found' : null
})

function retry(): void {
  retried.value = true
  void store.reloadJob(jobId.value)
}

function triggerElement(): HTMLElement | null {
  const element: unknown = cancelTrigger.value?.$el
  return element instanceof HTMLElement ? element : null
}

function openCancel(): void {
  cancelAnnouncement.value = ''
  cancelOpen.value = true
}

async function dismissCancel(): Promise<void> {
  cancelOpen.value = false
  await nextTick()
  const trigger = triggerElement()
  if (trigger !== null && trigger.isConnected) {
    trigger.focus()
  } else {
    header.value?.focusHeading()
  }
}

async function finishCancel(result: CancelDialogResult): Promise<void> {
  cancelOpen.value = false
  cancelReason.value = ''
  if (result.kind === 'cancelled') {
    cancelAnnouncement.value = `${formatJobNumber(result.job.number)} cancelled.`
  }
  await nextTick()
  header.value?.focusHeading()
}

watch(job, (current) => {
  now.value = new Date()
  if (current !== null) {
    live.markFresh()
  }
})

watch(
  panelShown,
  (shown, wasShown) => {
    const active = document.activeElement
    if (shown || !wasShown || active === null || side.value?.contains(active) !== true) {
      return
    }
    void nextTick(() => {
      header.value?.focusHeading()
    })
  },
  { flush: 'pre' },
)

watch(
  canCancel,
  (allowed, wasAllowed) => {
    if (allowed || !wasAllowed) {
      return
    }
    const current = job.value
    if (cancelOpen.value) {
      if (current === null || cancelDialog.value?.isBusy() === true) {
        return
      }
      cancelOpen.value = false
      cancelReason.value = ''
      store.showNotice('job.invalid-transition', current.number)
      return
    }
    const trigger = triggerElement()
    if (trigger === null || document.activeElement !== trigger) {
      return
    }
    void nextTick(() => {
      header.value?.focusHeading()
    })
  },
  { flush: 'pre' },
)

watch(
  () => store.jobNotice,
  async (current) => {
    if (current === null || noticeInPanel.value) {
      return
    }
    await nextTick()
    header.value?.focusHeading()
  },
)

void store.openJob(jobId.value)

onBeforeUnmount(() => {
  store.closeJob()
})
</script>

<template>
  <template v-if="store.jobStatus === 'ready' && job">
    <PageHeader ref="header" :title="job.title" :job-number="formatJobNumber(job.number)">
      <template #meta>
        <StatusChip :status="job.status" />
        <PriorityLabel :priority="job.priority" />
        <span
          >Created {{ formatMomentUnbroken(job.createdAt, now) }} by
          {{ job.createdBy.displayName }}</span
        >
      </template>
      <template v-if="job.status === 'Completed'" #actions>
        <AppButton
          variant="secondary"
          size="small"
          icon="clipboard-check"
          :to="{ name: 'job-inspection', params: { jobId: job.id } }"
          >Inspection report</AppButton
        >
      </template>
    </PageHeader>
    <Banner
      v-if="pageNotice"
      :key="`notice-${pageNotice.id}`"
      variant="warning"
      role="alert"
      :title="pageNotice.title"
      :message="pageNotice.message"
    />
    <Banner
      v-if="cancelled"
      class="job-detail__cancelled"
      :role="null"
      icon="x-circle"
      :title="cancelled.title"
      :detail="cancelled.reason"
    />
    <div class="job-detail">
      <div class="job-detail__main">
        <JobDetailsCard :job="job" :now="now" />
        <SectionCard heading="Description">
          <p class="job-detail__description">{{ job.description }}</p>
        </SectionCard>
        <SectionCard heading="Status history">
          <div class="job-detail__history">
            <StatusTimeline :history="job.history" :now="now" />
            <p v-if="liveLineShown" class="job-detail__live-line">
              Status changes from the technician app appear here in real time.
            </p>
          </div>
        </SectionCard>
        <AppButton
          v-if="canCancel"
          ref="cancelTrigger"
          class="job-detail__cancel"
          variant="danger"
          icon="x-circle"
          @click="openCancel"
          >Cancel job</AppButton
        >
        <CancelJobDialog
          v-if="cancelOpen"
          ref="cancelDialog"
          v-model:reason="cancelReason"
          :job="job"
          :open="cancelOpen"
          @dismiss="dismissCancel"
          @finished="finishCancel"
        />
      </div>
      <div v-if="panelShown" ref="side" class="job-detail__side">
        <AssignTechnicianCard :job="job" :now="now" :notice="panelNotice" />
      </div>
    </div>
    <p class="visually-hidden" role="status">{{ cancelAnnouncement }}</p>
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

.job-detail__side {
  flex: 1 1 380px;
  min-width: 0;
}

.job-detail__cancel {
  align-self: flex-start;
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
  display: flex;
  flex-direction: column;
  gap: var(--spacing-fs-14);
  padding: var(--spacing-fs-4) var(--spacing-fs-16) var(--spacing-fs-16);
}

.job-detail__live-line {
  margin: 0;
  color: var(--color-fs-muted);
  font-size: var(--text-fs-meta);
  line-height: var(--text-fs-meta--line-height);
  text-wrap: pretty;
}

.job-detail__skeleton {
  padding: var(--spacing-fs-16);
}
</style>
