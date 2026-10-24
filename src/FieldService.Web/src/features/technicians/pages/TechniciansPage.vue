<script lang="ts">
export const searchAddressDelayMilliseconds = 300

export const announcementDelayMilliseconds = 500
</script>

<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, shallowRef, useTemplateRef, watch } from 'vue'

import type { DataTableState } from '@/shared/ui/DataTable.vue'
import ErrorState from '@/shared/ui/ErrorState.vue'
import PageHeader from '@/shared/ui/PageHeader.vue'

import TechniciansTable from '../components/TechniciansTable.vue'
import TechniciansToolbar from '../components/TechniciansToolbar.vue'
import { useTechnicianFilters } from '../composables/use-technician-filters'
import { useTechniciansData } from '../composables/use-technicians-data'
import { filterTechnicians, type ShiftFilter } from '../technician-filters'

const { store, retry } = useTechniciansData({
  refreshFailedTitle: 'Technicians could not be refreshed.',
  refreshedLabel: 'Technicians refreshed at',
  showingFrom: (time) => `Showing technicians from ${time}.`,
})

const { filters, update, clear } = useTechnicianFilters()

const searchText = shallowRef(filters.value.search)

const now = shallowRef(new Date())

const retried = shallowRef(false)

const announcement = shallowRef('')

const toolbar = useTemplateRef<InstanceType<typeof TechniciansToolbar>>('toolbar')

const table = useTemplateRef<InstanceType<typeof TechniciansTable>>('table')

const errorState = useTemplateRef<InstanceType<typeof ErrorState>>('errorState')

const shift = computed(() => filters.value.shift)

const items = computed(() => store.items ?? [])

const ready = computed(() => store.status === 'ready' && store.items !== null)

const rows = computed(() =>
  filterTechnicians(items.value, { shift: shift.value, search: searchText.value }),
)

const tableState = computed<DataTableState>(() => {
  if (!ready.value) {
    return 'loading'
  }
  return rows.value.length === 0 ? 'empty' : 'content'
})

const noMatches = computed(() => ready.value && items.value.length > 0 && rows.value.length === 0)

const subline = computed(() => {
  const figure = store.onShift
  return ready.value && figure !== null ? `${figure.onShift} of ${figure.total} on shift` : null
})

const failureMessage = computed(() => {
  const error = store.error
  if (error !== null && error.status === 429) {
    const seconds = error.retryAfterSeconds ?? 60
    return seconds === 1 ? 'Try again in 1 second.' : `Try again in ${seconds} seconds.`
  }
  return 'Check your connection, then try again.'
})

const failureReference = computed(() => {
  const error = store.error
  return error === null || error.status === 0 || error.status === 429 ? null : error.correlationId
})

let searchTimer: ReturnType<typeof setTimeout> | undefined
let announcementTimer: ReturnType<typeof setTimeout> | undefined
let lastSentSearch: string | null = null
let announcedRows: string | null = null

function rowIds(): string {
  return rows.value.map((technician) => technician.id).join(' ')
}

function cancelSearchTimer(): void {
  if (searchTimer !== undefined) {
    clearTimeout(searchTimer)
    searchTimer = undefined
  }
}

function cancelAnnouncementTimer(): void {
  if (announcementTimer !== undefined) {
    clearTimeout(announcementTimer)
    announcementTimer = undefined
  }
}

function announceRows(): void {
  announcementTimer = undefined
  if (!ready.value) {
    return
  }
  const ids = rowIds()
  if (ids === announcedRows) {
    return
  }
  announcedRows = ids
  const text =
    rows.value.length === 0
      ? 'No technicians match these filters'
      : `Showing ${rows.value.length} of ${items.value.length} technicians`
  announcement.value = ''
  void nextTick(() => {
    announcement.value = text
  })
}

function scheduleAnnouncement(): void {
  cancelAnnouncementTimer()
  announcementTimer = setTimeout(announceRows, announcementDelayMilliseconds)
}

function sendSearch(value: string): void {
  searchTimer = undefined
  if (value.trim() === filters.value.search.trim()) {
    return
  }
  lastSentSearch = value
  void update({ search: value }, { replace: true })
}

watch(searchText, (value) => {
  cancelSearchTimer()
  searchTimer = setTimeout(() => {
    sendSearch(value)
  }, searchAddressDelayMilliseconds)
  scheduleAnnouncement()
})

watch(
  () => filters.value.search,
  (value) => {
    if (value === lastSentSearch) {
      return
    }
    lastSentSearch = null
    if (value !== searchText.value) {
      cancelSearchTimer()
      searchText.value = value
    }
  },
)

watch(shift, scheduleAnnouncement)

watch(ready, (current) => {
  if (current && announcedRows === null) {
    announcedRows = rowIds()
  }
})

watch(
  () => store.items,
  () => {
    now.value = new Date()
  },
)

function changeShift(next: ShiftFilter): void {
  void update({ shift: next })
}

async function retryLoad(): Promise<void> {
  retried.value = true
  await retry()
  await nextTick()
  if (store.status === 'ready') {
    table.value?.focusContent()
  } else if (store.status === 'error') {
    errorState.value?.focusRetry()
  }
}

async function clearFilters(): Promise<void> {
  cancelSearchTimer()
  lastSentSearch = null
  searchText.value = ''
  await clear()
  toolbar.value?.focusSearch()
}

onBeforeUnmount(() => {
  cancelSearchTimer()
  cancelAnnouncementTimer()
})
</script>

<template>
  <PageHeader title="Technicians" :subline="subline" />
  <TechniciansToolbar
    ref="toolbar"
    v-model:search="searchText"
    :shift="shift"
    :counts="ready ? store.counts : null"
    :disabled="!ready"
    @change-shift="changeShift"
  />
  <ErrorState
    v-if="store.status === 'error'"
    ref="errorState"
    title="Technicians could not be loaded."
    :message="failureMessage"
    :reference="failureReference"
    :role="retried ? 'alert' : 'status'"
    @retry="retryLoad"
  />
  <TechniciansTable
    v-else
    ref="table"
    :technicians="rows"
    :state="tableState"
    :no-matches="noMatches"
    :now="now"
    @clear="clearFilters"
  />
  <p class="visually-hidden" role="status">{{ announcement }}</p>
</template>
