<script setup lang="ts">
import { computed, useId, useTemplateRef } from 'vue'

import type { ApiError } from '@/shared/api/api-error'
import ErrorState from '@/shared/ui/ErrorState.vue'
import KpiTile from '@/shared/ui/KpiTile.vue'
import LoadingState from '@/shared/ui/LoadingState.vue'
import { jobStatusAppearance } from '@/shared/ui/status-appearance'

import type { DashboardSummary } from '../dashboard-api'
import {
  jobsByStatusSubline,
  statusTileOrder,
  statusTiles,
  type DashboardPartState,
} from '../dashboard-copy'

const props = withDefaults(
  defineProps<{
    summary: DashboardSummary | null
    state: DashboardPartState
    now: Date
    error?: ApiError | null
    retried?: boolean
  }>(),
  { error: null, retried: false },
)

const emit = defineEmits<{
  retry: []
}>()

const titleId = `${useId()}-status-title`

const heading = useTemplateRef<HTMLHeadingElement>('heading')

const errorState = useTemplateRef<InstanceType<typeof ErrorState>>('errorState')

const ready = computed(() => (props.state === 'ready' ? props.summary : null))

const subline = computed(() => (ready.value === null ? null : jobsByStatusSubline(ready.value)))

const tiles = computed(() =>
  ready.value === null
    ? []
    : statusTiles(ready.value, props.now).map((tile) => ({
        ...tile,
        icon: jobStatusAppearance(tile.status).icon,
        text: String(tile.value),
        to: { name: 'jobs', query: { status: tile.status } },
      })),
)

const skeletons = statusTileOrder.map((tile) => ({
  ...tile,
  icon: jobStatusAppearance(tile.status).icon,
}))

const failureMessage = computed(() => {
  const error = props.error
  if (error !== null && error.status === 429) {
    const seconds = error.retryAfterSeconds ?? 60
    return seconds === 1 ? 'Try again in 1 second.' : `Try again in ${seconds} seconds.`
  }
  return 'Check your connection, then try again.'
})

const failureReference = computed(() => {
  const error = props.error
  return error === null || error.status === 0 || error.status === 429 ? null : error.correlationId
})

function focusHeading(): void {
  heading.value?.focus()
}

function focusRetry(): void {
  errorState.value?.focusRetry()
}

defineExpose({ focusHeading, focusRetry })
</script>

<template>
  <section class="status-grid" :aria-labelledby="titleId">
    <header class="status-grid__header">
      <h2 :id="titleId" ref="heading" class="status-grid__title" tabindex="-1">Jobs by status</h2>
      <p v-if="subline" class="status-grid__subline">{{ subline }}</p>
    </header>
    <ErrorState
      v-if="state === 'error'"
      ref="errorState"
      title="Job counts could not be loaded."
      :message="failureMessage"
      :reference="failureReference"
      :role="retried ? 'alert' : 'status'"
      @retry="emit('retry')"
    />
    <div v-else class="status-grid__frame">
      <div class="status-grid__tiles">
        <template v-if="ready">
          <KpiTile
            v-for="tile in tiles"
            :key="tile.status"
            :label="tile.label"
            :icon="tile.icon"
            :tone="tile.status"
            :value="tile.text"
            :subline="tile.subline"
            :to="tile.to"
            :data-status="tile.status"
          />
        </template>
        <template v-else>
          <KpiTile
            v-for="tile in skeletons"
            :key="tile.status"
            :label="tile.label"
            :icon="tile.icon"
            :tone="tile.status"
            :data-status="tile.status"
            loading
          />
        </template>
      </div>
      <LoadingState v-if="!ready" label="Loading job counts" />
    </div>
  </section>
</template>

<style scoped>
.status-grid {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-fs-14);
}

.status-grid__header {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  justify-content: space-between;
  gap: var(--spacing-fs-8);
}

.status-grid__title {
  margin: 0;
  color: var(--color-fs-ink);
  font-size: var(--text-fs-section);
  font-weight: var(--text-fs-section--font-weight);
  line-height: var(--text-fs-section--line-height);
  letter-spacing: var(--text-fs-section--letter-spacing);
  text-wrap: balance;
}

.status-grid__subline {
  margin: 0;
  color: var(--color-fs-muted);
  font-size: var(--text-fs-table);
  line-height: 1.4;
}

.status-grid__frame {
  container-type: inline-size;
}

.status-grid__tiles {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: var(--spacing-fs-16);
}

@container (width >= 528px) {
  .status-grid__tiles {
    grid-template-columns: repeat(3, minmax(0, 1fr));
  }
}

@container (width >= 1072px) {
  .status-grid__tiles {
    grid-template-columns: repeat(6, minmax(0, 1fr));
  }
}
</style>
