<script setup lang="ts">
import AppButton from '@/shared/ui/AppButton.vue'
import EmptyState from '@/shared/ui/EmptyState.vue'
import PageHeader from '@/shared/ui/PageHeader.vue'
import SectionCard from '@/shared/ui/SectionCard.vue'

import { landingRouteName } from './router'
</script>

<template>
  <PageHeader title="Page not found" />
  <SectionCard>
    <EmptyState
      icon="navigation"
      title="Nothing is at this address"
      body="The link may be mistyped or out of date. Check the address, or go back to the dashboard."
    >
      <template #actions>
        <AppButton variant="secondary" icon="grid" :to="{ name: landingRouteName }"
          >Go to dashboard</AppButton
        >
      </template>
    </EmptyState>
  </SectionCard>
</template>
