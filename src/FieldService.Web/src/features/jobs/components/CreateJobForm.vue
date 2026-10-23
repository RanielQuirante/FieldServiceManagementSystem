<script lang="ts">
export type CreateJobField =
  | 'title'
  | 'description'
  | 'equipment'
  | 'customerName'
  | 'siteAddress'
  | 'siteAccessNotes'
  | 'contactName'
  | 'contactPhone'
  | 'priority'
  | 'date'
  | 'start'
  | 'end'
  | 'checklistTemplateId'
  | 'technicianId'

export const createJobLimits = {
  title: 120,
  description: 2000,
  equipment: 200,
  customerName: 120,
  siteAddress: 300,
  siteAccessNotes: 500,
  contactName: 100,
  contactPhone: 32,
} as const

export const contactPhonePattern = '\\+?[0-9 \\(\\)\\.\\-]{3,31}'
</script>

<script setup lang="ts">
import {
  computed,
  nextTick,
  onMounted,
  reactive,
  ref,
  shallowRef,
  useTemplateRef,
  watch,
} from 'vue'

import { useLiveRefetch } from '@/features/realtime/use-live-refetch'
import { isApiError, type ApiError } from '@/shared/api/api-error'
import { useAsyncState } from '@/shared/composables/use-async-state'
import { localDateTimeToInstant } from '@/shared/format/date-time'
import {
  isJobPriority,
  jobPriorityLabel,
  tradeLabel,
  type JobPriority,
} from '@/shared/format/labels'
import AppButton from '@/shared/ui/AppButton.vue'
import Banner from '@/shared/ui/Banner.vue'
import ErrorState from '@/shared/ui/ErrorState.vue'
import SectionCard from '@/shared/ui/SectionCard.vue'
import SegmentedControl, { type SegmentOption } from '@/shared/ui/SegmentedControl.vue'
import SelectField, { type SelectEntry, type SelectOption } from '@/shared/ui/SelectField.vue'
import TextArea from '@/shared/ui/TextArea.vue'
import TextField from '@/shared/ui/TextField.vue'

import {
  getAssignableTechnicians,
  getChecklistTemplates,
  type CreateJobRequest,
  type JobDetails,
  type Technician,
} from '../jobs-api'
import { useJobsStore } from '../jobs-store'
import { assignOrder, groupByTrade, technicianOptionLabel } from '../technician-options'

interface FormFailure {
  readonly title: string
  readonly message: string | null
  readonly reference: string | null
}

type FieldErrors = Partial<Record<CreateJobField, string>>

defineProps<{
  labelledBy?: string
}>()

const emit = defineEmits<{
  created: [job: JobDetails]
}>()

const store = useJobsStore()

const {
  status: templateStatus,
  data: templateData,
  error: templateError,
  run: loadTemplates,
} = useAsyncState(getChecklistTemplates, (items) => items.length === 0)

const {
  status: technicianStatus,
  data: technicianData,
  error: technicianError,
  run: loadTechnicians,
  refresh: refreshTechnicians,
} = useAsyncState(getAssignableTechnicians)

useLiveRefetch({ refetch: refreshTechnicians, refetchLists: refreshTechnicians })

const form = reactive({
  title: '',
  description: '',
  equipment: '',
  customerName: '',
  siteAddress: '',
  siteAccessNotes: '',
  contactName: '',
  contactPhone: '',
  priority: 'Normal',
  date: '',
  start: '',
  end: '',
  checklistTemplateId: '',
  technicianId: '',
})

const errors = ref<FieldErrors>({})
const summary = shallowRef<FormFailure | null>(null)
const failure = shallowRef<FormFailure | null>(null)
const attempt = ref(0)
const busy = ref(false)
const templatesRetried = shallowRef(false)
const techniciansRetried = shallowRef(false)

const formElement = useTemplateRef<HTMLFormElement>('formElement')

const templateErrorState = useTemplateRef<InstanceType<typeof ErrorState>>('templateErrorState')

const technicianErrorState = useTemplateRef<InstanceType<typeof ErrorState>>('technicianErrorState')

const checklistCard = useTemplateRef<InstanceType<typeof SectionCard>>('checklistCard')

const fieldOrder: readonly CreateJobField[] = [
  'title',
  'description',
  'equipment',
  'customerName',
  'siteAddress',
  'siteAccessNotes',
  'contactName',
  'contactPhone',
  'priority',
  'date',
  'start',
  'end',
  'checklistTemplateId',
  'technicianId',
]

const fieldIds: Readonly<Record<CreateJobField, string>> = {
  title: 'job-title',
  description: 'job-description',
  equipment: 'equipment',
  customerName: 'customer',
  siteAddress: 'site-address',
  siteAccessNotes: 'site-access-notes',
  contactName: 'contact-name',
  contactPhone: 'contact-phone',
  priority: 'priority',
  date: 'job-date',
  start: 'job-start',
  end: 'job-end',
  checklistTemplateId: 'checklist-template',
  technicianId: 'assign-technician',
}

const fieldLabels: Readonly<Record<CreateJobField, string>> = {
  title: 'Title',
  description: 'Description',
  equipment: 'Equipment',
  customerName: 'Customer',
  siteAddress: 'Site address',
  siteAccessNotes: 'Site access notes',
  contactName: 'Contact name',
  contactPhone: 'Contact phone',
  priority: 'Priority',
  date: 'Date',
  start: 'Start',
  end: 'End',
  checklistTemplateId: 'Checklist template',
  technicianId: 'Assign technician',
}

const serverFields: Readonly<Record<string, CreateJobField>> = {
  title: 'title',
  description: 'description',
  equipment: 'equipment',
  customerName: 'customerName',
  siteAddress: 'siteAddress',
  siteAccessNotes: 'siteAccessNotes',
  contactName: 'contactName',
  contactPhone: 'contactPhone',
  priority: 'priority',
  scheduledStart: 'start',
  scheduledEnd: 'end',
  checklistTemplateId: 'checklistTemplateId',
  technicianId: 'technicianId',
}

const technicianUnavailableMessage =
  'This technician is no longer available. Choose another technician or leave the job unassigned.'

const priorityOptions: readonly SegmentOption[] = [
  { value: 'Low', label: jobPriorityLabel('Low'), icon: 'chevron-down', iconTone: 'muted' },
  { value: 'Normal', label: jobPriorityLabel('Normal'), icon: 'minus', iconTone: 'ink-2' },
  { value: 'High', label: jobPriorityLabel('High'), icon: 'chevron-up', iconTone: 'high' },
  { value: 'Urgent', label: jobPriorityLabel('Urgent'), icon: 'alert-triangle', iconTone: 'high' },
]

const templateOptions = computed<SelectOption[]>(() =>
  (templateData.value ?? []).map((template) => ({ value: template.id, label: template.name })),
)

const templatePlaceholder = computed(() => {
  switch (templateStatus.value) {
    case 'content':
      return 'Choose a checklist template'
    case 'empty':
      return 'No checklist templates are available'
    case 'error':
      return 'Checklist templates are unavailable'
    default:
      return 'Loading checklist templates…'
  }
})

const selectedTrade = computed<string | null>(
  () =>
    templateData.value?.find((template) => template.id === form.checklistTemplateId)?.trade ?? null,
)

const techniciansAvailable = computed(
  () => technicianData.value !== null && technicianStatus.value !== 'error',
)

function technicianOption(technician: Technician): SelectOption {
  return {
    value: technician.id,
    label: technicianOptionLabel(technician),
    disabled: !technician.availableForAssignment,
  }
}

const technicianOptions = computed<SelectEntry[]>(() => {
  const technicians = technicianData.value
  if (technicians === null || !techniciansAvailable.value) {
    return []
  }
  const unassigned: SelectOption = { value: '', label: 'Leave unassigned' }
  const trade = selectedTrade.value
  const ordered = assignOrder(technicians, trade)
  if (trade === null) {
    return [unassigned, ...ordered.map(technicianOption)]
  }
  const { sameTrade, otherTrades } = groupByTrade(ordered, trade)
  return [
    unassigned,
    { label: tradeLabel(trade), options: sameTrade.map(technicianOption) },
    { label: 'Different trade', options: otherTrades.map(technicianOption) },
  ].filter((entry) => !('options' in entry) || entry.options.length > 0)
})

const technicianPlaceholder = computed(() => {
  if (technicianStatus.value === 'error') {
    return 'Technicians are unavailable'
  }
  return technicianData.value === null ? 'Loading technicians…' : null
})

const phonePattern = new RegExp(`^(?:${contactPhonePattern})$`)

function clearErrors(...fields: CreateJobField[]): void {
  if (fields.every((field) => errors.value[field] === undefined)) {
    return
  }
  const next = { ...errors.value }
  for (const field of fields) {
    delete next[field]
  }
  errors.value = next
}

watch(
  () => form.title,
  () => clearErrors('title'),
)
watch(
  () => form.description,
  () => clearErrors('description'),
)
watch(
  () => form.equipment,
  () => clearErrors('equipment'),
)
watch(
  () => form.customerName,
  () => clearErrors('customerName'),
)
watch(
  () => form.siteAddress,
  () => clearErrors('siteAddress'),
)
watch(
  () => form.siteAccessNotes,
  () => clearErrors('siteAccessNotes'),
)
watch(
  () => form.contactName,
  () => clearErrors('contactName'),
)
watch(
  () => form.contactPhone,
  () => clearErrors('contactPhone'),
)
watch(
  () => form.priority,
  () => clearErrors('priority'),
)
watch(
  () => [form.date, form.start, form.end],
  () => clearErrors('date', 'start', 'end'),
)
watch(
  () => form.checklistTemplateId,
  () => clearErrors('checklistTemplateId'),
)
watch(
  () => form.technicianId,
  () => clearErrors('technicianId'),
)
watch(technicianStatus, (status) => {
  if (status === 'error') {
    form.technicianId = ''
  }
})

function isBlank(value: string): boolean {
  return value.trim().length === 0
}

function validate(): FieldErrors {
  const found: FieldErrors = {}
  if (isBlank(form.title)) {
    found.title = 'Enter a title for the job.'
  }
  if (isBlank(form.description)) {
    found.description = 'Describe the work to be done.'
  }
  if (isBlank(form.customerName)) {
    found.customerName = 'Enter the customer name.'
  }
  if (isBlank(form.siteAddress)) {
    found.siteAddress = 'Enter the site address so the technician can find the job.'
  }
  if (!isBlank(form.contactPhone) && !phonePattern.test(form.contactPhone.trim())) {
    found.contactPhone = 'Use digits, spaces, parentheses, dots or hyphens, such as (555) 014-8812.'
  }
  if (!isJobPriority(form.priority)) {
    found.priority = 'Choose a priority.'
  }
  if (form.date === '') {
    found.date = 'Choose the date of the visit.'
  }
  if (form.start === '') {
    found.start = 'Choose a start time.'
  }
  if (form.end === '') {
    found.end = 'Choose an end time.'
  }
  if (found.date === undefined && found.start === undefined && found.end === undefined) {
    const start = localDateTimeToInstant(form.date, form.start)
    const end = localDateTimeToInstant(form.date, form.end)
    if (start === null) {
      found.start = 'Choose a start time.'
    } else if (end === null) {
      found.end = 'Choose an end time.'
    } else if (Date.parse(end) <= Date.parse(start)) {
      found.end = 'The end time must be after the start time.'
    }
  }
  if (form.checklistTemplateId === '') {
    found.checklistTemplateId = 'Choose a checklist template.'
  }
  return found
}

function focusField(field: CreateJobField): void {
  const root = formElement.value
  if (root === null) {
    return
  }
  const target =
    field === 'priority'
      ? (root.querySelector<HTMLInputElement>('input[name="priority"]:checked') ??
        root.querySelector<HTMLInputElement>('input[name="priority"]'))
      : root.querySelector<HTMLElement>(`#${fieldIds[field]}`)
  target?.focus()
}

function focusFirstError(found: FieldErrors): boolean {
  const first = fieldOrder.find((field) => found[field] !== undefined)
  if (first === undefined) {
    return false
  }
  void nextTick(() => focusField(first))
  return true
}

function optional(value: string): string | undefined {
  const trimmed = value.trim()
  return trimmed.length > 0 ? trimmed : undefined
}

function buildRequest(): CreateJobRequest {
  const equipment = optional(form.equipment)
  const siteAccessNotes = optional(form.siteAccessNotes)
  const contactName = optional(form.contactName)
  const contactPhone = optional(form.contactPhone)
  return {
    title: form.title.trim(),
    description: form.description.trim(),
    priority: form.priority as JobPriority,
    customerName: form.customerName.trim(),
    siteAddress: form.siteAddress.trim(),
    ...(siteAccessNotes !== undefined ? { siteAccessNotes } : {}),
    ...(contactName !== undefined ? { contactName } : {}),
    ...(contactPhone !== undefined ? { contactPhone } : {}),
    ...(equipment !== undefined ? { equipment } : {}),
    scheduledStart: localDateTimeToInstant(form.date, form.start) ?? '',
    scheduledEnd: localDateTimeToInstant(form.date, form.end) ?? '',
    checklistTemplateId: form.checklistTemplateId,
    ...(form.technicianId !== '' ? { technicianId: form.technicianId } : {}),
  }
}

function listOf(names: readonly string[]): string {
  if (names.length <= 1) {
    return names.join('')
  }
  return `${names.slice(0, -1).join(', ')} and ${names[names.length - 1]}`
}

function showServerFieldErrors(error: ApiError): void {
  const mapped: FieldErrors = {}
  const unmapped: string[] = []
  for (const [key, messages] of Object.entries(error.fieldErrors)) {
    const field = serverFields[key]
    const message = messages[0]
    if (message === undefined) {
      continue
    }
    if (field === undefined) {
      unmapped.push(...messages)
    } else if (mapped[field] === undefined) {
      mapped[field] = message
    }
  }
  errors.value = mapped
  const names = fieldOrder
    .filter((field) => mapped[field] !== undefined)
    .map((field) => fieldLabels[field])
  const parts = [names.length > 0 ? `Check ${listOf(names)}.` : null, ...unmapped]
  summary.value = {
    title: 'Some details need attention.',
    message: parts.filter((part): part is string => part !== null).join(' ') || null,
    reference: null,
  }
  focusFirstError(mapped)
}

async function retryTemplates(): Promise<void> {
  templatesRetried.value = true
  await loadTemplates()
  await nextTick()
  if (templateStatus.value === 'error') {
    templateErrorState.value?.focusRetry()
  } else if (templateStatus.value === 'empty') {
    checklistCard.value?.focusHeading()
  } else {
    focusField('checklistTemplateId')
  }
}

async function retryTechnicians(): Promise<void> {
  techniciansRetried.value = true
  await loadTechnicians()
  await nextTick()
  if (technicianStatus.value === 'error') {
    technicianErrorState.value?.focusRetry()
  } else {
    focusField('technicianId')
  }
}

async function showTechnicianUnavailable(): Promise<void> {
  errors.value = { technicianId: technicianUnavailableMessage }
  await loadTechnicians()
  await nextTick()
  focusField('technicianId')
}

function retryWait(error: ApiError): string {
  const seconds = error.retryAfterSeconds ?? 60
  return seconds === 1 ? 'Try again in 1 second.' : `Try again in ${seconds} seconds.`
}

function describeFailure(error: ApiError): FormFailure | null {
  if (error.isNetworkError) {
    return {
      title: 'FieldService could not be reached.',
      message: 'Check your connection, then try again.',
      reference: null,
    }
  }
  if (error.status === 401) {
    return null
  }
  if (error.status === 429) {
    return { title: 'Too many requests.', message: retryWait(error), reference: null }
  }
  if (error.status === 403) {
    return {
      title: 'Your account cannot create jobs.',
      message: null,
      reference: error.correlationId,
    }
  }
  return {
    title: 'The job could not be created.',
    message: 'Try again in a moment.',
    reference: error.correlationId,
  }
}

async function submit(): Promise<void> {
  if (busy.value) {
    return
  }
  attempt.value += 1
  summary.value = null
  failure.value = null
  const found = validate()
  errors.value = found
  if (focusFirstError(found)) {
    return
  }
  busy.value = true
  try {
    const created = await store.createJob(buildRequest())
    emit('created', created)
  } catch (error) {
    if (!isApiError(error)) {
      throw error
    }
    if (error.code === 'request.validation-failed' || error.status === 400) {
      showServerFieldErrors(error)
    } else if (error.status === 409 && error.code === 'technician.unavailable') {
      await showTechnicianUnavailable()
    } else {
      failure.value = describeFailure(error)
    }
  } finally {
    busy.value = false
  }
}

onMounted(() => {
  void loadTemplates()
  void loadTechnicians()
})
</script>

<template>
  <form
    ref="formElement"
    class="create-job-form"
    novalidate
    :aria-labelledby="labelledBy"
    @submit.prevent="submit"
  >
    <SectionCard variant="form" heading="Job" heading-id="job-section-title">
      <TextField
        :id="fieldIds.title"
        v-model="form.title"
        label="Title"
        name="title"
        required
        :maxlength="createJobLimits.title"
        :error="errors.title"
      />
      <TextArea
        :id="fieldIds.description"
        v-model="form.description"
        label="Description"
        name="description"
        required
        :maxlength="createJobLimits.description"
        :error="errors.description"
      />
      <TextField
        :id="fieldIds.equipment"
        v-model="form.equipment"
        label="Equipment"
        name="equipment"
        optional
        autocomplete="off"
        placeholder="Unit, model or tag"
        help="Name the unit or system to work on. The technician sees it on the job details."
        :maxlength="createJobLimits.equipment"
        :error="errors.equipment"
      />
    </SectionCard>
    <SectionCard variant="form" heading="Customer and site" heading-id="customer-section-title">
      <TextField
        :id="fieldIds.customerName"
        v-model="form.customerName"
        label="Customer"
        name="customerName"
        required
        :maxlength="createJobLimits.customerName"
        :error="errors.customerName"
      />
      <TextField
        :id="fieldIds.siteAddress"
        v-model="form.siteAddress"
        label="Site address"
        name="siteAddress"
        placeholder="Street, number, unit"
        required
        :maxlength="createJobLimits.siteAddress"
        :error="errors.siteAddress"
      />
      <TextField
        :id="fieldIds.siteAccessNotes"
        v-model="form.siteAccessNotes"
        label="Site access notes"
        name="siteAccessNotes"
        optional
        :maxlength="createJobLimits.siteAccessNotes"
        :error="errors.siteAccessNotes"
      />
      <div class="create-job-form__pair">
        <TextField
          :id="fieldIds.contactName"
          v-model="form.contactName"
          label="Contact name"
          name="contactName"
          optional
          autocomplete="off"
          :maxlength="createJobLimits.contactName"
          :error="errors.contactName"
        />
        <TextField
          :id="fieldIds.contactPhone"
          v-model="form.contactPhone"
          type="tel"
          label="Contact phone"
          name="contactPhone"
          optional
          autocomplete="off"
          :maxlength="createJobLimits.contactPhone"
          :pattern="contactPhonePattern"
          :error="errors.contactPhone"
        />
      </div>
    </SectionCard>
    <SectionCard variant="form" heading="Schedule and priority" heading-id="schedule-section-title">
      <SegmentedControl
        :id="fieldIds.priority"
        v-model="form.priority"
        legend="Priority"
        name="priority"
        :options="priorityOptions"
        :error="errors.priority"
      />
      <div class="create-job-form__schedule">
        <TextField
          :id="fieldIds.date"
          v-model="form.date"
          type="date"
          label="Date"
          name="date"
          required
          trailing-icon="calendar"
          :error="errors.date"
        />
        <TextField
          :id="fieldIds.start"
          v-model="form.start"
          type="time"
          label="Start"
          name="start"
          required
          trailing-icon="clock"
          :error="errors.start"
        />
        <TextField
          :id="fieldIds.end"
          v-model="form.end"
          type="time"
          label="End"
          name="end"
          required
          trailing-icon="clock"
          :error="errors.end"
        />
      </div>
    </SectionCard>
    <SectionCard
      ref="checklistCard"
      variant="form"
      heading="Checklist and assignment"
      heading-id="checklist-section-title"
      focusable-heading
    >
      <SelectField
        :id="fieldIds.checklistTemplateId"
        v-model="form.checklistTemplateId"
        label="Checklist template"
        name="checklistTemplateId"
        required
        :options="templateOptions"
        :placeholder="templatePlaceholder"
        :disabled="templateStatus !== 'content'"
        :error="errors.checklistTemplateId"
      />
      <ErrorState
        v-if="templateStatus === 'error'"
        ref="templateErrorState"
        title="Checklist templates could not be loaded."
        message="Check your connection, then try again."
        :reference="templateError?.correlationId ?? null"
        :role="templatesRetried ? 'alert' : 'status'"
        @retry="retryTemplates"
      />
      <SelectField
        :id="fieldIds.technicianId"
        v-model="form.technicianId"
        label="Assign technician"
        name="technicianId"
        help="The technician sees the job in the app as soon as you create it."
        :options="technicianOptions"
        :placeholder="technicianPlaceholder"
        :disabled="!techniciansAvailable"
        :error="errors.technicianId"
      />
      <ErrorState
        v-if="technicianStatus === 'error'"
        ref="technicianErrorState"
        title="Technicians could not be loaded."
        message="You can still create the job unassigned."
        :reference="technicianError?.correlationId ?? null"
        :role="techniciansRetried ? 'alert' : 'status'"
        @retry="retryTechnicians"
      />
    </SectionCard>
    <Banner
      v-if="summary"
      :key="`summary-${attempt}`"
      variant="error"
      role="alert"
      :title="summary.title"
      :message="summary.message"
    />
    <Banner
      v-if="failure"
      :key="`failure-${attempt}`"
      variant="error"
      role="alert"
      :title="failure.title"
      :message="failure.message"
      :reference="failure.reference"
    />
    <div class="create-job-form__actions">
      <AppButton type="submit" icon="check" :busy="busy" busy-label="Creating job…"
        >Create job</AppButton
      >
      <AppButton variant="quiet" :to="{ name: 'jobs' }">Cancel</AppButton>
    </div>
  </form>
</template>

<style scoped>
.create-job-form {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-fs-16);
  width: 100%;
  max-width: 55rem;
}

.create-job-form__pair {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(240px, 100%), 1fr));
  gap: var(--spacing-fs-16);
}

.create-job-form__schedule {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(200px, 100%), 1fr));
  gap: var(--spacing-fs-16);
}

.create-job-form__actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-fs-12);
}
</style>
