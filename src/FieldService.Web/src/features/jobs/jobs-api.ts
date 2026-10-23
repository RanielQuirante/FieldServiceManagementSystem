import { apiClient } from '@/shared/api/api-client'
import { unwrap } from '@/shared/api/api-error'
import type { components } from '@/shared/api/schema.gen'
import type { JobPriority, JobStatus } from '@/shared/format/labels'

type Schemas = components['schemas']

export type JobSummary = Schemas['JobSummaryResponse']

export type JobDetails = Schemas['JobDetailsResponse']

export type SubmitAffordance = Schemas['SubmitAffordance']

export type JobHistoryEntry = Schemas['JobHistoryEntryResponse']

export type JobStatusCount = Schemas['JobStatusCount']

export type JobList = Schemas['GetJobsResponse']

export type CreateJobRequest = Schemas['CreateJobRequest']

export type AssignJobRequest = Schemas['AssignJobRequest']

export type ChecklistTemplate = Schemas['ChecklistTemplateResponse']

export type Technician = Schemas['TechnicianResponse']

export type TechnicianJobReference = Schemas['TechnicianJobReference']

export type Inspection = Schemas['InspectionResponse']

export type InspectionItem = Schemas['InspectionItemResponse']

export type InspectionAnswer = Schemas['InspectionAnswerResponse']

export interface JobListQuery {
  readonly search?: string
  readonly status?: JobStatus
  readonly technicianId?: string
  readonly priority?: JobPriority
  readonly page: number
  readonly pageSize: number
}

export function getJobs(query: JobListQuery): Promise<JobList> {
  return unwrap(apiClient.GET('/api/jobs', { params: { query } }))
}

export function getJobById(jobId: string): Promise<JobDetails> {
  return unwrap(apiClient.GET('/api/jobs/{jobId}', { params: { path: { jobId } } }))
}

export function getInspection(jobId: string): Promise<Inspection> {
  return unwrap(apiClient.GET('/api/jobs/{jobId}/inspection', { params: { path: { jobId } } }))
}

export function createJob(request: CreateJobRequest): Promise<JobDetails> {
  return unwrap(apiClient.POST('/api/jobs', { body: request }))
}

export function assignJob(jobId: string, request: AssignJobRequest): Promise<JobDetails> {
  return unwrap(
    apiClient.PUT('/api/jobs/{jobId}/assignment', { params: { path: { jobId } }, body: request }),
  )
}

export async function getChecklistTemplates(): Promise<readonly ChecklistTemplate[]> {
  const response = await unwrap(apiClient.GET('/api/checklist-templates'))
  return response.items
}

export async function getAssignableTechnicians(): Promise<readonly Technician[]> {
  const response = await unwrap(apiClient.GET('/api/technicians'))
  return response.items
}
