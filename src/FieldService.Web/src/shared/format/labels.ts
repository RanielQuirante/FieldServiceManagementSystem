import type { components } from '@/shared/api/schema.gen'

type Schemas = components['schemas']

export type JobStatus = Schemas['JobStatus']

export type JobPriority = Schemas['JobPriority']

export type Trade = Schemas['Trade']

export type UserRole = Schemas['UserRole']

export type JobHistoryAction = Schemas['JobHistoryAction']

export type InspectionResult = Schemas['InspectionResult']

export const inspectionResults: readonly InspectionResult[] = ['Pass', 'Fail', 'NotApplicable']

export const jobStatuses: readonly JobStatus[] = [
  'Unassigned',
  'Assigned',
  'InProgress',
  'OnHold',
  'Completed',
  'Cancelled',
]

export const jobPriorities: readonly JobPriority[] = ['Low', 'Normal', 'High', 'Urgent']

const jobStatusLabels: Readonly<Record<JobStatus, string>> = {
  Unassigned: 'Unassigned',
  Assigned: 'Assigned',
  InProgress: 'In progress',
  OnHold: 'On hold',
  Completed: 'Completed',
  Cancelled: 'Cancelled',
}

const jobPriorityLabels: Readonly<Record<JobPriority, string>> = {
  Low: 'Low',
  Normal: 'Normal',
  High: 'High',
  Urgent: 'Urgent',
}

const tradeLabels: Readonly<Record<Trade, string>> = {
  Hvac: 'HVAC',
  Electrical: 'Electrical',
  Plumbing: 'Plumbing',
  FireSafety: 'Fire safety',
}

const roleLabels: Readonly<Record<UserRole, string>> = {
  Administrator: 'Dispatcher',
  Technician: 'Technician',
}

const historyActionLabels: Readonly<Record<JobHistoryAction, string>> = {
  Created: 'Created',
  Assigned: 'Assigned',
  Started: 'Started',
  PutOnHold: 'Put on hold',
  Resumed: 'Resumed',
  Completed: 'Completed',
  Cancelled: 'Cancelled',
}

const inspectionResultLabels: Readonly<Record<InspectionResult, string>> = {
  Pass: 'Pass',
  Fail: 'Fail',
  NotApplicable: 'N/A',
}

function labelFrom<Key extends string>(
  labels: Readonly<Record<Key, string>>,
  value: string,
): string {
  return Object.prototype.hasOwnProperty.call(labels, value) ? labels[value as Key] : value
}

export function isJobStatus(value: unknown): value is JobStatus {
  return typeof value === 'string' && (jobStatuses as readonly string[]).includes(value)
}

export function isJobPriority(value: unknown): value is JobPriority {
  return typeof value === 'string' && (jobPriorities as readonly string[]).includes(value)
}

export function jobStatusLabel(status: string): string {
  return labelFrom(jobStatusLabels, status)
}

export function jobPriorityLabel(priority: string): string {
  return labelFrom(jobPriorityLabels, priority)
}

export function tradeLabel(trade: string): string {
  return labelFrom(tradeLabels, trade)
}

export function roleLabel(role: string): string {
  return labelFrom(roleLabels, role)
}

export function historyActionLabel(action: string): string {
  return labelFrom(historyActionLabels, action)
}

export function isInspectionResult(value: unknown): value is InspectionResult {
  return typeof value === 'string' && (inspectionResults as readonly string[]).includes(value)
}

export function inspectionResultLabel(result: string): string {
  return labelFrom(inspectionResultLabels, result)
}
