import { apiClient } from '@/shared/api/api-client'
import { unwrap } from '@/shared/api/api-error'
import type { components } from '@/shared/api/schema.gen'

type Schemas = components['schemas']

export type Technician = Schemas['TechnicianResponse']

export type TechnicianJobReference = Schemas['TechnicianJobReference']

export async function getTechnicians(): Promise<readonly Technician[]> {
  const response = await unwrap(apiClient.GET('/api/technicians'))
  return response.items
}
