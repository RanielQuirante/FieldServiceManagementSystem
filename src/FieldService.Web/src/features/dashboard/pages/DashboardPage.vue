<script setup lang="ts">
import { computed, nextTick, shallowRef, useTemplateRef, watch } from 'vue'

import { useSessionStore } from '@/features/auth/session-store'

import HeroBand from '../components/HeroBand.vue'
import LiveActivityCard from '../components/LiveActivityCard.vue'
import NeedsTechnicianCard from '../components/NeedsTechnicianCard.vue'
import StatusKpiGrid from '../components/StatusKpiGrid.vue'
import TechnicianWorkloadCard from '../components/TechnicianWorkloadCard.vue'
import { useDashboardData } from '../composables/use-dashboard-data'
import { useGreetingClock } from '../composables/use-greeting-clock'
import type { DashboardPartState } from '../dashboard-copy'
import type { DashboardLoadStatus } from '../dashboard-store'

const { store, today, retrySummary, retryWorkload } = useDashboardData({
  refreshFailedTitle: 'The dashboard could not be refreshed.',
  refreshedLabel: 'The dashboard was refreshed at',
  showingFrom: (time) => `Showing data from ${time}.`,
})

const session = useSessionStore()

const clock = useGreetingClock()

const now = shallowRef(new Date())

const summaryRetried = shallowRef(false)

const workloadRetried = shallowRef(false)

const grid = useTemplateRef<InstanceType<typeof StatusKpiGrid>>('grid')

const workloadCard = useTemplateRef<InstanceType<typeof TechnicianWorkloadCard>>('workloadCard')

function partState(status: DashboardLoadStatus, hasData: boolean): DashboardPartState {
  if (status === 'error') {
    return 'error'
  }
  return status === 'ready' && hasData ? 'ready' : 'loading'
}

const summaryState = computed(() => partState(store.summaryStatus, store.summary !== null))

const workloadState = computed(() => partState(store.workloadStatus, store.workload !== null))

const displayName = computed(() => session.user?.displayName ?? null)

watch([() => store.summary, () => store.workload, today, clock], () => {
  now.value = new Date()
})

async function retrySummaryLoad(): Promise<void> {
  summaryRetried.value = true
  await retrySummary()
  await nextTick()
  if (store.summaryStatus === 'ready') {
    grid.value?.focusHeading()
  } else if (store.summaryStatus === 'error') {
    grid.value?.focusRetry()
  }
}

async function retryWorkloadLoad(): Promise<void> {
  workloadRetried.value = true
  await retryWorkload()
  await nextTick()
  if (store.workloadStatus === 'ready') {
    workloadCard.value?.focusContent()
  } else if (store.workloadStatus === 'error') {
    workloadCard.value?.focusRetry()
  }
}
</script>

<template>
  <HeroBand
    :now="clock"
    :today="today"
    :display-name="displayName"
    :summary="store.summary"
    :state="summaryState"
  />
  <StatusKpiGrid
    ref="grid"
    :summary="store.summary"
    :state="summaryState"
    :now="now"
    :error="store.summaryError"
    :retried="summaryRetried"
    @retry="retrySummaryLoad"
  />
  <div class="dashboard__pair">
    <NeedsTechnicianCard :summary="store.summary" :state="summaryState" :now="now" />
    <LiveActivityCard
      :summary="store.summary"
      :state="summaryState"
      :now="now"
      :new-ids="store.newActivityIds"
    />
  </div>
  <TechnicianWorkloadCard
    ref="workloadCard"
    :technicians="store.workload"
    :state="workloadState"
    :now="now"
    :error="store.workloadError"
    :retried="workloadRetried"
    @retry="retryWorkloadLoad"
  />
</template>

<style scoped>
.dashboard__pair {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(440px, 100%), 1fr));
  align-items: start;
  gap: var(--spacing-fs-20);
}
</style>
