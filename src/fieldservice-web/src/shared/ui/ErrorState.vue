<script setup lang="ts">
import { useTemplateRef } from 'vue'

import AppButton from './AppButton.vue'
import Banner from './Banner.vue'

withDefaults(
  defineProps<{
    title: string
    message?: string | null
    reference?: string | null
    role?: 'status' | 'alert'
    retryLabel?: string | null
    busy?: boolean
  }>(),
  { message: null, reference: null, role: 'status', retryLabel: 'Try again', busy: false },
)

const emit = defineEmits<{
  retry: []
}>()

const retryButton = useTemplateRef<InstanceType<typeof AppButton>>('retryButton')

function focusRetry(): void {
  retryButton.value?.focus()
}

defineExpose({ focusRetry })
</script>

<template>
  <Banner variant="error" :role="role" :title="title" :message="message" :reference="reference">
    <template v-if="retryLabel" #actions>
      <AppButton
        ref="retryButton"
        variant="secondary"
        size="small"
        icon="refresh"
        :busy="busy"
        class="error-state__retry"
        @click="emit('retry')"
        >{{ retryLabel }}</AppButton
      >
    </template>
  </Banner>
</template>
