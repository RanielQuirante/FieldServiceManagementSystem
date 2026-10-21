<script setup lang="ts">
import { computed } from 'vue'

import { icons, type IconName, type IconShape, type IconSize } from './icons'

const props = withDefaults(
  defineProps<{
    name: IconName
    size?: IconSize
    strokeWidth?: number
  }>(),
  { size: 20, strokeWidth: undefined },
)

const shapes = computed<readonly IconShape[]>(() => icons[props.name])

const stroke = computed(() => props.strokeWidth ?? (props.size <= 16 ? 2.25 : 2))
</script>

<template>
  <svg
    class="app-icon"
    :width="size"
    :height="size"
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    :stroke-width="stroke"
    stroke-linecap="round"
    stroke-linejoin="round"
    aria-hidden="true"
    focusable="false"
  >
    <template v-for="(shape, index) in shapes" :key="index">
      <path v-if="shape.kind === 'path'" :d="shape.d" />
      <circle
        v-else-if="shape.kind === 'circle'"
        :cx="shape.cx"
        :cy="shape.cy"
        :r="shape.r"
        :stroke-dasharray="shape.dasharray"
      />
      <rect
        v-else
        :x="shape.x"
        :y="shape.y"
        :width="shape.width"
        :height="shape.height"
        :rx="shape.rx"
      />
    </template>
  </svg>
</template>

<style scoped>
.app-icon {
  flex: none;
}
</style>
