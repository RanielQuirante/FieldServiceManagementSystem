<script setup lang="ts">
import { computed, onBeforeUnmount, ref, useTemplateRef, watch } from 'vue'

import {
  jobPriorityLabel,
  jobStatuses,
  isJobPriority,
  isJobStatus,
  type JobPriority,
  type JobStatus,
} from '@/shared/format/labels'
import FilterChipGroup, { type FilterChipOption } from '@/shared/ui/FilterChipGroup.vue'
import SectionCard from '@/shared/ui/SectionCard.vue'
import SelectField, { type SelectOption } from '@/shared/ui/SelectField.vue'
import { jobStatusAppearance } from '@/shared/ui/status-appearance'
import TextField from '@/shared/ui/TextField.vue'

import {
  searchDelayMilliseconds,
  searchMaximumLength,
  type JobFilters,
} from '../composables/use-job-filters'

const props = defineProps<{
  filters: JobFilters
  statusCounts: Readonly<Record<JobStatus, number>> | null
  statusTotal: number | null
}>()

const emit = defineEmits<{
  change: [changes: Partial<JobFilters>, replace: boolean]
}>()

const allStatuses = 'all'

const priorityOrder: readonly JobPriority[] = ['Urgent', 'High', 'Normal', 'Low']

const searchText = ref(props.filters.search)

let searchTimer: ReturnType<typeof setTimeout> | undefined
let lastSentSearch: string | null = null

function cancelPendingSearch(): void {
  if (searchTimer !== undefined) {
    clearTimeout(searchTimer)
    searchTimer = undefined
  }
}

function sendSearch(): void {
  cancelPendingSearch()
  const value = searchText.value
  if (value.trim() === props.filters.search.trim()) {
    return
  }
  lastSentSearch = value
  emit('change', { search: value }, true)
}

function scheduleSearch(): void {
  cancelPendingSearch()
  searchTimer = setTimeout(sendSearch, searchDelayMilliseconds)
}

watch(searchText, scheduleSearch)

watch(
  () => props.filters.search,
  (value) => {
    if (value === lastSentSearch) {
      return
    }
    cancelPendingSearch()
    lastSentSearch = null
    if (value !== searchText.value) {
      searchText.value = value
      cancelPendingSearch()
    }
  },
)

onBeforeUnmount(cancelPendingSearch)

const statusOptions = computed<FilterChipOption[]>(() => [
  { value: allStatuses, label: 'All', count: props.statusTotal },
  ...jobStatuses.map((status) => {
    const appearance = jobStatusAppearance(status)
    return {
      value: status,
      label: appearance.label,
      icon: appearance.icon,
      tone: appearance.tone,
      count: props.statusCounts?.[status] ?? null,
    }
  }),
])

const selectedStatus = computed({
  get: () => props.filters.status ?? allStatuses,
  set: (value: string) => {
    emit('change', { status: isJobStatus(value) ? value : null }, false)
  },
})

const priorityOptions: readonly SelectOption[] = [
  { value: '', label: 'All priorities' },
  ...priorityOrder.map((priority) => ({ value: priority, label: jobPriorityLabel(priority) })),
]

const selectedPriority = computed({
  get: () => props.filters.priority ?? '',
  set: (value: string) => {
    emit('change', { priority: isJobPriority(value) ? value : null }, false)
  },
})

const searchField = useTemplateRef<InstanceType<typeof TextField>>('searchField')

function focusSearch(): void {
  searchField.value?.focus()
}

defineExpose({ focusSearch })
</script>

<template>
  <SectionCard variant="toolbar">
    <form class="jobs-toolbar" role="search" aria-label="Filter jobs" @submit.prevent="sendSearch">
      <div class="jobs-toolbar__fields">
        <TextField
          ref="searchField"
          v-model="searchText"
          class="jobs-toolbar__search"
          type="search"
          name="search"
          label="Search jobs, customers or sites"
          :maxlength="searchMaximumLength"
        />
        <SelectField
          v-model="selectedPriority"
          class="jobs-toolbar__priority"
          name="priority"
          label="Priority"
          hidden-label
          :options="priorityOptions"
        />
      </div>
      <FilterChipGroup v-model="selectedStatus" label="Filter by status" :options="statusOptions" />
    </form>
  </SectionCard>
</template>

<style scoped>
.jobs-toolbar {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-fs-12);
}

.jobs-toolbar__fields {
  display: flex;
  flex-wrap: wrap;
  gap: var(--spacing-fs-12);
}

.jobs-toolbar__search {
  flex: 1 1 320px;
}

.jobs-toolbar__priority {
  flex: 0 1 200px;
}
</style>
