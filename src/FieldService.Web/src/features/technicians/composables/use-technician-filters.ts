import { computed, type ComputedRef } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import {
  queryFromTechnicianFilters,
  technicianFiltersFromQuery,
  type TechnicianFilters,
} from '../technician-filters'

export interface TechnicianFilterControls {
  readonly filters: ComputedRef<TechnicianFilters>
  update(
    changes: Partial<TechnicianFilters>,
    options?: { readonly replace?: boolean },
  ): Promise<void>
  clear(): Promise<void>
}

export const techniciansRouteName = 'technicians'

export function useTechnicianFilters(): TechnicianFilterControls {
  const route = useRoute()
  const router = useRouter()

  const filters = computed(() => technicianFiltersFromQuery(route.query))

  async function update(
    changes: Partial<TechnicianFilters>,
    options: { readonly replace?: boolean } = {},
  ): Promise<void> {
    const next: TechnicianFilters = { ...filters.value, ...changes }
    const location = { name: techniciansRouteName, query: queryFromTechnicianFilters(next) }
    await (options.replace === true ? router.replace(location) : router.push(location))
  }

  async function clear(): Promise<void> {
    await router.push({ name: techniciansRouteName, query: {} })
  }

  return { filters, update, clear }
}
