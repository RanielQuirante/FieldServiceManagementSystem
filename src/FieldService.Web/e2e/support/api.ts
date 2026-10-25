import process from 'node:process'

import type { APIRequestContext, APIResponse } from '@playwright/test'

import { recordAdministratorRequest, recordAuthCall } from './budget.ts'
import { recordSessionEvent } from './sessions.ts'

export type SessionClient = 'Web' | 'Mobile'

export type JobStatus =
  'Unassigned' | 'Assigned' | 'InProgress' | 'OnHold' | 'Completed' | 'Cancelled'

export type JobPriority = 'Low' | 'Normal' | 'High' | 'Urgent'

export type Trade = 'Hvac' | 'Electrical' | 'Plumbing' | 'FireSafety'

export type AssignedJobsView = 'active' | 'completed'

export interface SessionUser {
  readonly id: string
  readonly displayName: string
  readonly role: 'Administrator' | 'Technician'
}

export interface SessionResponse {
  readonly accessToken: string
  readonly accessTokenExpiresAt: string
  readonly user: SessionUser
  readonly refreshToken: string | null
}

export interface PersonReference {
  readonly id: string
  readonly displayName: string
}

export interface TechnicianReference extends PersonReference {
  readonly trade: string
}

export interface ChecklistTemplate {
  readonly id: string
  readonly name: string
  readonly trade: string
  readonly itemCount: number
}

export interface JobSummary {
  readonly id: string
  readonly number: number
  readonly title: string
  readonly priority: JobPriority
  readonly status: JobStatus
  readonly customerName: string
  readonly siteAddress: string
  readonly scheduledStart: string
  readonly scheduledEnd: string
  readonly assignedTechnician: TechnicianReference | null
  readonly updatedAt: string
  readonly version: number
}

export interface JobHistoryEntry {
  readonly id: string
  readonly action: string
  readonly fromStatus: JobStatus | null
  readonly toStatus: JobStatus
  readonly actor: PersonReference
  readonly technician: TechnicianReference | null
  readonly note: string | null
  readonly occurredAt: string
}

export type StatusChangeTarget = 'InProgress' | 'OnHold'

export interface AllowedStatusChange {
  readonly status: JobStatus
  readonly requiresNote: boolean
}

export interface UpdateJobStatusBody {
  readonly status: string
  readonly note: string | null
  readonly version: number
}

export interface JobDetails extends JobSummary {
  readonly description: string
  readonly siteAccessNotes: string | null
  readonly contactName: string | null
  readonly contactPhone: string | null
  readonly equipment: string | null
  readonly checklistTemplate: { readonly id: string; readonly name: string; readonly trade: Trade }
  readonly createdBy: PersonReference
  readonly createdAt: string
  readonly assignedAt: string | null
  readonly startedAt: string | null
  readonly completedAt: string | null
  readonly cancelledAt: string | null
  readonly history: readonly JobHistoryEntry[]
  readonly affordances: {
    readonly allowedStatusChanges: readonly AllowedStatusChange[]
    readonly canAssign: boolean
    readonly canCancel: boolean
  }
}

export interface DashboardOpenJobs {
  readonly total: number
  readonly unassigned: number
  readonly assigned: number
  readonly inProgress: number
  readonly onHold: number
  readonly withTechnician: number
  readonly inProgressTechnicianCount: number
  readonly nextAssignedStart: string | null
  readonly onHoldSince: string | null
}

export interface DashboardRange {
  readonly from: string
  readonly to: string
  readonly created: number
  readonly completed: number
  readonly cancelled: number
  readonly unassignedScheduled: number
  readonly lastCompletedAt: string | null
}

export interface DashboardUnassignedJob {
  readonly id: string
  readonly number: number
  readonly title: string
  readonly priority: JobPriority
  readonly customerName: string
  readonly siteAddress: string
  readonly scheduledStart: string
  readonly scheduledEnd: string
}

export interface DashboardActivityEntry {
  readonly id: string
  readonly occurredAt: string
  readonly action: string
  readonly jobId: string
  readonly jobNumber: number
  readonly fromStatus: JobStatus | null
  readonly toStatus: JobStatus
  readonly actor: PersonReference
  readonly technician: PersonReference | null
  readonly note: string | null
}

export interface DashboardSummary {
  readonly generatedAt: string
  readonly openJobs: DashboardOpenJobs
  readonly range: DashboardRange
  readonly technicians: { readonly total: number; readonly onShift: number }
  readonly needsTechnician: readonly DashboardUnassignedJob[]
  readonly recentActivity: readonly DashboardActivityEntry[]
}

export interface DayWindow {
  readonly from: string
  readonly to: string
}

export interface TechnicianJobReference {
  readonly id: string
  readonly number: number
  readonly status: JobStatus
  readonly scheduledStart: string
  readonly scheduledEnd: string
}

export interface Technician {
  readonly id: string
  readonly displayName: string
  readonly email: string
  readonly phoneNumber: string
  readonly trade: Trade
  readonly isActive: boolean
  readonly isOnShift: boolean
  readonly availableForAssignment: boolean
  readonly openJobCount: number
  readonly currentJob: TechnicianJobReference | null
  readonly nextJob: TechnicianJobReference | null
}

export interface AssignedJob {
  readonly id: string
  readonly number: number
  readonly title: string
  readonly status: JobStatus
  readonly scheduledStart: string
  readonly version: number
}

export interface StatusCount {
  readonly status: JobStatus
  readonly count: number
}

export interface JobList {
  readonly items: readonly JobSummary[]
  readonly page: number
  readonly pageSize: number
  readonly totalCount: number
  readonly statusCounts: readonly StatusCount[]
}

export interface CreateJobBody {
  readonly title: string
  readonly description: string
  readonly priority: JobPriority
  readonly customerName: string
  readonly siteAddress: string
  readonly siteAccessNotes?: string
  readonly contactName?: string
  readonly contactPhone?: string
  readonly equipment?: string
  readonly scheduledStart: string
  readonly scheduledEnd: string
  readonly checklistTemplateId: string
  readonly technicianId?: string
}

export type InspectionResult = 'Pass' | 'Fail' | 'NotApplicable'

export interface InspectionReadingInput {
  readonly readingFieldId: string
  readonly value: number
}

export interface InspectionAnswerInput {
  readonly checklistItemId: string
  readonly result: InspectionResult
  readonly note?: string | null
  readonly readings?: readonly InspectionReadingInput[] | null
}

export interface SaveInspectionBody {
  readonly answers: readonly InspectionAnswerInput[]
  readonly version: number
}

export interface SubmitJobBody {
  readonly technicianNotes: string | null
  readonly siteSafetyConfirmed: boolean
  readonly version: number
}

export interface InspectionReadingField {
  readonly readingFieldId: string
  readonly position: number
  readonly label: string
  readonly unit: string
}

export interface InspectionItem {
  readonly checklistItemId: string
  readonly position: number
  readonly prompt: string
  readonly readingFields: readonly InspectionReadingField[]
  readonly answer: {
    readonly result: InspectionResult
    readonly note: string | null
    readonly readings: readonly InspectionReadingInput[]
  } | null
}

export interface Inspection {
  readonly jobId: string
  readonly jobNumber: number
  readonly jobStatus: JobStatus
  readonly items: readonly InspectionItem[]
  readonly progress: {
    readonly answered: number
    readonly total: number
    readonly passCount: number
    readonly failCount: number
    readonly notApplicableCount: number
  }
  readonly startedAt: string | null
  readonly completedAt: string | null
  readonly updatedAt: string | null
  readonly submittedAt: string | null
  readonly technicianNotes: string | null
  readonly version: number
  readonly affordances: {
    readonly canEdit: boolean
    readonly canSubmit: {
      readonly allowed: boolean
      readonly blockingCode: string | null
      readonly answered: number
      readonly total: number
    }
  }
}

export interface Problem {
  readonly status: number
  readonly code: string
  readonly title: string
  readonly errors?: Readonly<Record<string, readonly string[]>>
}

export const demoAccounts = {
  administrator: 'dana.whitfield@fieldservice.example',
  technician: 'marco.reyes@fieldservice.example',
} as const

export const demoTechnicians = {
  assignments: { name: 'Lena Park', email: 'lena.park@fieldservice.example' },
  inspections: { name: 'Priya Natarajan', email: 'priya.natarajan@fieldservice.example' },
  offShift: { name: 'Samuel Osei', email: 'samuel.osei@fieldservice.example' },
  conflicting: { name: 'Tom Becker', email: 'tom.becker@fieldservice.example' },
} as const

export const refreshCookieName = 'fs_refresh'

export const socketTicketsPath = '/api/auth/socket-tickets'

export const csrfHeader = { 'X-FieldService-Csrf': '1' } as const

export type SentinelSink = (label: string, value: string | null | undefined) => void

export function demoPassword(): string {
  const value = process.env.E2E_DEMO_PASSWORD
  if (value === undefined || value.length === 0) {
    throw new Error(
      'E2E_DEMO_PASSWORD is not set. Locally, playwright.config.ts loads it from the e2e env file written by create-dev-env.mjs.',
    )
  }
  return value
}

export function refreshCookieFrom(setCookieHeaders: readonly string[]): string | null {
  for (const header of setCookieHeaders) {
    const match = new RegExp(`(?:^|\\n)${refreshCookieName}=([^;\\n]*)`).exec(header)
    if (match?.[1] !== undefined && match[1].length > 0) {
      return match[1]
    }
  }
  return null
}

export function setCookieHeadersOf(response: APIResponse): string[] {
  return response
    .headersArray()
    .filter((header) => header.name.toLowerCase() === 'set-cookie')
    .map((header) => header.value)
}

async function recordSession(response: APIResponse, sink: SentinelSink): Promise<void> {
  sink('refresh cookie', refreshCookieFrom(setCookieHeadersOf(response)))
  if (!response.ok()) {
    return
  }
  const session = (await response.json()) as SessionResponse
  sink('access token', session.accessToken)
  sink('refresh token', session.refreshToken)
}

function spendBudget(call: 'sign-in' | 'refresh'): void {
  const violation = recordAuthCall(call)
  if (violation !== null) {
    throw new Error(violation)
  }
}

export async function signIn(
  request: APIRequestContext,
  email: string,
  client: SessionClient,
  sink: SentinelSink,
): Promise<APIResponse> {
  spendBudget('sign-in')
  const response = await request.post('/api/auth/sign-in', {
    data: { email, password: demoPassword(), client },
  })
  await recordSession(response, sink)
  if (response.ok()) {
    recordSessionEvent('opened', `${client} sign-in through the API`)
  }
  return response
}

export async function signOutWithCookie(request: APIRequestContext): Promise<number> {
  const response = await request.post('/api/auth/sign-out', { headers: { ...csrfHeader } })
  if (response.status() === 204) {
    recordSessionEvent('signed-out', 'Web sign-out with the refresh cookie')
  }
  return response.status()
}

export async function signOutWithToken(
  request: APIRequestContext,
  refreshToken: string,
): Promise<number> {
  const response = await request.post('/api/auth/sign-out', { data: { refreshToken } })
  if (response.status() === 204) {
    recordSessionEvent('signed-out', 'Mobile sign-out with the refresh token')
  }
  return response.status()
}

export async function refreshWithCookie(
  request: APIRequestContext,
  sink: SentinelSink,
  headers: Readonly<Record<string, string>> = csrfHeader,
): Promise<APIResponse> {
  spendBudget('refresh')
  const response = await request.post('/api/auth/refresh', { headers: { ...headers } })
  await recordSession(response, sink)
  return response
}

export async function readSession(
  response: APIResponse,
  purpose: string,
): Promise<SessionResponse> {
  if (!response.ok()) {
    const problem = await readProblem(response)
    throw new Error(`${purpose} failed with ${problem.status} ${problem.code}.`)
  }
  return (await response.json()) as SessionResponse
}

export async function readProblem(response: APIResponse): Promise<Problem> {
  const body = (await response.json()) as Partial<Problem>
  return {
    status: response.status(),
    code: body.code ?? '',
    title: body.title ?? '',
    ...(body.errors !== undefined ? { errors: body.errors } : {}),
  }
}

export interface AccessTokens {
  current(): string
  renew(): Promise<string>
}

export type ApiMethod = 'GET' | 'POST' | 'PUT'

export class AdminApi {
  constructor(
    private readonly request: APIRequestContext,
    private readonly tokens: AccessTokens,
  ) {}

  async send(method: ApiMethod, path: string, data?: unknown): Promise<APIResponse> {
    const attempt = (token: string): Promise<APIResponse> => {
      recordAdministratorRequest()
      return this.request.fetch(path, {
        method,
        headers: { Authorization: `Bearer ${token}` },
        ...(data !== undefined ? { data } : {}),
      })
    }
    const response = await attempt(this.tokens.current())
    if (response.status() !== 401) {
      return response
    }
    return attempt(await this.tokens.renew())
  }

  private async read<T>(method: ApiMethod, path: string, data?: unknown): Promise<T> {
    const response = await this.send(method, path, data)
    if (!response.ok()) {
      const problem = await readProblem(response)
      throw new Error(
        `${method} ${path.split('?')[0]} failed with ${problem.status} ${problem.code}.`,
      )
    }
    return (await response.json()) as T
  }

  getJobs(query: Readonly<Record<string, string | number>> = {}): Promise<JobList> {
    const search = new URLSearchParams(
      Object.entries(query).map(([key, value]) => [key, String(value)]),
    ).toString()
    return this.read<JobList>('GET', search.length > 0 ? `/api/jobs?${search}` : '/api/jobs')
  }

  getJob(id: string): Promise<JobDetails> {
    return this.read<JobDetails>('GET', `/api/jobs/${id}`)
  }

  getInspection(jobId: string): Promise<Inspection> {
    return this.read<Inspection>('GET', `/api/jobs/${jobId}/inspection`)
  }

  async findJobByNumber(number: number): Promise<JobSummary> {
    const list = await this.getJobs({ search: `JOB-${number}`, pageSize: 100 })
    const match = list.items.find((job) => job.number === number)
    if (match === undefined) {
      throw new Error(`JOB-${number} is not in the job list.`)
    }
    return match
  }

  async checklistTemplates(): Promise<ChecklistTemplate[]> {
    const body = await this.read<{ items: ChecklistTemplate[] }>('GET', '/api/checklist-templates')
    return body.items
  }

  createJob(body: CreateJobBody): Promise<JobDetails> {
    return this.read<JobDetails>('POST', '/api/jobs', body)
  }

  async technicians(): Promise<Technician[]> {
    const body = await this.read<{ items: Technician[] }>('GET', '/api/technicians')
    return body.items
  }

  async technicianNamed(displayName: string): Promise<Technician> {
    const match = (await this.technicians()).find(
      (technician) => technician.displayName === displayName,
    )
    if (match === undefined) {
      throw new Error(`No technician is named ${displayName}.`)
    }
    return match
  }

  assignJob(jobId: string, technicianId: string, version: number): Promise<JobDetails> {
    return this.read<JobDetails>('PUT', `/api/jobs/${jobId}/assignment`, {
      technicianId,
      version,
    })
  }

  cancelJob(jobId: string, reason: string, version: number): Promise<JobDetails> {
    return this.read<JobDetails>('POST', `/api/jobs/${jobId}/cancellation`, { reason, version })
  }

  dashboardSummary(window: DayWindow): Promise<DashboardSummary> {
    const query = new URLSearchParams({ from: window.from, to: window.to }).toString()
    return this.read<DashboardSummary>('GET', `/api/dashboard/summary?${query}`)
  }
}

export class TechnicianApi {
  constructor(
    private readonly request: APIRequestContext,
    readonly user: SessionUser,
    private readonly accessToken: string,
    private readonly refreshToken: string,
  ) {}

  signOut(): Promise<number> {
    return signOutWithToken(this.request, this.refreshToken)
  }

  private get(path: string): Promise<APIResponse> {
    return this.request.get(path, { headers: { Authorization: `Bearer ${this.accessToken}` } })
  }

  updateStatus(jobId: string, body: UpdateJobStatusBody): Promise<APIResponse> {
    return this.request.put(`/api/jobs/${jobId}/status`, {
      headers: { Authorization: `Bearer ${this.accessToken}` },
      data: body,
    })
  }

  async readJob(jobId: string): Promise<JobDetails> {
    const response = await this.getJob(jobId)
    if (!response.ok()) {
      const problem = await readProblem(response)
      throw new Error(`GET /api/jobs/{jobId} failed with ${problem.status} ${problem.code}.`)
    }
    return (await response.json()) as JobDetails
  }

  private async changeStatus(
    jobId: string,
    status: StatusChangeTarget,
    note: string | null,
  ): Promise<JobDetails> {
    const current = await this.readJob(jobId)
    const response = await this.updateStatus(jobId, { status, note, version: current.version })
    if (!response.ok()) {
      const problem = await readProblem(response)
      throw new Error(
        `PUT /api/jobs/{jobId}/status to ${status} failed with ${problem.status} ${problem.code}.`,
      )
    }
    return (await response.json()) as JobDetails
  }

  start(jobId: string): Promise<JobDetails> {
    return this.changeStatus(jobId, 'InProgress', null)
  }

  hold(jobId: string, note: string): Promise<JobDetails> {
    return this.changeStatus(jobId, 'OnHold', note)
  }

  resume(jobId: string): Promise<JobDetails> {
    return this.changeStatus(jobId, 'InProgress', null)
  }

  async assignedJobs(view: AssignedJobsView): Promise<AssignedJob[]> {
    const response = await this.get(`/api/technicians/me/jobs?view=${view}`)
    if (!response.ok()) {
      const problem = await readProblem(response)
      throw new Error(`GET /api/technicians/me/jobs failed with ${problem.status} ${problem.code}.`)
    }
    return ((await response.json()) as { items: AssignedJob[] }).items
  }

  getJob(id: string): Promise<APIResponse> {
    return this.get(`/api/jobs/${id}`)
  }

  getInspection(jobId: string): Promise<APIResponse> {
    return this.get(`/api/jobs/${jobId}/inspection`)
  }

  saveInspection(jobId: string, body: SaveInspectionBody): Promise<APIResponse> {
    return this.request.put(`/api/jobs/${jobId}/inspection`, {
      headers: { Authorization: `Bearer ${this.accessToken}` },
      data: body,
    })
  }

  submitJob(jobId: string, body: SubmitJobBody): Promise<APIResponse> {
    return this.request.post(`/api/jobs/${jobId}/submission`, {
      headers: { Authorization: `Bearer ${this.accessToken}` },
      data: body,
    })
  }

  requestSocketTicket(): Promise<APIResponse> {
    return this.request.post(socketTicketsPath, {
      headers: { Authorization: `Bearer ${this.accessToken}`, ...csrfHeader },
    })
  }
}

export async function signInTechnician(
  request: APIRequestContext,
  email: string,
  sink: SentinelSink,
): Promise<TechnicianApi> {
  const session = await readSession(
    await signIn(request, email, 'Mobile', sink),
    'A technician sign-in with the mobile client',
  )
  if (session.user.role !== 'Technician') {
    throw new Error('The technician sign-in returned another role.')
  }
  if (session.refreshToken === null) {
    throw new Error('The mobile sign-in returned no refresh token.')
  }
  return new TechnicianApi(request, session.user, session.accessToken, session.refreshToken)
}
