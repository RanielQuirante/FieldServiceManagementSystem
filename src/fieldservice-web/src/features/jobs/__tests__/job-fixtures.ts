import type { JobStatus } from '@/shared/format/labels'

import type {
  ChecklistTemplate,
  JobDetails,
  JobHistoryEntry,
  JobList,
  JobSummary,
} from '../jobs-api'

export const demoToday = new Date(2026, 9, 19, 10, 42)

export function onDemoDay(dayOffset: number, hours: number, minutes: number): string {
  return new Date(2026, 9, 19 + dayOffset, hours, minutes).toISOString()
}

export const dana = { id: '01a0f6b1-2680-7152-92a5-bb4d3a6df7ca', displayName: 'Dana Whitfield' }

export const marco = {
  id: '01a0f6b1-2a68-77d9-8aa0-8a58bef6a99e',
  displayName: 'Marco Reyes',
  trade: 'Hvac',
} as const

export const priya = {
  id: '01a0f6b1-2e50-70c1-9f96-2344e84e3210',
  displayName: 'Priya Natarajan',
  trade: 'Electrical',
} as const

export const tom = {
  id: '01a0f6b1-3000-7000-8000-000000000003',
  displayName: 'Tom Becker',
  trade: 'Plumbing',
} as const

export const checklistTemplates: readonly ChecklistTemplate[] = [
  {
    id: '01a15175-3c00-7000-8000-000103000000',
    name: 'Boiler service',
    trade: 'Plumbing',
    itemCount: 8,
  },
  {
    id: '01a15175-3c00-7000-8000-000105000000',
    name: 'Electrical panel inspection',
    trade: 'Electrical',
    itemCount: 8,
  },
  {
    id: '01a15175-3c00-7000-8000-000104000000',
    name: 'Fire extinguisher inspection',
    trade: 'FireSafety',
    itemCount: 7,
  },
  {
    id: '01a15175-3c00-7000-8000-000101000000',
    name: 'General electrical inspection',
    trade: 'Electrical',
    itemCount: 8,
  },
  {
    id: '01a15175-3c00-7000-8000-000102000000',
    name: 'Rooftop HVAC service',
    trade: 'Hvac',
    itemCount: 10,
  },
]

export function jobSummary(overrides: Partial<JobSummary> = {}): JobSummary {
  return {
    id: '01a15488-9680-7989-92af-ed07fc34a890',
    number: 1051,
    title: 'Replace faulty breaker panel',
    priority: 'High',
    status: 'Unassigned',
    customerName: 'Pinecrest Public Library',
    siteAddress: '300 Pinecrest Road',
    scheduledStart: onDemoDay(0, 15, 30),
    scheduledEnd: onDemoDay(0, 17, 0),
    assignedTechnician: null,
    updatedAt: onDemoDay(0, 10, 20),
    version: 48101,
    ...overrides,
  }
}

export function demoSummaries(count: number): JobSummary[] {
  return Array.from({ length: count }, (_, index) =>
    jobSummary({
      id: `01a15488-9680-7989-92af-${String(index).padStart(12, '0')}`,
      number: 1055 - index,
      title: `Demo job ${1055 - index}`,
    }),
  )
}

export const demoStatusCounts: Readonly<Record<JobStatus, number>> = {
  Unassigned: 4,
  Assigned: 8,
  InProgress: 2,
  OnHold: 3,
  Completed: 2,
  Cancelled: 1,
}

export function jobList(items: readonly JobSummary[], overrides: Partial<JobList> = {}): JobList {
  return {
    items: [...items],
    page: 1,
    pageSize: 12,
    totalCount: 20,
    statusCounts: Object.entries(demoStatusCounts).map(([status, count]) => ({
      status: status as JobStatus,
      count,
    })),
    ...overrides,
  }
}

export function historyEntry(overrides: Partial<JobHistoryEntry> = {}): JobHistoryEntry {
  return {
    id: '01a15488-9680-71ae-8e7f-1896175a9d87',
    action: 'Created',
    fromStatus: null,
    toStatus: 'Unassigned',
    actor: dana,
    technician: null,
    note: null,
    occurredAt: onDemoDay(0, 10, 20),
    ...overrides,
  }
}

export function jobDetails(overrides: Partial<JobDetails> = {}): JobDetails {
  return {
    id: '01a15488-9680-7989-92af-ed07fc34a890',
    number: 1051,
    title: 'Replace faulty breaker panel',
    description:
      'Main breaker panel trips under load. Several 20 A breakers are warm to the touch. The library closes to the public at 5 PM.',
    priority: 'High',
    status: 'Unassigned',
    customerName: 'Pinecrest Public Library',
    siteAddress: '300 Pinecrest Road',
    siteAccessNotes:
      'Main electrical room, basement level. Building supervisor will meet you at the loading door.',
    contactName: 'Owen Mitchell',
    contactPhone: '(555) 014-3307',
    equipment: null,
    scheduledStart: onDemoDay(0, 15, 30),
    scheduledEnd: onDemoDay(0, 17, 0),
    checklistTemplate: {
      id: '01a15175-3c00-7000-8000-000105000000',
      name: 'Electrical panel inspection',
      trade: 'Electrical',
    },
    assignedTechnician: null,
    createdBy: dana,
    createdAt: onDemoDay(0, 10, 20),
    updatedAt: onDemoDay(0, 10, 20),
    assignedAt: null,
    startedAt: null,
    completedAt: null,
    cancelledAt: null,
    history: [historyEntry()],
    version: 48101,
    ...overrides,
  }
}

export function assignedInProgressJob(): JobDetails {
  return jobDetails({
    id: '01a15415-3b40-79a4-859f-2cf627f64d02',
    number: 1042,
    title: 'Rooftop HVAC unit not cooling',
    description:
      'Rooftop unit RTU-2 runs but is not cooling. The waiting room is at 81°F. The customer reports the problem started on Saturday afternoon.',
    status: 'InProgress',
    customerName: 'Brightwater Medical Clinic',
    siteAddress: '214 Harbor Road, Suite 3',
    siteAccessNotes: 'Roof access through the east stairwell. Ask reception for the roof key.',
    contactName: 'Nina Alvarez',
    contactPhone: '(555) 014-6620',
    equipment: 'RTU-2, 10-ton packaged rooftop unit',
    scheduledStart: onDemoDay(0, 9, 0),
    scheduledEnd: onDemoDay(0, 11, 0),
    checklistTemplate: {
      id: '01a15175-3c00-7000-8000-000102000000',
      name: 'Rooftop HVAC service',
      trade: 'Hvac',
    },
    assignedTechnician: marco,
    createdAt: onDemoDay(0, 8, 14),
    updatedAt: onDemoDay(0, 9, 50),
    assignedAt: onDemoDay(0, 8, 20),
    startedAt: onDemoDay(0, 9, 12),
    history: [
      historyEntry({ id: '01a15415-3b40-77f2-9c94-972cf43879ed', occurredAt: onDemoDay(0, 8, 14) }),
      historyEntry({
        id: '01a1541a-b980-77ba-b9a7-db809866381b',
        action: 'Assigned',
        fromStatus: 'Unassigned',
        toStatus: 'Assigned',
        technician: { id: marco.id, displayName: marco.displayName },
        occurredAt: onDemoDay(0, 8, 20),
      }),
      historyEntry({
        id: '01a1544a-5500-71c6-b856-aa0a8dd0fb9c',
        action: 'Started',
        fromStatus: 'Assigned',
        toStatus: 'InProgress',
        actor: { id: marco.id, displayName: marco.displayName },
        occurredAt: onDemoDay(0, 9, 12),
      }),
    ],
    version: 48213,
  })
}

export function onHoldJob(): JobDetails {
  return jobDetails({
    id: '01a15062-0000-7000-8000-000000001037',
    number: 1037,
    title: 'Annual boiler service',
    description: 'Annual service of the central heating boiler before the heating season.',
    priority: 'Normal',
    status: 'OnHold',
    customerName: 'Harborview Apartments',
    siteAddress: '600 Bayfront Avenue',
    siteAccessNotes: null,
    contactName: null,
    contactPhone: null,
    scheduledStart: onDemoDay(-3, 13, 0),
    scheduledEnd: onDemoDay(-3, 15, 0),
    checklistTemplate: {
      id: '01a15175-3c00-7000-8000-000103000000',
      name: 'Boiler service',
      trade: 'Plumbing',
    },
    assignedTechnician: tom,
    createdAt: onDemoDay(-4, 10, 15),
    assignedAt: onDemoDay(-4, 10, 20),
    startedAt: onDemoDay(-3, 13, 5),
    history: [
      historyEntry({ id: 'h-1037-1', occurredAt: onDemoDay(-4, 10, 15) }),
      historyEntry({
        id: 'h-1037-2',
        action: 'Assigned',
        fromStatus: 'Unassigned',
        toStatus: 'Assigned',
        technician: { id: tom.id, displayName: tom.displayName },
        occurredAt: onDemoDay(-4, 10, 20),
      }),
      historyEntry({
        id: 'h-1037-3',
        action: 'Started',
        fromStatus: 'Assigned',
        toStatus: 'InProgress',
        actor: { id: tom.id, displayName: tom.displayName },
        occurredAt: onDemoDay(-3, 13, 5),
      }),
      historyEntry({
        id: 'h-1037-4',
        action: 'PutOnHold',
        fromStatus: 'InProgress',
        toStatus: 'OnHold',
        actor: { id: tom.id, displayName: tom.displayName },
        note: 'Waiting for the gas utility to restore supply to the boiler room.',
        occurredAt: onDemoDay(-3, 13, 50),
      }),
    ],
  })
}
