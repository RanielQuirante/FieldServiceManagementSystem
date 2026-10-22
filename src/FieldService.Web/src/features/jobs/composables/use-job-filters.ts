import { computed, type ComputedRef } from 'vue'
import { useRoute, useRouter, type LocationQuery, type LocationQueryRaw } from 'vue-router'

import {
  isJobPriority,
  isJobStatus,
  type JobPriority,
  type JobStatus,
} from '@/shared/format/labels'

export interface JobFilters {
  readonly search: string
  readonly status: JobStatus | null
  readonly technicianId: string | null
  readonly priority: JobPriority | null
  readonly page: number
}

export const searchMaximumLength = 100

export const searchDelayMilliseconds = 300

export const maximumPage = 10_000

export const noJobFilters: JobFilters = {
  search: '',
  status: null,
  technicianId: null,
  priority: null,
  page: 1,
}

const technicianIdPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

function firstValue(value: LocationQuery[string] | undefined): string | null {
  if (Array.isArray(value)) {
    return value.find((item): item is string => typeof item === 'string') ?? null
  }
  return typeof value === 'string' ? value : null
}

function pageFrom(value: string | null): number {
  if (value === null || !/^\d{1,6}$/.test(value)) {
    return 1
  }
  return Math.min(Math.max(Number.parseInt(value, 10), 1), maximumPage)
}

export function filtersFromQuery(query: LocationQuery): JobFilters {
  const status = firstValue(query.status)
  const technicianId = firstValue(query.technicianId)
  const priority = firstValue(query.priority)
  return {
    search: (firstValue(query.search) ?? '').slice(0, searchMaximumLength),
    status: isJobStatus(status) ? status : null,
    technicianId:
      technicianId !== null && technicianIdPattern.test(technicianId) ? technicianId : null,
    priority: isJobPriority(priority) ? priority : null,
    page: pageFrom(firstValue(query.page)),
  }
}

export function queryFromFilters(filters: JobFilters): LocationQueryRaw {
  const query: LocationQueryRaw = {}
  if (filters.search.trim().length > 0) {
    query.search = filters.search
  }
  if (filters.status !== null) {
    query.status = filters.status
  }
  if (filters.technicianId !== null) {
    query.technicianId = filters.technicianId
  }
  if (filters.priority !== null) {
    query.priority = filters.priority
  }
  if (filters.page > 1) {
    query.page = String(filters.page)
  }
  return query
}

export function hasActiveFilters(filters: JobFilters): boolean {
  return (
    filters.search.trim().length > 0 ||
    filters.status !== null ||
    filters.technicianId !== null ||
    filters.priority !== null
  )
}

export interface JobFilterControls {
  readonly filters: ComputedRef<JobFilters>
  update(changes: Partial<JobFilters>, options?: { readonly replace?: boolean }): Promise<void>
  clear(): Promise<void>
}

export function useJobFilters(): JobFilterControls {
  const route = useRoute()
  const router = useRouter()

  const filters = computed(() => filtersFromQuery(route.query))

  async function update(
    changes: Partial<JobFilters>,
    options: { readonly replace?: boolean } = {},
  ): Promise<void> {
    const next: JobFilters = { ...filters.value, page: 1, ...changes }
    const location = { name: 'jobs', query: queryFromFilters(next) }
    await (options.replace === true ? router.replace(location) : router.push(location))
  }

  async function clear(): Promise<void> {
    await router.push({ name: 'jobs', query: {} })
  }

  return { filters, update, clear }
}
