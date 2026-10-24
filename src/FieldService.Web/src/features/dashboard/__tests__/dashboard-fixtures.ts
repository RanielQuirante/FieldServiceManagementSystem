import { dana, demoToday, onDemoDay } from '@/features/jobs/__tests__/job-fixtures'

import type {
  DashboardActivityEntry,
  DashboardSummary,
  DashboardUnassignedJob,
} from '../dashboard-api'
import { dashboardWindow } from '../dashboard-window'

const priyaReference = {
  id: '01a0f6b1-3a08-7fbf-89df-c40260fc01fa',
  displayName: 'Priya Natarajan',
}

const tomReference = { id: '01a0f6b1-3238-7ae6-a3c8-a68e544219c1', displayName: 'Tom Becker' }

const marcoReference = { id: '01a0f6b1-2a68-77d9-8aa0-8a58bef6a99e', displayName: 'Marco Reyes' }

const aishaReference = { id: '01a0f6b1-2e50-70c1-9f96-2344e84e3210', displayName: 'Aisha Karim' }

export function activityEntry(
  overrides: Partial<DashboardActivityEntry> = {},
): DashboardActivityEntry {
  return {
    id: '01a1549c-bac0-7dd7-887f-51d55ef3411e',
    occurredAt: onDemoDay(0, 10, 42),
    action: 'Started',
    jobId: '01a153ec-f2c0-7499-a012-b2be45d90129',
    jobNumber: 1049,
    fromStatus: 'Assigned',
    toStatus: 'InProgress',
    actor: priyaReference,
    technician: null,
    note: null,
    ...overrides,
  }
}

export const demoActivity: readonly DashboardActivityEntry[] = [
  activityEntry(),
  activityEntry({
    id: '01a15492-a8a0-7883-a05e-b6bbfe34d3c7',
    occurredAt: onDemoDay(0, 10, 31),
    action: 'PutOnHold',
    jobId: '01a153da-a340-7b8f-b0eb-d9e42e79e054',
    jobNumber: 1044,
    fromStatus: 'InProgress',
    toStatus: 'OnHold',
    actor: tomReference,
    note: 'Waiting for replacement gas valve',
  }),
  activityEntry({
    id: '01a15488-9680-7a4a-9498-8e59978d42cc',
    occurredAt: onDemoDay(0, 10, 20),
    action: 'Created',
    jobId: '01a15488-9680-7989-92af-ed07fc34a890',
    jobNumber: 1051,
    fromStatus: null,
    toStatus: 'Unassigned',
    actor: dana,
  }),
  activityEntry({
    id: '01a1547a-dae0-7492-9ee8-6b6ebed01c3f',
    occurredAt: onDemoDay(0, 10, 5),
    action: 'Assigned',
    jobId: '01a1546d-1f40-74c9-b6a6-b365b1422136',
    jobNumber: 1047,
    fromStatus: 'Unassigned',
    toStatus: 'Assigned',
    actor: dana,
    technician: marcoReference,
  }),
  activityEntry({
    id: '01a15474-7240-7bde-ac0d-e24ee480a82d',
    occurredAt: onDemoDay(0, 9, 58),
    action: 'Completed',
    jobId: '01a153d1-7b80-7a19-bf23-15548395a552',
    jobNumber: 1043,
    fromStatus: 'InProgress',
    toStatus: 'Completed',
    actor: aishaReference,
  }),
  activityEntry({
    id: '01a1544a-5500-71c6-b856-aa0a8dd0fb9c',
    occurredAt: onDemoDay(0, 9, 12),
    action: 'Started',
    jobId: '01a15415-3b40-79a4-859f-2cf627f64d02',
    jobNumber: 1042,
    fromStatus: 'Assigned',
    toStatus: 'InProgress',
    actor: marcoReference,
  }),
]

export const demoNeedsTechnician: readonly DashboardUnassignedJob[] = [
  {
    id: '01a15424-cba0-7324-8dfe-aec14fd87c8b',
    number: 1052,
    title: 'Fire alarm panel trouble signal',
    priority: 'Urgent',
    customerName: 'Cedar Ridge Elementary School',
    siteAddress: '1180 Cedar Ridge Drive',
    scheduledStart: onDemoDay(0, 14, 0),
    scheduledEnd: onDemoDay(0, 15, 30),
  },
  {
    id: '01a15488-9680-7989-92af-ed07fc34a890',
    number: 1051,
    title: 'Replace faulty breaker panel',
    priority: 'High',
    customerName: 'Pinecrest Public Library',
    siteAddress: '300 Pinecrest Road',
    scheduledStart: onDemoDay(0, 15, 30),
    scheduledEnd: onDemoDay(0, 17, 0),
  },
  {
    id: '01a14ff4-b680-72b1-b5c0-7f2fe309d6f0',
    number: 1050,
    title: 'Emergency lighting test',
    priority: 'Low',
    customerName: 'Northgate Logistics Warehouse',
    siteAddress: '42 Industrial Parkway, Dock 4',
    scheduledStart: onDemoDay(2, 9, 0),
    scheduledEnd: onDemoDay(2, 11, 0),
  },
  {
    id: '01a15062-9380-7aa7-8e25-c4fbbaea52ab',
    number: 1054,
    title: 'Leaking rooftop drain',
    priority: 'Normal',
    customerName: 'Harborview Apartments',
    siteAddress: '600 Bayfront Avenue',
    scheduledStart: onDemoDay(3, 10, 0),
    scheduledEnd: onDemoDay(3, 12, 0),
  },
]

export function demoSummary(
  overrides: Partial<DashboardSummary> = {},
  now: Date = demoToday,
): DashboardSummary {
  const period = dashboardWindow(now)
  return {
    generatedAt: now.toISOString(),
    openJobs: {
      total: 17,
      unassigned: 4,
      assigned: 8,
      inProgress: 2,
      onHold: 3,
      withTechnician: 13,
      inProgressTechnicianCount: 2,
      nextAssignedStart: onDemoDay(0, 11, 30),
      onHoldSince: onDemoDay(-3, 13, 50),
    },
    range: {
      from: period.from,
      to: period.to,
      created: 5,
      completed: 2,
      cancelled: 0,
      unassignedScheduled: 2,
      lastCompletedAt: onDemoDay(0, 9, 58),
    },
    technicians: { total: 6, onShift: 5 },
    needsTechnician: [...demoNeedsTechnician],
    recentActivity: [...demoActivity],
    ...overrides,
  }
}

export function withNewActivity(
  summary: DashboardSummary,
  entries: readonly DashboardActivityEntry[],
): DashboardSummary {
  return {
    ...summary,
    recentActivity: [...entries, ...summary.recentActivity].slice(0, 6),
  }
}

export function startedEntry(jobNumber: number, minute: number): DashboardActivityEntry {
  return activityEntry({
    id: `01a154a0-0000-7000-8000-${String(jobNumber).padStart(12, '0')}`,
    occurredAt: onDemoDay(0, 10, minute),
    jobId: `01a154a0-0000-7000-9000-${String(jobNumber).padStart(12, '0')}`,
    jobNumber,
    actor: marcoReference,
  })
}
