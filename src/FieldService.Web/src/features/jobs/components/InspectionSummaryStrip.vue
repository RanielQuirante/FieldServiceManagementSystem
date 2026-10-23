<script setup lang="ts">
import { computed } from 'vue'

import type { IconName } from '@/shared/ui/icons'
import KpiTile from '@/shared/ui/KpiTile.vue'
import StatusChip from '@/shared/ui/StatusChip.vue'

import { summaryTiles, type ResultCount, type SummaryTile } from '../inspection-report'
import type { Inspection, JobDetails } from '../jobs-api'

interface LoadingTile {
  readonly key: string
  readonly label: string | null
  readonly icon: IconName | null
}

const props = withDefaults(
  defineProps<{
    job?: JobDetails | null
    inspection?: Inspection | null
    now: Date
    loading?: boolean
  }>(),
  { job: null, inspection: null, loading: false },
)

const loadingTiles: readonly LoadingTile[] = [
  { key: 'result', label: 'Result', icon: 'clipboard-check' },
  { key: 'time-on-site', label: 'Time on site', icon: 'clock' },
  { key: 'closing', label: null, icon: null },
]

const tiles = computed<readonly SummaryTile[]>(() =>
  props.loading || props.job === null || props.inspection === null
    ? []
    : summaryTiles(props.job, props.inspection, props.now),
)

const showsLoading = computed(() => tiles.value.length === 0)

function textOf(tile: SummaryTile): string | null {
  return tile.value.kind === 'text' ? tile.value.text : null
}

function countsOf(tile: SummaryTile): readonly ResultCount[] {
  return tile.value.kind === 'results' ? tile.value.counts : []
}
</script>

<template>
  <div class="inspection-summary">
    <template v-if="showsLoading">
      <KpiTile
        v-for="tile in loadingTiles"
        :key="tile.key"
        :label="tile.label"
        :icon="tile.icon"
        :data-tile="tile.key"
        loading
      />
    </template>
    <template v-else>
      <template v-for="tile in tiles" :key="tile.key">
        <KpiTile
          v-if="tile.value.kind === 'results'"
          :label="tile.label"
          :icon="tile.icon"
          :subline="tile.subline"
          :data-tile="tile.key"
        >
          <template #value>
            <StatusChip
              v-for="entry in countsOf(tile)"
              :key="entry.result"
              :result="entry.result"
              :count="entry.count"
            />
          </template>
        </KpiTile>
        <KpiTile
          v-else
          :label="tile.label"
          :icon="tile.icon"
          :value="textOf(tile)"
          :subline="tile.subline"
          :data-tile="tile.key"
        />
      </template>
    </template>
  </div>
</template>

<style scoped>
.inspection-summary {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(240px, 100%), 1fr));
  gap: var(--spacing-fs-16);
}
</style>
