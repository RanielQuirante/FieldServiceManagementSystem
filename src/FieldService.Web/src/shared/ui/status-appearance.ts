import {
  inspectionResultLabel,
  isInspectionResult,
  jobStatusLabel,
  type InspectionResult,
  type JobStatus,
} from '@/shared/format/labels'

import type { IconName } from './icons'

export type StatusTone =
  'unassigned' | 'assigned' | 'in-progress' | 'on-hold' | 'completed' | 'cancelled' | 'unknown'

export interface StatusAppearance {
  readonly label: string
  readonly icon: IconName
  readonly tone: StatusTone
}

export type ResultTone = 'completed' | 'fail' | 'cancelled' | 'unknown'

export interface ResultAppearance {
  readonly label: string
  readonly icon: IconName
  readonly tone: ResultTone
}

const jobStatusLooks: Readonly<Record<JobStatus, Omit<StatusAppearance, 'label'>>> = {
  Unassigned: { icon: 'circle-dashed', tone: 'unassigned' },
  Assigned: { icon: 'user-check', tone: 'assigned' },
  InProgress: { icon: 'play-circle', tone: 'in-progress' },
  OnHold: { icon: 'pause-circle', tone: 'on-hold' },
  Completed: { icon: 'check-circle', tone: 'completed' },
  Cancelled: { icon: 'x-circle', tone: 'cancelled' },
}

const resultLooks: Readonly<Record<InspectionResult, Omit<ResultAppearance, 'label'>>> = {
  Pass: { icon: 'check', tone: 'completed' },
  Fail: { icon: 'x', tone: 'fail' },
  NotApplicable: { icon: 'minus', tone: 'cancelled' },
}

export function jobStatusAppearance(status: string): StatusAppearance {
  const look = Object.prototype.hasOwnProperty.call(jobStatusLooks, status)
    ? jobStatusLooks[status as JobStatus]
    : { icon: 'circle-dashed' as const, tone: 'unknown' as const }
  return { label: jobStatusLabel(status), ...look }
}

export function inspectionResultAppearance(result: string): ResultAppearance {
  const look = isInspectionResult(result)
    ? resultLooks[result]
    : { icon: 'circle-dashed' as const, tone: 'unknown' as const }
  return { label: inspectionResultLabel(result), ...look }
}
