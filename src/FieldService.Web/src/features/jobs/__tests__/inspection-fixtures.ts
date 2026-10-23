import type { InspectionResult } from '@/shared/format/labels'

import type { Inspection, InspectionAnswer, InspectionItem, JobDetails } from '../jobs-api'
import {
  administratorSubmit,
  assignedInProgressJob,
  dana,
  historyEntry,
  jobDetails,
  marco,
  onDemoDay,
  readOnlyAffordances,
  tom,
} from './job-fixtures'

type ReadingField = InspectionItem['readingFields'][number]

type Reading = InspectionAnswer['readings'][number]

export const rooftopTemplate = {
  id: '01a15175-3c00-7000-8000-000102000000',
  name: 'Rooftop HVAC service',
  trade: 'Hvac',
} as const

export const boilerTemplate = {
  id: '01a15175-3c00-7000-8000-000103000000',
  name: 'Boiler service',
  trade: 'Plumbing',
} as const

export const suctionField = '01a15175-3c00-7000-8000-000302030100'

export const dischargeField = '01a15175-3c00-7000-8000-000302030200'

export const supplyAirField = '01a15175-3c00-7000-8000-000302080100'

export const systemPressureField = '01a15175-3c00-7000-8000-000303030100'

export const fanMotorNote = 'Bearing noise at start-up. Recommend motor replacement within 30 days.'

export const rooftopNotes =
  'Recharged the system to spec and cleaned the condenser coil. Unit is holding 54 °F supply air. Fan motor bearing is noisy; replacement quote requested.'

export const boilerNotes =
  'Pressure drop traced to a weeping fitting on the return line. Fitting tightened, system refilled to 18 psi and holding.'

export const cancelNote =
  'Building management replaced the filters under its own maintenance contract.'

function entryId(number: number, index: number): string {
  return `01a15400-0000-7000-8000-0000${number}000${index}`
}

const byMarco = { id: marco.id, displayName: marco.displayName }

const byTom = { id: tom.id, displayName: tom.displayName }

export function readingField(
  readingFieldId: string,
  position: number,
  label: string,
  unit: string,
): ReadingField {
  return { readingFieldId, position, label, unit }
}

export function reading(readingFieldId: string, value: number): Reading {
  return { readingFieldId, value }
}

export function answer(
  result: InspectionResult,
  answeredAt: string,
  note: string | null = null,
  readings: readonly Reading[] = [],
): InspectionAnswer {
  return { result, note, readings: [...readings], answeredAt }
}

function item(
  checklistItemId: string,
  position: number,
  prompt: string,
  readingFields: readonly ReadingField[] = [],
  saved: InspectionAnswer | null = null,
): InspectionItem {
  return { checklistItemId, position, prompt, readingFields: [...readingFields], answer: saved }
}

export function progressOf(items: readonly InspectionItem[]): Inspection['progress'] {
  const count = (result: InspectionResult) =>
    items.filter((entry) => entry.answer?.result === result).length
  return {
    answered: items.filter((entry) => entry.answer !== null).length,
    total: items.length,
    passCount: count('Pass'),
    failCount: count('Fail'),
    notApplicableCount: count('NotApplicable'),
  }
}

export function inspection(overrides: Partial<Inspection> & Pick<Inspection, 'items'>): Inspection {
  const progress = progressOf(overrides.items)
  return {
    jobId: '01a15415-3b40-79a4-859f-2cf627f64d02',
    jobNumber: 1042,
    jobStatus: 'InProgress',
    template: rooftopTemplate,
    progress,
    startedAt: null,
    completedAt: null,
    updatedAt: null,
    submittedAt: null,
    technicianNotes: null,
    version: 48213,
    affordances: {
      canEdit: false,
      canSubmit: administratorSubmit(progress.answered, progress.total),
    },
    ...overrides,
  }
}

const rooftopIds = Array.from(
  { length: 10 },
  (_, index) => `01a15175-3c00-7000-8000-000202${String(index + 1).padStart(2, '0')}0000`,
)

function rooftopId(position: number): string {
  return rooftopIds[position - 1] ?? ''
}

export function rooftopItems(stage: 'blank' | 'draft' | 'final'): InspectionItem[] {
  const firstSave = onDemoDay(0, 9, 31)
  const secondSave = onDemoDay(0, 9, 50)
  const finalSave = onDemoDay(0, 10, 57)
  const draft = stage !== 'blank'
  const final = stage === 'final'
  return [
    item(
      rooftopId(1),
      1,
      'Lock out and tag out unit power',
      [],
      draft ? answer('Pass', firstSave) : null,
    ),
    item(
      rooftopId(2),
      2,
      'Replace return air filters',
      [],
      draft ? answer('Pass', firstSave) : null,
    ),
    item(
      rooftopId(3),
      3,
      'Check refrigerant pressures',
      [
        readingField(suctionField, 1, 'Suction', 'psi'),
        readingField(dischargeField, 2, 'Discharge', 'psi'),
      ],
      draft
        ? answer('Pass', firstSave, null, [
            reading(dischargeField, 285.0),
            reading(suctionField, 118.0),
          ])
        : null,
    ),
    item(rooftopId(4), 4, 'Clean condenser coil', [], draft ? answer('Pass', secondSave) : null),
    item(
      rooftopId(5),
      5,
      'Inspect condenser fan motor and bearings',
      [],
      draft ? answer('Fail', secondSave, fanMotorNote) : null,
    ),
    item(
      rooftopId(6),
      6,
      'Check belts and pulleys',
      [],
      draft ? answer('NotApplicable', secondSave, 'Direct-drive unit') : null,
    ),
    item(
      rooftopId(7),
      7,
      'Clear condensate drain line',
      [],
      final ? answer('Pass', finalSave) : null,
    ),
    item(
      rooftopId(8),
      8,
      'Record supply air temperature',
      [readingField(supplyAirField, 1, 'Supply air temperature', '°F')],
      final ? answer('Pass', finalSave, null, [reading(supplyAirField, 54.0)]) : null,
    ),
    item(
      rooftopId(9),
      9,
      'Test thermostat and economizer controls',
      [],
      final ? answer('Pass', finalSave) : null,
    ),
    item(
      rooftopId(10),
      10,
      'Restore power and confirm normal operation',
      [],
      final ? answer('Pass', finalSave) : null,
    ),
  ]
}

export function rooftopItem(stage: 'blank' | 'draft' | 'final', position: number): InspectionItem {
  const found = rooftopItems(stage).find((entry) => entry.position === position)
  if (found === undefined) {
    throw new Error(`Rooftop HVAC service has no item ${position}`)
  }
  return found
}

export function rooftopInProgress(): Inspection {
  return inspection({
    items: rooftopItems('draft'),
    startedAt: onDemoDay(0, 9, 12),
    updatedAt: onDemoDay(0, 9, 50),
  })
}

export function rooftopSubmitted(): Inspection {
  return inspection({
    jobStatus: 'Completed',
    items: rooftopItems('final'),
    startedAt: onDemoDay(0, 9, 12),
    completedAt: onDemoDay(0, 10, 58),
    updatedAt: onDemoDay(0, 10, 58),
    submittedAt: onDemoDay(0, 10, 58),
    technicianNotes: rooftopNotes,
    version: 48297,
  })
}

export function completedRooftopJob(): JobDetails {
  const job = assignedInProgressJob()
  return {
    ...job,
    status: 'Completed',
    updatedAt: onDemoDay(0, 10, 58),
    completedAt: onDemoDay(0, 10, 58),
    history: [
      ...job.history,
      historyEntry({
        id: '01a15460-7a80-7c01-8e4d-3f2a9b6c1042',
        action: 'Completed',
        fromStatus: 'InProgress',
        toStatus: 'Completed',
        actor: byMarco,
        occurredAt: onDemoDay(0, 10, 58),
      }),
    ],
    version: 48297,
    affordances: readOnlyAffordances(),
  }
}

export const boilerJobId = '01a15400-0000-7000-8000-000000001039'

function boilerId(position: number): string {
  return `01a15175-3c00-7000-8000-0002030${position}0000`
}

export function boilerItems(): InspectionItem[] {
  const firstSave = onDemoDay(0, 7, 48)
  const secondSave = onDemoDay(0, 8, 49)
  return [
    item(boilerId(1), 1, 'Isolate gas and electrical supply', [], answer('Pass', firstSave)),
    item(
      boilerId(2),
      2,
      'Inspect valves and fittings for leaks',
      [],
      answer('Pass', firstSave, 'Tightened a weeping fitting on the return line'),
    ),
    item(
      boilerId(3),
      3,
      'Check system pressure',
      [readingField(systemPressureField, 1, 'System pressure', 'psi')],
      answer('Pass', firstSave, null, [reading(systemPressureField, 18.0)]),
    ),
    item(boilerId(4), 4, 'Test pressure relief valve', [], answer('Pass', firstSave)),
    item(
      boilerId(5),
      5,
      'Inspect expansion tank',
      [],
      answer('Pass', secondSave, 'Recharged to 12 psi'),
    ),
    item(boilerId(6), 6, 'Check burner flame and combustion', [], answer('Pass', secondSave)),
    item(
      boilerId(7),
      7,
      'Test low-water cutoff',
      [],
      answer('NotApplicable', secondSave, 'Not fitted on this model'),
    ),
    item(boilerId(8), 8, 'Restore supply and confirm operation', [], answer('Pass', secondSave)),
  ]
}

export function boilerJob(): JobDetails {
  return jobDetails({
    id: boilerJobId,
    number: 1039,
    title: 'Boiler pressure drop',
    description:
      'Boiler pressure falls overnight and the heating cuts out before the practice opens.',
    priority: 'High',
    status: 'Completed',
    customerName: 'Lakeside Dental',
    siteAddress: '9 Lakeside Court',
    siteAccessNotes: null,
    contactName: null,
    contactPhone: null,
    equipment: null,
    scheduledStart: onDemoDay(0, 7, 0),
    scheduledEnd: onDemoDay(0, 8, 30),
    checklistTemplate: boilerTemplate,
    assignedTechnician: tom,
    createdBy: dana,
    createdAt: onDemoDay(0, 6, 58),
    updatedAt: onDemoDay(0, 8, 52),
    assignedAt: onDemoDay(0, 7, 0),
    startedAt: onDemoDay(0, 7, 5),
    completedAt: onDemoDay(0, 8, 52),
    history: [
      historyEntry({ id: entryId(1039, 1), occurredAt: onDemoDay(0, 6, 58) }),
      historyEntry({
        id: entryId(1039, 2),
        action: 'Assigned',
        fromStatus: 'Unassigned',
        toStatus: 'Assigned',
        technician: byTom,
        occurredAt: onDemoDay(0, 7, 0),
      }),
      historyEntry({
        id: entryId(1039, 3),
        action: 'Started',
        fromStatus: 'Assigned',
        toStatus: 'InProgress',
        actor: byTom,
        occurredAt: onDemoDay(0, 7, 5),
      }),
      historyEntry({
        id: entryId(1039, 4),
        action: 'Completed',
        fromStatus: 'InProgress',
        toStatus: 'Completed',
        actor: byTom,
        occurredAt: onDemoDay(0, 8, 52),
      }),
    ],
    version: 47990,
    affordances: readOnlyAffordances(),
  })
}

export function boilerInspection(): Inspection {
  return inspection({
    jobId: boilerJobId,
    jobNumber: 1039,
    jobStatus: 'Completed',
    template: boilerTemplate,
    items: boilerItems(),
    startedAt: onDemoDay(0, 7, 5),
    completedAt: onDemoDay(0, 8, 52),
    updatedAt: onDemoDay(0, 8, 52),
    submittedAt: onDemoDay(0, 8, 52),
    technicianNotes: boilerNotes,
    version: 47990,
  })
}

export const freezerJobId = '01a1546d-1f40-74c9-b6a6-b365b1422136'

export function assignedFreezerJob(): JobDetails {
  return jobDetails({
    id: freezerJobId,
    number: 1047,
    title: 'Walk-in freezer temperature alarm',
    description:
      'The walk-in freezer alarm keeps sounding and the display reads above the set point. Staff moved the stock to the reach-in freezers.',
    priority: 'Urgent',
    status: 'Assigned',
    customerName: 'Elm Street Bakery',
    siteAddress: '88 Elm Street',
    siteAccessNotes: null,
    contactName: null,
    contactPhone: '(555) 014-4417',
    scheduledStart: onDemoDay(0, 13, 30),
    scheduledEnd: onDemoDay(0, 15, 0),
    checklistTemplate: rooftopTemplate,
    assignedTechnician: marco,
    createdAt: onDemoDay(0, 8, 47),
    updatedAt: onDemoDay(0, 10, 5),
    assignedAt: onDemoDay(0, 10, 5),
    history: [
      historyEntry({ id: entryId(1047, 1), occurredAt: onDemoDay(0, 8, 47) }),
      historyEntry({
        id: entryId(1047, 2),
        action: 'Assigned',
        fromStatus: 'Unassigned',
        toStatus: 'Assigned',
        technician: byMarco,
        occurredAt: onDemoDay(0, 10, 5),
      }),
    ],
    version: 48190,
    affordances: readOnlyAffordances(),
  })
}

export function blankFreezerInspection(): Inspection {
  return inspection({
    jobId: freezerJobId,
    jobNumber: 1047,
    jobStatus: 'Assigned',
    items: rooftopItems('blank'),
    version: 48190,
  })
}

export const filterJobId = '01a15300-0000-7000-8000-000000001036'

export function cancelledFilterJob(): JobDetails {
  return jobDetails({
    id: filterJobId,
    number: 1036,
    title: 'Office AC filter replacement',
    description: 'Replace the filters on the office split units on Floor 4.',
    priority: 'Low',
    status: 'Cancelled',
    customerName: 'Summit Coworking',
    siteAddress: '12 Summit Plaza, Floor 4',
    siteAccessNotes: null,
    contactName: null,
    contactPhone: null,
    scheduledStart: onDemoDay(-3, 14, 0),
    scheduledEnd: onDemoDay(-3, 15, 0),
    checklistTemplate: rooftopTemplate,
    assignedTechnician: marco,
    createdAt: onDemoDay(-4, 9, 40),
    updatedAt: onDemoDay(-3, 9, 20),
    assignedAt: onDemoDay(-4, 9, 45),
    cancelledAt: onDemoDay(-3, 9, 20),
    history: [
      historyEntry({ id: entryId(1036, 1), occurredAt: onDemoDay(-4, 9, 40) }),
      historyEntry({
        id: entryId(1036, 2),
        action: 'Assigned',
        fromStatus: 'Unassigned',
        toStatus: 'Assigned',
        technician: byMarco,
        occurredAt: onDemoDay(-4, 9, 45),
      }),
      historyEntry({
        id: entryId(1036, 3),
        action: 'Cancelled',
        fromStatus: 'Assigned',
        toStatus: 'Cancelled',
        note: cancelNote,
        occurredAt: onDemoDay(-3, 9, 20),
      }),
    ],
    version: 47710,
    affordances: readOnlyAffordances(),
  })
}

export function blankFilterInspection(): Inspection {
  return inspection({
    jobId: filterJobId,
    jobNumber: 1036,
    jobStatus: 'Cancelled',
    items: rooftopItems('blank'),
    version: 47710,
  })
}
