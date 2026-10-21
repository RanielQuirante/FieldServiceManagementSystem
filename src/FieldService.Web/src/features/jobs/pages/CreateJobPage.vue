<script setup lang="ts">
import { useRouter } from 'vue-router'

import PageHeader from '@/shared/ui/PageHeader.vue'

import CreateJobForm from '../components/CreateJobForm.vue'
import type { JobDetails } from '../jobs-api'

const router = useRouter()

async function openCreatedJob(job: JobDetails): Promise<void> {
  await router.push({ name: 'job-detail', params: { jobId: job.id } })
}
</script>

<template>
  <PageHeader title="New job" heading-id="page-title" />
  <CreateJobForm labelled-by="page-title" @created="openCreatedJob" />
</template>
