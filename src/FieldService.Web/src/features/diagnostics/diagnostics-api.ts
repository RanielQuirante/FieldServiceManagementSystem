import { apiClient } from '@/shared/api/api-client'
import { unwrapResponse } from '@/shared/api/api-error'
import { correlationIdHeader } from '@/shared/api/auth-fetch'
import type { components } from '@/shared/api/schema.gen'

export type ReportClientErrorRequest = components['schemas']['ReportClientErrorRequest']

export async function reportClientError(request: ReportClientErrorRequest): Promise<string | null> {
  const response = await unwrapResponse(apiClient.POST('/api/client-errors', { body: request }))
  return response.headers.get(correlationIdHeader)
}
