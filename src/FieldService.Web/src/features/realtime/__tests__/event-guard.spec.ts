import { describe, expect, it } from 'vitest'

import { clientFixtureFolder, readClientFixtures } from '@/shared/testing/contract-fixtures'

import { isJobEventName, isJobEventPayload, jobEventNames } from '../realtime-events'

const fixtures = readClientFixtures()

const statusChanged = fixtures.find((fixture) => fixture.name === 'job.status-changed')?.payload

function mutated(changes: Record<string, unknown>): Record<string, unknown> {
  return { ...statusChanged, ...changes }
}

function without(member: string): Record<string, unknown> {
  const copy: Record<string, unknown> = { ...statusChanged }
  Reflect.deleteProperty(copy, member)
  return copy
}

describe('the client fixtures', () => {
  it('are read from docs/contracts/events/client relative to the package root', () => {
    expect(clientFixtureFolder.replace(/\\/g, '/')).toMatch(/\/docs\/contracts\/events\/client$/)
    expect(fixtures.map((fixture) => fixture.name)).toEqual([...jobEventNames].sort())
  })

  it('carry exactly the six payload members', () => {
    for (const fixture of fixtures) {
      expect(Object.keys(fixture.payload)).toEqual([
        'eventId',
        'jobId',
        'jobNumber',
        'status',
        'previousStatus',
        'occurredAt',
      ])
    }
  })
})

describe('isJobEventPayload', () => {
  it.each(fixtures.map((fixture) => [fixture.name, fixture.payload] as const))(
    'accepts the %s fixture',
    (_name, payload) => {
      expect(isJobEventPayload(payload)).toBe(true)
    },
  )

  it('accepts a payload with a member added later', () => {
    expect(isJobEventPayload(mutated({ correlationHint: 'ignored' }))).toBe(true)
  })

  it.each(['eventId', 'jobId', 'jobNumber', 'status', 'previousStatus', 'occurredAt'])(
    'drops a copy without %s',
    (member) => {
      expect(isJobEventPayload(without(member))).toBe(false)
    },
  )

  it.each([
    ['a non-uuid event id', { eventId: 'not-a-uuid' }],
    ['an event id in braces', { eventId: '{01a1549c-bac0-7272-8af7-8acf82fc5f4d}' }],
    ['an event id with the wrong variant', { eventId: '01a1549c-bac0-7272-0af7-8acf82fc5f4d' }],
    ['a numeric event id', { eventId: 1049 }],
    ['a non-uuid job id', { jobId: 'JOB-1049' }],
    ['a string job number', { jobNumber: '1049' }],
    ['a fractional job number', { jobNumber: 1049.5 }],
    ['a zero job number', { jobNumber: 0 }],
    ['a negative job number', { jobNumber: -1 }],
    ['an unsafe job number', { jobNumber: Number.MAX_SAFE_INTEGER + 1 }],
    ['an unknown status', { status: 'Paused' }],
    ['a status label instead of the value', { status: 'In progress' }],
    ['a lower-case status', { status: 'inprogress' }],
    ['an unknown previous status', { previousStatus: 'Paused' }],
    ['an undefined previous status', { previousStatus: undefined }],
    ['an instant without Z', { occurredAt: '2026-10-19T14:42:00.000' }],
    ['an instant with an offset', { occurredAt: '2026-10-19T14:42:00.000+00:00' }],
    ['an instant without milliseconds', { occurredAt: '2026-10-19T14:42:00Z' }],
    ['an impossible date', { occurredAt: '2026-02-30T14:42:00.000Z' }],
    ['a numeric instant', { occurredAt: 1792420920000 }],
  ])('drops a copy with %s', (_label, changes) => {
    expect(isJobEventPayload(mutated(changes))).toBe(false)
  })

  it.each([
    ['null', null],
    ['a string', JSON.stringify(statusChanged)],
    ['an array', [statusChanged]],
    ['a number', 1049],
    ['undefined', undefined],
  ])('drops %s', (_label, value) => {
    expect(isJobEventPayload(value)).toBe(false)
  })
})

describe('isJobEventName', () => {
  it('knows the three server events and nothing else', () => {
    expect(jobEventNames).toEqual(['job.created', 'job.assigned', 'job.status-changed'])
    expect(jobEventNames.every(isJobEventName)).toBe(true)
    expect(isJobEventName('job.deleted')).toBe(false)
    expect(isJobEventName('connect')).toBe(false)
  })
})
