import { apiClient } from '@/shared/api/api-client'
import { unwrap } from '@/shared/api/api-error'
import type { components } from '@/shared/api/schema.gen'

import type { DashboardWindow } from './dashboard-window'

type Schemas = components['schemas']

export type DashboardSummary = Schemas['DashboardSummaryResponse']

export type DashboardOpenJobs = Schemas['DashboardOpenJobs']

export type DashboardRange = Schemas['DashboardRange']

export type DashboardTechnicianCounts = Schemas['DashboardTechnicianCounts']

export type DashboardUnassignedJob = Schemas['DashboardUnassignedJob']

export type DashboardActivityEntry = Schemas['DashboardActivityEntry']

export type WorkloadTechnician = Schemas['TechnicianResponse']

export function getDashboardSummary(period: DashboardWindow): Promise<DashboardSummary> {
  return unwrap(
    apiClient.GET('/api/dashboard/summary', {
      params: { query: { from: period.from, to: period.to } },
    }),
  )
}

export async function getTechnicians(): Promise<readonly WorkloadTechnician[]> {
  const response = await unwrap(apiClient.GET('/api/technicians'))
  return response.items
}
