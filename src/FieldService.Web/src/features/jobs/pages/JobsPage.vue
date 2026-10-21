<script setup lang="ts">
import { computed, nextTick, ref, shallowRef, useTemplateRef, watch } from 'vue'

import AppButton from '@/shared/ui/AppButton.vue'
import EmptyState from '@/shared/ui/EmptyState.vue'
import ErrorState from '@/shared/ui/ErrorState.vue'
import PageHeader from '@/shared/ui/PageHeader.vue'
import Pagination, { rangeSummary } from '@/shared/ui/Pagination.vue'

import JobsTable from '../components/JobsTable.vue'
import JobsToolbar from '../components/JobsToolbar.vue'
import { hasActiveFilters, useJobFilters, type JobFilters } from '../composables/use-job-filters'
import { jobsPageSize, useJobsStore } from '../jobs-store'

const store = useJobsStore()

const { filters, update, clear } = useJobFilters()

const now = shallowRef(new Date())

const afterUserAction = ref(false)

const filtersActive = computed(() => hasActiveFilters(filters.value))

const list = computed(() => store.list)

const tableState = computed(() => {
  if (list.value === null) {
    return 'loading'
  }
  return list.value.items.length === 0 ? 'empty' : 'content'
})

const toolbar = useTemplateRef<InstanceType<typeof JobsToolbar>>('toolbar')

const table = useTemplateRef<InstanceType<typeof JobsTable>>('table')

const errorState = useTemplateRef<InstanceType<typeof ErrorState>>('errorState')

const subline = computed(() => {
  if (list.value === null || store.listStatus === 'error') {
    return null
  }
  return list.value.totalCount === 1 ? '1 job' : `${list.value.totalCount} jobs`
})

const announcement = computed(() => {
  if (store.listStatus !== 'ready' || list.value === null) {
    return ''
  }
  if (list.value.items.length === 0) {
    return filtersActive.value ? 'No jobs match these filters' : 'No jobs yet'
  }
  return rangeSummary(list.value.page, list.value.pageSize, list.value.totalCount, 'jobs', 'job')
})

const errorMessage = computed(() => {
  const error = store.listError
  if (error !== null && error.status === 429) {
    const seconds = error.retryAfterSeconds ?? 60
    return seconds === 1 ? 'Try again in 1 second.' : `Try again in ${seconds} seconds.`
  }
  return 'Check your connection, then try again.'
})

async function load(value: JobFilters): Promise<void> {
  await store.loadJobs(value)
  now.value = new Date()
  const result = store.list
  if (
    store.listStatus === 'ready' &&
    result !== null &&
    result.items.length === 0 &&
    result.totalCount > 0 &&
    value.page > 1
  ) {
    await update({ page: Math.ceil(result.totalCount / jobsPageSize) }, { replace: true })
  }
}

watch(
  filters,
  (value, previous) => {
    if (previous !== undefined) {
      afterUserAction.value = true
    }
    void load(value)
  },
  { immediate: true },
)

function changeFilters(changes: Partial<JobFilters>, replace: boolean): void {
  void update(changes, { replace })
}

function changePage(page: number): void {
  void update({ page }, { replace: false })
}

async function retry(): Promise<void> {
  afterUserAction.value = true
  await load(filters.value)
  await nextTick()
  if (store.listStatus === 'ready') {
    table.value?.focusRegion()
  } else if (store.listStatus === 'error') {
    errorState.value?.focusRetry()
  }
}

async function clearFilters(): Promise<void> {
  await clear()
  toolbar.value?.focusSearch()
}
</script>

<template>
  <PageHeader title="Jobs" :subline="subline" />
  <JobsToolbar
    ref="toolbar"
    :filters="filters"
    :status-counts="store.statusCounts"
    :status-total="store.statusTotal"
    @change="changeFilters"
  />
  <ErrorState
    v-if="store.listStatus === 'error'"
    ref="errorState"
    title="Jobs could not be loaded."
    :message="errorMessage"
    :reference="store.listError?.correlationId ?? null"
    :role="afterUserAction ? 'alert' : 'status'"
    @retry="retry"
  />
  <JobsTable
    v-else
    ref="table"
    :jobs="list?.items ?? []"
    :state="tableState"
    :busy="store.listStatus === 'loading'"
    :now="now"
  >
    <template #empty>
      <EmptyState
        v-if="filtersActive"
        icon="search"
        title="No jobs match these filters"
        body="Change the search or filters, or clear them to see every job."
      >
        <template #actions>
          <AppButton variant="secondary" icon="x" class="jobs-page__clear" @click="clearFilters"
            >Clear filters</AppButton
          >
        </template>
      </EmptyState>
      <EmptyState
        v-else
        icon="clipboard-check"
        title="No jobs yet"
        body="Jobs you create appear here, newest activity first."
      >
        <template #actions>
          <AppButton :to="{ name: 'job-create' }" icon="plus">New job</AppButton>
        </template>
      </EmptyState>
    </template>
    <template #footer>
      <Pagination
        v-if="list"
        :page="list.page"
        :page-size="list.pageSize"
        :total-count="list.totalCount"
        noun="jobs"
        noun-singular="job"
        @change="changePage"
      />
    </template>
  </JobsTable>
  <p class="visually-hidden" role="status">{{ announcement }}</p>
</template>
