<script setup lang="ts">
import { computed, useTemplateRef } from 'vue'

import FilterChipGroup, { type FilterChipOption } from '@/shared/ui/FilterChipGroup.vue'
import SectionCard from '@/shared/ui/SectionCard.vue'
import TextField from '@/shared/ui/TextField.vue'

import {
  shiftFilters,
  technicianSearchMaximumLength,
  type ShiftCounts,
  type ShiftFilter,
} from '../technician-filters'

const props = withDefaults(
  defineProps<{
    shift: ShiftFilter
    counts: ShiftCounts | null
    disabled?: boolean
  }>(),
  { disabled: false },
)

const emit = defineEmits<{
  'change-shift': [shift: ShiftFilter]
}>()

const search = defineModel<string>('search', { required: true })

const labels: Readonly<Record<ShiftFilter, string>> = {
  all: 'All',
  'on-shift': 'On shift',
  'off-shift': 'Off shift',
}

function countOf(shift: ShiftFilter, counts: ShiftCounts | null): number | null {
  if (counts === null) {
    return null
  }
  if (shift === 'all') {
    return counts.all
  }
  return shift === 'on-shift' ? counts.onShift : counts.offShift
}

const options = computed<FilterChipOption[]>(() =>
  shiftFilters.map((shift) => ({
    value: shift,
    label: labels[shift],
    count: countOf(shift, props.counts),
  })),
)

const selected = computed({
  get: () => props.shift,
  set: (value: string) => {
    const next = shiftFilters.find((shift) => shift === value)
    if (next !== undefined) {
      emit('change-shift', next)
    }
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
    <form class="technicians-toolbar" role="search" aria-label="Filter technicians" @submit.prevent>
      <FilterChipGroup
        v-model="selected"
        label="Filter by shift"
        :options="options"
        :disabled="disabled"
      />
      <TextField
        ref="searchField"
        v-model="search"
        class="technicians-toolbar__search"
        type="search"
        name="search"
        label="Search technicians"
        :maxlength="technicianSearchMaximumLength"
        :disabled="disabled"
      />
    </form>
  </SectionCard>
</template>

<style scoped>
.technicians-toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-fs-12);
}

.technicians-toolbar__search {
  flex: 1 1 260px;
}
</style>
