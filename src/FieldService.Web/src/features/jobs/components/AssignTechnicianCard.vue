<script lang="ts">
export interface JobNoticeCopy {
  readonly id: number
  readonly title: string
  readonly message: string
}
</script>

<script setup lang="ts">
import {
  computed,
  nextTick,
  onBeforeUnmount,
  onMounted,
  shallowRef,
  useId,
  useTemplateRef,
  watch,
} from 'vue'

import { firstFieldError, isApiError, type ApiError } from '@/shared/api/api-error'
import { useAsyncState } from '@/shared/composables/use-async-state'
import { tradeLabel } from '@/shared/format/labels'
import AppButton from '@/shared/ui/AppButton.vue'
import AppIcon from '@/shared/ui/AppIcon.vue'
import Avatar from '@/shared/ui/Avatar.vue'
import Banner from '@/shared/ui/Banner.vue'
import ErrorState from '@/shared/ui/ErrorState.vue'
import LoadingState from '@/shared/ui/LoadingState.vue'
import SectionCard from '@/shared/ui/SectionCard.vue'
import StatusChip from '@/shared/ui/StatusChip.vue'
import TextField from '@/shared/ui/TextField.vue'

import { getAssignableTechnicians, type JobDetails, type Technician } from '../jobs-api'
import { useJobsStore } from '../jobs-store'
import {
  assignOrder,
  assignRowDetail,
  groupByTrade,
  matchesSearch,
  type DetailPart,
} from '../technician-options'

interface PanelMessage {
  readonly variant: 'warning' | 'error'
  readonly title: string
  readonly message: string | null
  readonly reference: string | null
}

interface ShownMessage extends PanelMessage {
  readonly key: string
}

interface AssignSuccess {
  readonly name: string
  readonly reassigned: boolean
}

interface AssignRow {
  readonly technician: Technician
  readonly detail: readonly DetailPart[]
}

interface RowGroup {
  readonly key: string
  readonly label: string
  readonly rows: readonly AssignRow[]
}

const props = withDefaults(
  defineProps<{
    job: JobDetails
    now: Date
    notice?: JobNoticeCopy | null
  }>(),
  { notice: null },
)

const store = useJobsStore()

const {
  status: listStatus,
  data: listData,
  error: listError,
  run: loadTechnicians,
} = useAsyncState(getAssignableTechnicians)

const generatedId = useId()

const helperId = `${generatedId}-assign-helper`

const errorId = `${generatedId}-assign-error`

const query = shallowRef('')

const selectedId = shallowRef<string | null>(props.job.assignedTechnician?.id ?? null)

const busy = shallowRef(false)

const reassigning = shallowRef(false)

const attempt = shallowRef(0)

const panelMessage = shallowRef<PanelMessage | null>(null)

const fieldError = shallowRef<string | null>(null)

const success = shallowRef<AssignSuccess | null>(null)

const retried = shallowRef(false)

let mounted = true

const panelBody = useTemplateRef<HTMLDivElement>('panelBody')

const searchField = useTemplateRef<InstanceType<typeof TextField>>('searchField')

const group = useTemplateRef<HTMLFieldSetElement>('group')

const errorState = useTemplateRef<InstanceType<typeof ErrorState>>('errorState')

const assignButton = useTemplateRef<InstanceType<typeof AppButton>>('assignButton')

const trade = computed(() => props.job.checklistTemplate.trade)

const tradeText = computed(() => tradeLabel(trade.value))

const current = computed(() => props.job.assignedTechnician)

const heading = computed(() =>
  current.value === null ? 'Assign technician' : 'Reassign technician',
)

const failed = computed(() => listStatus.value === 'error')

const loading = computed(() => listData.value === null && !failed.value)

const listReady = computed(() => !loading.value && !failed.value)

const technicians = computed<readonly Technician[]>(() => listData.value ?? [])

const ordered = computed(() => assignOrder(technicians.value, trade.value))

const visible = computed(() =>
  ordered.value.filter((technician) => matchesSearch(technician, query.value)),
)

const groups = computed<RowGroup[]>(() => {
  const { sameTrade, otherTrades } = groupByTrade(visible.value, trade.value)
  const toRows = (items: readonly Technician[]): AssignRow[] =>
    items.map((technician) => ({
      technician,
      detail: assignRowDetail(technician, trade.value, props.now),
    }))
  return [
    { key: 'same-trade', label: tradeText.value, rows: toRows(sameTrade) },
    { key: 'other-trades', label: 'Different trade', rows: toRows(otherTrades) },
  ].filter((entry) => entry.rows.length > 0)
})

const searchText = computed(() => query.value.trim())

const noTechnicians = computed(() => technicians.value.length === 0)

const noMatches = computed(() => !noTechnicians.value && visible.value.length === 0)

const nobodyAvailable = computed(
  () => !technicians.value.some((technician) => technician.availableForAssignment),
)

const choice = computed<Technician | null>(() => {
  const chosen = technicians.value.find((technician) => technician.id === selectedId.value)
  if (chosen === undefined || chosen.id === current.value?.id) {
    return null
  }
  return chosen.availableForAssignment ? chosen : null
})

const buttonLabel = computed(() => {
  const chosen = choice.value
  if (chosen === null) {
    return current.value === null ? 'Assign technician' : 'Reassign technician'
  }
  return current.value === null
    ? `Assign to ${chosen.displayName}`
    : `Reassign to ${chosen.displayName}`
})

const busyLabel = computed(() => (reassigning.value ? 'Reassigning…' : 'Assigning…'))

const helperText = computed(() => {
  const chosen = choice.value
  const assignee = current.value
  if (chosen !== null) {
    const seesIt = `${chosen.displayName} will see this job in the technician app straight away.`
    return assignee === null ? seesIt : `${seesIt} ${assignee.displayName} will no longer see it.`
  }
  if (nobodyAvailable.value) {
    return 'No technician is available right now.'
  }
  return assignee === null
    ? 'Choose an available technician.'
    : `${assignee.displayName} is assigned now. Choose another technician to reassign the job.`
})

const successText = computed(() => {
  const done = success.value
  if (done === null) {
    return ''
  }
  return done.reassigned ? `Reassigned to ${done.name}.` : `Assigned to ${done.name}.`
})

const shownMessage = computed<ShownMessage | null>(() => {
  if (panelMessage.value !== null) {
    return { ...panelMessage.value, key: `message-${attempt.value}` }
  }
  const notice = props.notice
  if (notice === null) {
    return null
  }
  return {
    variant: 'warning',
    title: notice.title,
    message: notice.message,
    reference: null,
    key: `notice-${notice.id}`,
  }
})

function radioId(technician: Technician): string {
  return `${generatedId}-assign-${technician.id}`
}

function resetChoice(): void {
  selectedId.value = current.value?.id ?? null
}

function chooseRow(): void {
  success.value = null
  fieldError.value = null
}

function retryWait(error: ApiError): string {
  const seconds = error.retryAfterSeconds ?? 60
  return seconds === 1 ? 'Try again in 1 second.' : `Try again in ${seconds} seconds.`
}

function describeFailure(error: ApiError): PanelMessage {
  if (error.isNetworkError) {
    return {
      variant: 'error',
      title: 'FieldService could not be reached.',
      message: 'Check your connection, then try again.',
      reference: null,
    }
  }
  if (error.status === 403) {
    return {
      variant: 'error',
      title: 'Your account cannot assign jobs.',
      message: null,
      reference: error.correlationId,
    }
  }
  if (error.status === 429) {
    return {
      variant: 'error',
      title: 'Too many requests.',
      message: retryWait(error),
      reference: null,
    }
  }
  return {
    variant: 'error',
    title: 'The job could not be assigned.',
    message: 'Try again in a moment.',
    reference: error.correlationId,
  }
}

function focusFirstEnabledRow(): void {
  const firstEnabled = group.value?.querySelector<HTMLInputElement>(
    'input[type="radio"]:not(:disabled)',
  )
  if (firstEnabled) {
    firstEnabled.focus()
  } else {
    searchField.value?.focus()
  }
}

async function refetchAndFocusRows(): Promise<void> {
  await loadTechnicians()
  busy.value = false
  await nextTick()
  if (failed.value) {
    errorState.value?.focusRetry()
  } else {
    focusFirstEnabledRow()
  }
}

function focusIsLost(): boolean {
  const focused = document.activeElement
  return (
    focused === null || focused === document.body || (panelBody.value?.contains(focused) ?? false)
  )
}

async function refreshTechniciansInBackground(): Promise<void> {
  if (!mounted) {
    return
  }
  await loadTechnicians()
  if (!mounted || !failed.value) {
    return
  }
  await nextTick()
  if (focusIsLost()) {
    errorState.value?.focusRetry()
  }
}

function keepFocusOnButton(): void {
  const focused = document.activeElement
  if (focused !== null && group.value?.contains(focused) === true) {
    assignButton.value?.focus()
  }
}

function keepFocusedButtonInView(): void {
  const element: unknown = assignButton.value?.$el
  if (!(element instanceof HTMLElement) || document.activeElement !== element) {
    return
  }
  if (typeof element.scrollIntoView === 'function') {
    element.scrollIntoView({ block: 'nearest' })
  }
}

async function showFailure(error: ApiError, target: Technician): Promise<void> {
  if (error.status === 401) {
    return
  }
  if (error.status === 409 && error.code === 'technician.unavailable') {
    panelMessage.value = {
      variant: 'warning',
      title: `${target.displayName} is no longer available.`,
      message: 'The list is up to date. Choose another technician.',
      reference: null,
    }
    resetChoice()
    await refetchAndFocusRows()
    return
  }
  if (error.status === 400 && firstFieldError(error.fieldErrors, 'technicianId') !== null) {
    fieldError.value = 'Choose a technician from the list.'
    await refetchAndFocusRows()
    return
  }
  panelMessage.value = describeFailure(error)
}

async function assign(): Promise<void> {
  const target = choice.value
  if (busy.value || target === null) {
    return
  }
  attempt.value += 1
  panelMessage.value = null
  fieldError.value = null
  success.value = null
  reassigning.value = current.value !== null
  keepFocusOnButton()
  busy.value = true
  let refreshAfter = false
  try {
    const outcome = await store.assignJob(target.id)
    if (outcome.kind === 'assigned') {
      selectedId.value = outcome.job.assignedTechnician?.id ?? target.id
      success.value = { name: target.displayName, reassigned: reassigning.value }
      refreshAfter = true
    } else {
      refreshAfter = outcome.code === 'concurrency.conflict'
    }
  } catch (error) {
    if (!isApiError(error)) {
      throw error
    }
    await showFailure(error, target)
  } finally {
    busy.value = false
  }
  if (refreshAfter) {
    void refreshTechniciansInBackground()
  }
}

async function retryTechnicians(): Promise<void> {
  retried.value = true
  await loadTechnicians()
  await nextTick()
  if (failed.value) {
    errorState.value?.focusRetry()
  } else {
    searchField.value?.focus()
  }
}

function clearSearch(): void {
  query.value = ''
  searchField.value?.focus()
}

watch(
  () => props.job.assignedTechnician?.id ?? null,
  (next, previous) => {
    if (selectedId.value === null || selectedId.value === previous) {
      selectedId.value = next
    }
  },
)

watch(
  () => shownMessage.value?.key ?? null,
  async (key) => {
    if (key === null) {
      return
    }
    await nextTick()
    keepFocusedButtonInView()
  },
)

watch(listData, (list) => {
  const id = selectedId.value
  if (list === null || id === null || id === current.value?.id) {
    return
  }
  const match = list.find((technician) => technician.id === id)
  if (match === undefined || !match.availableForAssignment) {
    resetChoice()
  }
})

onMounted(() => {
  void loadTechnicians()
})

onBeforeUnmount(() => {
  mounted = false
})
</script>

<template>
  <SectionCard
    :heading="heading"
    heading-id="assign-title"
    :subheading="`${tradeText} technicians first`"
  >
    <div ref="panelBody" class="assign-panel">
      <TextField
        ref="searchField"
        v-model="query"
        type="search"
        label="Search technicians"
        :disabled="loading || failed"
      />
      <template v-if="loading">
        <div class="assign-panel__skeleton" aria-hidden="true">
          <div v-for="row in 3" :key="row" class="assign-panel__skeleton-row">
            <LoadingState :shapes="['radio']" />
            <LoadingState :shapes="['avatar-large']" />
            <LoadingState :shapes="['technician-name', 'technician-detail']" />
          </div>
        </div>
        <LoadingState label="Loading technicians" />
      </template>
      <ErrorState
        v-else-if="failed"
        ref="errorState"
        title="Technicians could not be loaded."
        message="Check your connection, then try again."
        :reference="listError?.correlationId ?? null"
        :role="retried ? 'alert' : 'status'"
        @retry="retryTechnicians"
      />
      <template v-else>
        <template v-if="noMatches">
          <p role="status" class="assign-panel__note">No technicians match “{{ searchText }}”.</p>
          <AppButton variant="quiet" icon="x" class="assign-panel__clear" @click="clearSearch"
            >Clear search</AppButton
          >
        </template>
        <p v-else-if="noTechnicians" role="status" class="assign-panel__note">
          There are no technician accounts yet.
        </p>
        <fieldset
          v-else
          ref="group"
          class="assign-panel__group"
          :inert="busy || undefined"
          :aria-describedby="fieldError ? errorId : undefined"
        >
          <legend class="visually-hidden">Assign technician</legend>
          <template v-for="(entry, index) in groups" :key="entry.key">
            <p :class="['assign-panel__eyebrow', { 'assign-panel__eyebrow--later': index > 0 }]">
              {{ entry.label }}
            </p>
            <label
              v-for="row in entry.rows"
              :key="row.technician.id"
              :for="radioId(row.technician)"
              :class="[
                'assign-row',
                {
                  'assign-row--selected': selectedId === row.technician.id,
                  'assign-row--unavailable': !row.technician.availableForAssignment,
                },
              ]"
            >
              <input
                :id="radioId(row.technician)"
                v-model="selectedId"
                class="assign-row__radio"
                type="radio"
                name="assign-technician"
                :value="row.technician.id"
                :disabled="!row.technician.availableForAssignment"
                @change="chooseRow"
              />
              <Avatar :name="row.technician.displayName" :size="36" />
              <span class="assign-row__text">
                <span class="assign-row__headline">
                  <span class="assign-row__name">{{ row.technician.displayName }}</span>
                  <StatusChip :shift="row.technician.isOnShift ? 'on' : 'off'" />
                </span>
                <span class="assign-row__detail">
                  <template v-for="(part, partIndex) in row.detail" :key="partIndex">
                    <span v-if="part.kind === 'job-number'" class="assign-row__job">{{
                      part.text
                    }}</span>
                    <span v-else-if="part.kind === 'moment'" class="assign-row__moment">{{
                      part.text
                    }}</span>
                    <template v-else>{{ part.text }}</template>
                  </template>
                </span>
              </span>
            </label>
          </template>
        </fieldset>
        <p v-if="fieldError" :id="errorId" class="assign-panel__error">
          <AppIcon name="alert-triangle" :size="14" />{{ fieldError }}
        </p>
      </template>
      <Banner
        v-if="shownMessage"
        :key="shownMessage.key"
        :variant="shownMessage.variant"
        role="alert"
        :title="shownMessage.title"
        :message="shownMessage.message"
        :reference="shownMessage.reference"
      />
      <template v-if="listReady">
        <AppButton
          ref="assignButton"
          class="assign-panel__action"
          block
          icon="user-check"
          :aria-disabled="choice === null"
          :aria-describedby="helperId"
          :busy="busy"
          :busy-label="busyLabel"
          @click="assign"
          >{{ buttonLabel }}</AppButton
        >
        <p
          v-if="success"
          :id="helperId"
          key="assign-success"
          role="status"
          class="assign-panel__helper"
        >
          {{ successText }}
        </p>
        <p v-else :id="helperId" key="assign-helper" class="assign-panel__helper">
          {{ helperText }}
        </p>
      </template>
    </div>
  </SectionCard>
</template>

<style scoped>
.assign-panel {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-fs-12);
  padding: 0 var(--spacing-fs-8) var(--spacing-fs-8);
}

.assign-panel__group {
  position: relative;
  display: flex;
  flex-direction: column;
  gap: var(--spacing-fs-6);
  min-width: 0;
  margin: 0;
  padding: 0;
  border: 0;
}

.assign-panel__eyebrow {
  margin: 0;
  padding: 0 var(--spacing-fs-2);
  color: var(--color-fs-muted);
  font-size: var(--text-fs-eyebrow);
  font-weight: var(--text-fs-eyebrow--font-weight);
  line-height: var(--text-fs-eyebrow--line-height);
  letter-spacing: var(--text-fs-eyebrow--letter-spacing);
  text-transform: uppercase;
}

.assign-panel__eyebrow--later {
  padding-top: var(--spacing-fs-8);
}

.assign-row {
  display: flex;
  align-items: center;
  gap: var(--spacing-fs-12);
  min-height: 4rem;
  padding: var(--spacing-fs-10) var(--spacing-fs-14);
  border-radius: var(--radius-fs-well);
  background: transparent;
  box-shadow: var(--shadow-fs-option);
  cursor: pointer;
}

.assign-row--selected {
  background: var(--color-fs-selected-bg);
  box-shadow: var(--shadow-fs-selected);
}

.assign-row--unavailable {
  opacity: 0.6;
  cursor: not-allowed;
}

.assign-row__radio {
  flex: none;
  width: 18px;
  height: 18px;
  margin: 0;
  accent-color: var(--color-fs-teal-700);
}

.assign-row__text {
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: var(--spacing-fs-2);
  min-width: 0;
}

.assign-row__headline {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: var(--spacing-fs-4) var(--spacing-fs-8);
}

.assign-row__name {
  min-width: 0;
  color: var(--color-fs-ink);
  font-size: var(--text-fs-table);
  font-weight: 600;
  line-height: 1.3;
  overflow-wrap: anywhere;
}

.assign-row__detail {
  color: var(--color-fs-muted);
  font-size: var(--text-fs-meta);
  line-height: var(--text-fs-meta--line-height);
  font-variant-numeric: tabular-nums;
}

.assign-row__job {
  font-family: var(--font-mono);
  font-size: var(--text-fs-mono-sm);
  font-weight: var(--text-fs-mono-sm--font-weight);
  letter-spacing: var(--text-fs-mono-sm--letter-spacing);
  white-space: nowrap;
}

.assign-row__moment {
  white-space: nowrap;
}

.assign-panel__skeleton {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-fs-6);
}

.assign-panel__skeleton-row {
  display: flex;
  align-items: center;
  gap: var(--spacing-fs-12);
  min-height: 4rem;
  padding: var(--spacing-fs-10) var(--spacing-fs-14);
}

.assign-panel__note {
  margin: 0;
  color: var(--color-fs-ink-2);
  font-size: var(--text-fs-table);
  line-height: var(--text-fs-table--line-height);
  text-wrap: pretty;
}

.assign-panel__clear {
  align-self: flex-start;
}

.assign-panel__error {
  display: flex;
  align-items: center;
  gap: var(--spacing-fs-6);
  margin: 0;
  color: var(--color-fs-danger);
  font-size: var(--text-fs-meta);
  font-weight: 500;
  line-height: var(--text-fs-meta--line-height);
}

.assign-panel__action {
  scroll-margin-block: var(--spacing-fs-16);
}

.assign-panel__helper {
  margin: 0;
  padding: 0 var(--spacing-fs-2);
  color: var(--color-fs-muted);
  font-size: var(--text-fs-meta);
  line-height: var(--text-fs-meta--line-height);
  text-wrap: pretty;
}

@media (forced-colors: active) {
  .assign-row {
    border: 1px solid CanvasText;
  }

  .assign-row--selected {
    border: 2px solid Highlight;
  }
}
</style>
