import type { JobStatus } from '@/shared/format/labels'

import type {
  ChecklistTemplate,
  JobDetails,
  JobHistoryEntry,
  JobList,
  JobSummary,
  SubmitAffordance,
  Technician,
  TechnicianJobReference,
} from '../jobs-api'

export const demoToday = new Date(2026, 9, 19, 10, 42)

export function administratorSubmit(answered = 0, total = 8): SubmitAffordance {
  return { allowed: false, blockingCode: 'auth.forbidden', answered, total }
}

export function readOnlyAffordances(): JobDetails['affordances'] {
  return {
    allowedStatusChanges: [],
    canSubmit: administratorSubmit(),
    canAssign: false,
    canCancel: false,
  }
}

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

function jobReference(
  number: number,
  status: TechnicianJobReference['status'],
  start: string,
  end: string,
): TechnicianJobReference {
  return {
    id: `01a15400-0000-7000-8000-00000000${number}`,
    number,
    status,
    scheduledStart: start,
    scheduledEnd: end,
  }
}

export function technician(overrides: Partial<Technician> = {}): Technician {
  return {
    id: '01a0f6b1-3a08-7fbf-89df-c40260fc01fa',
    displayName: 'Aisha Karim',
    email: 'aisha.karim@fieldservice.example',
    phoneNumber: '(555) 014-7810',
    trade: 'Electrical',
    isActive: true,
    isOnShift: true,
    availableForAssignment: true,
    openJobCount: 3,
    currentJob: null,
    nextJob: jobReference(1055, 'Assigned', onDemoDay(0, 13, 0), onDemoDay(0, 14, 30)),
    ...overrides,
  }
}

export const aishaTechnician = technician()

export const lenaTechnician = technician({
  id: '01a0f6b1-3620-7891-8f84-8b8ca5e5e243',
  displayName: 'Lena Park',
  email: 'lena.park@fieldservice.example',
  phoneNumber: '(555) 014-5521',
  trade: 'FireSafety',
  openJobCount: 1,
  nextJob: jobReference(1045, 'Assigned', onDemoDay(0, 11, 30), onDemoDay(0, 13, 0)),
})

export const marcoTechnician = technician({
  id: marco.id,
  displayName: marco.displayName,
  email: 'marco.reyes@fieldservice.example',
  phoneNumber: '(555) 014-2231',
  trade: 'Hvac',
  openJobCount: 3,
  currentJob: jobReference(1042, 'InProgress', onDemoDay(0, 9, 0), onDemoDay(0, 11, 0)),
  nextJob: jobReference(1047, 'Assigned', onDemoDay(0, 13, 30), onDemoDay(0, 15, 0)),
})

export const priyaTechnician = technician({
  id: priya.id,
  displayName: priya.displayName,
  email: 'priya.natarajan@fieldservice.example',
  phoneNumber: '(555) 014-6604',
  trade: 'Electrical',
  openJobCount: 2,
  currentJob: jobReference(1049, 'InProgress', onDemoDay(0, 10, 0), onDemoDay(0, 12, 0)),
  nextJob: jobReference(1046, 'Assigned', onDemoDay(1, 9, 0), onDemoDay(1, 10, 30)),
})

export const samuelTechnician = technician({
  id: '01a0f6b1-3df0-7802-8952-7b3a85cffa38',
  displayName: 'Samuel Osei',
  email: 'samuel.osei@fieldservice.example',
  phoneNumber: '(555) 014-9148',
  trade: 'Hvac',
  isOnShift: false,
  availableForAssignment: false,
  openJobCount: 0,
  currentJob: null,
  nextJob: null,
})

export const tomTechnician = technician({
  id: tom.id,
  displayName: tom.displayName,
  email: 'tom.becker@fieldservice.example',
  phoneNumber: '(555) 014-3392',
  trade: 'Plumbing',
  openJobCount: 4,
  currentJob: jobReference(1044, 'OnHold', onDemoDay(0, 8, 0), onDemoDay(0, 10, 0)),
  nextJob: jobReference(1048, 'Assigned', onDemoDay(1, 8, 0), onDemoDay(1, 9, 30)),
})

export const demoTechnicians: readonly Technician[] = [
  aishaTechnician,
  lenaTechnician,
  marcoTechnician,
  priyaTechnician,
  samuelTechnician,
  tomTechnician,
]

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
    affordances: {
      allowedStatusChanges: [],
      canSubmit: administratorSubmit(),
      canAssign: true,
      canCancel: true,
    },
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
    affordances: {
      allowedStatusChanges: [],
      canSubmit: administratorSubmit(6, 10),
      canAssign: false,
      canCancel: true,
    },
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
    affordances: { ...readOnlyAffordances(), canCancel: true },
  })
}

export const holdNote = 'Waiting for the replacement fan motor quote to be approved.'

export function statusChangedJob(note: string = holdNote): JobDetails {
  const byMarco = { id: marco.id, displayName: marco.displayName }
  return jobDetails({
    id: '01a1543c-6f40-7a2e-9b1d-5d0c3e8f1047',
    number: 1047,
    title: 'Walk-in freezer temperature alarm',
    description:
      'The walk-in freezer alarm keeps sounding and the display reads above the set point. Staff moved the stock to the reach-in freezers.',
    priority: 'Urgent',
    status: 'InProgress',
    customerName: 'Elm Street Bakery',
    siteAddress: '88 Elm Street',
    siteAccessNotes: null,
    contactName: null,
    contactPhone: '(555) 014-4417',
    scheduledStart: onDemoDay(0, 13, 30),
    scheduledEnd: onDemoDay(0, 15, 0),
    checklistTemplate: {
      id: '01a15175-3c00-7000-8000-000102000000',
      name: 'Rooftop HVAC service',
      trade: 'Hvac',
    },
    assignedTechnician: marco,
    createdAt: onDemoDay(0, 8, 47),
    updatedAt: onDemoDay(0, 10, 38),
    assignedAt: onDemoDay(0, 10, 5),
    startedAt: onDemoDay(0, 10, 12),
    history: [
      historyEntry({ id: 'h-1047-1', occurredAt: onDemoDay(0, 8, 47) }),
      historyEntry({
        id: 'h-1047-2',
        action: 'Assigned',
        fromStatus: 'Unassigned',
        toStatus: 'Assigned',
        technician: byMarco,
        occurredAt: onDemoDay(0, 10, 5),
      }),
      historyEntry({
        id: 'h-1047-3',
        action: 'Started',
        fromStatus: 'Assigned',
        toStatus: 'InProgress',
        actor: byMarco,
        occurredAt: onDemoDay(0, 10, 12),
      }),
      historyEntry({
        id: 'h-1047-4',
        action: 'PutOnHold',
        fromStatus: 'InProgress',
        toStatus: 'OnHold',
        actor: byMarco,
        note,
        occurredAt: onDemoDay(0, 10, 25),
      }),
      historyEntry({
        id: 'h-1047-5',
        action: 'Resumed',
        fromStatus: 'OnHold',
        toStatus: 'InProgress',
        actor: byMarco,
        occurredAt: onDemoDay(0, 10, 38),
      }),
    ],
    version: 48300,
    affordances: {
      allowedStatusChanges: [],
      canSubmit: administratorSubmit(0, 10),
      canAssign: false,
      canCancel: true,
    },
  })
}
