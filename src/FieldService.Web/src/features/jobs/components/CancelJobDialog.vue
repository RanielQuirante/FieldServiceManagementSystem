<script lang="ts">
import type { CancelOutcome } from '../jobs-store'

export type CancelDialogResult = Extract<
  CancelOutcome,
  { kind: 'cancelled' | 'already-cancelled' | 'invalid-transition' | 'not-found' }
>

export const reasonMaximumLength = 500
</script>

<script setup lang="ts">
import { computed, nextTick, shallowRef, useId, useTemplateRef, watch } from 'vue'

import type { ApiError } from '@/shared/api/api-error'
import { separator } from '@/shared/format/date-time'
import { formatJobNumber } from '@/shared/format/job-number'
import AppIcon from '@/shared/ui/AppIcon.vue'
import Banner from '@/shared/ui/Banner.vue'
import ConfirmDialog from '@/shared/ui/ConfirmDialog.vue'
import StatusChip from '@/shared/ui/StatusChip.vue'
import TextArea from '@/shared/ui/TextArea.vue'

import type { JobDetails } from '../jobs-api'
import { useJobsStore } from '../jobs-store'

interface DialogBanner {
  readonly variant: 'error' | 'warning'
  readonly title: string
  readonly message: string | null
  readonly reference: string | null
}

const props = defineProps<{
  job: JobDetails
  open: boolean
}>()

const reason = defineModel<string>('reason', { required: true })

const emit = defineEmits<{
  dismiss: []
  finished: [result: CancelDialogResult]
}>()

const countFrom = 400

const blankReasonMessage = 'Enter a reason for cancelling the job.'

const store = useJobsStore()

const baseId = useId()

const jobLineId = `${baseId}-job`

const statusRowId = `${baseId}-status`

const consequenceId = `${baseId}-consequence`

const reasonId = `${baseId}-reason`

const describedBy = `${jobLineId} ${statusRowId} ${consequenceId}`

const busy = shallowRef(false)

const fieldError = shallowRef<string | null>(null)

const errorKind = shallowRef<'blank' | 'server' | null>(null)

const banner = shallowRef<DialogBanner | null>(null)

const announcement = shallowRef('')

const dialog = useTemplateRef<InstanceType<typeof ConfirmDialog>>('dialog')

const field = useTemplateRef<InstanceType<typeof TextArea>>('field')

const jobNumber = computed(() => formatJobNumber(props.job.number))

const technicianName = computed(() => props.job.assignedTechnician?.displayName ?? null)

const consequence = computed(() => {
  const rule = 'Cancelled jobs are read-only. This cannot be undone.'
  return technicianName.value === null
    ? rule
    : `${technicianName.value} sees it as cancelled in the technician app. ${rule}`
})

const countLine = computed(() => {
  const length = reason.value.length
  if (length < countFrom) {
    return null
  }
  const left = Math.max(reasonMaximumLength - length, 0)
  if (left === 0) {
    return 'No characters left'
  }
  return left === 1 ? '1 character left' : `${left} characters left`
})

function announce(text: string): void {
  announcement.value = ''
  void nextTick(() => {
    announcement.value = text
  })
}

watch(countLine, (current, previous) => {
  if (current === null) {
    announcement.value = ''
    return
  }
  if (previous === null || (current === 'No characters left' && previous !== current)) {
    announce(current)
  }
})

watch(reason, (value) => {
  if (errorKind.value === 'server' || (errorKind.value === 'blank' && value.trim().length > 0)) {
    fieldError.value = null
    errorKind.value = null
  }
})

watch(
  () => props.open,
  async (open) => {
    if (!open) {
      return
    }
    fieldError.value = null
    errorKind.value = null
    banner.value = null
    await nextTick()
    field.value?.focus()
  },
  { flush: 'post', immediate: true },
)

function waitMessage(error: ApiError): string {
  const seconds = error.retryAfterSeconds ?? 60
  return seconds === 1 ? 'Try again in 1 second.' : `Try again in ${seconds} seconds.`
}

function failureBanner(error: ApiError): DialogBanner {
  if (error.status === 0) {
    return {
      variant: 'error',
      title: 'FieldService could not be reached.',
      message: 'Check your connection, then try again.',
      reference: null,
    }
  }
  if (error.status === 429) {
    return {
      variant: 'error',
      title: 'Too many requests.',
      message: waitMessage(error),
      reference: null,
    }
  }
  if (error.status === 403) {
    return {
      variant: 'error',
      title: 'Your account cannot cancel jobs.',
      message: null,
      reference: error.correlationId,
    }
  }
  return {
    variant: 'error',
    title: 'The job could not be cancelled.',
    message: 'Try again in a moment.',
    reference: error.correlationId,
  }
}

async function showBanner(next: DialogBanner): Promise<void> {
  banner.value = next
  await nextTick()
  dialog.value?.revealBannerAndActions()
  dialog.value?.focusConfirm()
}

async function showFieldError(message: string, kind: 'blank' | 'server'): Promise<void> {
  fieldError.value = message
  errorKind.value = kind
  await nextTick()
  field.value?.focus()
}

async function confirm(): Promise<void> {
  if (busy.value) {
    return
  }
  if (reason.value.trim().length === 0) {
    banner.value = null
    await showFieldError(blankReasonMessage, 'blank')
    return
  }
  busy.value = true
  banner.value = null
  fieldError.value = null
  errorKind.value = null
  let outcome: CancelOutcome
  try {
    outcome = await store.cancelJob(reason.value)
  } finally {
    busy.value = false
  }
  switch (outcome.kind) {
    case 'reason-rejected':
      await showFieldError(outcome.message, 'server')
      return
    case 'conflict':
      await showBanner({
        variant: 'warning',
        title: `${jobNumber.value} changed after you opened it.`,
        message: 'Its current status is shown above. Cancel it again if you still want to.',
        reference: null,
      })
      return
    case 'failed':
      await showBanner(failureBanner(outcome.error))
      return
    default:
      emit('finished', outcome)
  }
}

function dismiss(): void {
  if (!busy.value) {
    emit('dismiss')
  }
}

function isBusy(): boolean {
  return busy.value
}

defineExpose({ isBusy })
</script>

<template>
  <ConfirmDialog
    ref="dialog"
    class="cancel-dialog"
    :open="open"
    title="Cancel this job?"
    :described-by="describedBy"
    dismiss-label="Keep job"
    confirm-label="Cancel job"
    confirm-busy-label="Cancelling…"
    :busy="busy"
    @dismiss="dismiss"
    @confirm="confirm"
  >
    <template #description>
      <p :id="jobLineId" class="cancel-dialog__job">
        <span class="cancel-dialog__number">{{ jobNumber }}</span
        ><span aria-hidden="true">{{ separator }}</span
        ><span class="visually-hidden">{{ ', ' }}</span
        >{{ job.title }}
      </p>
      <div :id="statusRowId" class="cancel-dialog__status">
        <StatusChip :status="job.status" />
        <span v-if="technicianName" class="cancel-dialog__technician"
          ><AppIcon class="cancel-dialog__technician-icon" name="user" :size="14" />{{
            technicianName
          }}</span
        >
      </div>
      <p :id="consequenceId" class="cancel-dialog__consequence">{{ consequence }}</p>
    </template>
    <div class="cancel-dialog__field">
      <TextArea
        :id="reasonId"
        ref="field"
        v-model="reason"
        label="Reason for cancelling"
        aria-required
        :maxlength="reasonMaximumLength"
        :readonly="busy"
        :error="fieldError"
        help="The reason is saved in the job's status history."
        :count="countLine"
      />
      <p class="visually-hidden" role="status">{{ announcement }}</p>
    </div>
    <template v-if="banner" #banner>
      <Banner
        :variant="banner.variant"
        role="alert"
        :title="banner.title"
        :message="banner.message"
        :reference="banner.reference"
      />
    </template>
  </ConfirmDialog>
</template>

<style scoped>
.cancel-dialog__job {
  margin: 0;
  color: var(--color-fs-ink-2);
  font-size: var(--text-fs-body);
  line-height: var(--text-fs-body--line-height);
  overflow-wrap: anywhere;
}

.cancel-dialog__number {
  font-family: var(--font-mono);
  font-size: 0.875rem;
  font-weight: 500;
  font-variant-numeric: tabular-nums;
}

.cancel-dialog__status {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-fs-8);
}

.cancel-dialog__technician {
  display: inline-flex;
  align-items: center;
  gap: var(--spacing-fs-6);
  color: var(--color-fs-ink-2);
  font-size: var(--text-fs-meta);
  line-height: var(--text-fs-meta--line-height);
}

.cancel-dialog__technician-icon {
  flex: none;
  color: var(--color-fs-muted);
}

.cancel-dialog__consequence {
  margin: 0;
  color: var(--color-fs-ink-2);
  font-size: var(--text-fs-body);
  line-height: var(--text-fs-body--line-height);
  text-wrap: pretty;
}

.cancel-dialog__field {
  display: flex;
  flex-direction: column;
}
</style>
