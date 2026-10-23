import { apiClient } from '@/shared/api/api-client'
import { unwrap } from '@/shared/api/api-error'
import type { components } from '@/shared/api/schema.gen'

type Schemas = components['schemas']

export type SocketTicket = Schemas['SocketTicketResponse']

export type LiveJob = Schemas['JobDetailsResponse']

export type LiveJobHistoryEntry = Schemas['JobHistoryEntryResponse']

export function issueSocketTicket(): Promise<SocketTicket> {
  return unwrap(apiClient.POST('/api/auth/socket-tickets'))
}

export function getJobForToast(jobId: string): Promise<LiveJob> {
  return unwrap(apiClient.GET('/api/jobs/{jobId}', { params: { path: { jobId } } }))
}
