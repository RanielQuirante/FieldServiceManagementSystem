import { jobStatusLabel, type JobStatus } from '@/shared/format/labels'

import type { IconName } from './icons'

export type StatusTone =
  'unassigned' | 'assigned' | 'in-progress' | 'on-hold' | 'completed' | 'cancelled' | 'unknown'

export interface StatusAppearance {
  readonly label: string
  readonly icon: IconName
  readonly tone: StatusTone
}

const jobStatusLooks: Readonly<Record<JobStatus, Omit<StatusAppearance, 'label'>>> = {
  Unassigned: { icon: 'circle-dashed', tone: 'unassigned' },
  Assigned: { icon: 'user-check', tone: 'assigned' },
  InProgress: { icon: 'play-circle', tone: 'in-progress' },
  OnHold: { icon: 'pause-circle', tone: 'on-hold' },
  Completed: { icon: 'check-circle', tone: 'completed' },
  Cancelled: { icon: 'x-circle', tone: 'cancelled' },
}

export function jobStatusAppearance(status: string): StatusAppearance {
  const look = Object.prototype.hasOwnProperty.call(jobStatusLooks, status)
    ? jobStatusLooks[status as JobStatus]
    : { icon: 'circle-dashed' as const, tone: 'unknown' as const }
  return { label: jobStatusLabel(status), ...look }
}
