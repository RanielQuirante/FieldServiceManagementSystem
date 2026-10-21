import { describe, expect, it } from 'vitest'

import {
  dayOffset,
  formatDuration,
  formatLongDate,
  formatMoment,
  formatRelativeDay,
  formatScheduleDay,
  formatScheduled,
  formatShortDate,
  formatTime,
  formatTimeWindow,
  formatUpdated,
  isSameLocalDay,
  localDateTimeToInstant,
} from '../date-time'
import { formatJobNumber } from '../job-number'
import {
  historyActionLabel,
  isJobPriority,
  isJobStatus,
  jobPriorityLabel,
  jobStatusLabel,
  roleLabel,
  tradeLabel,
} from '../labels'

const today = new Date(2026, 9, 19, 10, 42)

function at(dayShift: number, hours: number, minutes: number): Date {
  return new Date(2026, 9, 19 + dayShift, hours, minutes)
}

describe('job numbers', () => {
  it('prefix the number with JOB-', () => {
    expect(formatJobNumber(1042)).toBe('JOB-1042')
    expect(formatJobNumber(1056)).toBe('JOB-1056')
  })
})

describe('times', () => {
  it.each([
    [0, 0, '12:00 AM'],
    [7, 5, '7:05 AM'],
    [10, 42, '10:42 AM'],
    [12, 0, '12:00 PM'],
    [13, 30, '1:30 PM'],
    [23, 59, '11:59 PM'],
  ])('show %i:%i in 12-hour form with AM or PM', (hours, minutes, expected) => {
    expect(formatTime(at(0, hours, minutes))).toBe(expected)
  })

  it('read an RFC 3339 instant in the browser time zone', () => {
    expect(formatTime(at(0, 14, 45).toISOString())).toBe('2:45 PM')
  })

  it('write one marker when both ends of a window share it', () => {
    expect(formatTimeWindow(at(0, 9, 0), at(0, 11, 0))).toBe('9:00–11:00 AM')
    expect(formatTimeWindow(at(0, 15, 30), at(0, 17, 0))).toBe('3:30–5:00 PM')
  })

  it('write both markers when a window crosses noon', () => {
    expect(formatTimeWindow(at(0, 10, 0), at(0, 12, 0))).toBe('10:00 AM–12:00 PM')
    expect(formatTimeWindow(at(0, 11, 30), at(0, 13, 0))).toBe('11:30 AM–1:00 PM')
  })

  it('name the end day when a window runs past midnight', () => {
    expect(formatTimeWindow(at(0, 22, 0), at(1, 2, 0), today)).toBe('10:00 PM–Tue 20 Oct, 2:00 AM')
  })
})

describe('dates', () => {
  it('write the short date as weekday, day and month', () => {
    expect(formatShortDate(at(0, 9, 0), today)).toBe('Mon 19 Oct')
    expect(formatShortDate(at(1, 9, 0), today)).toBe('Tue 20 Oct')
  })

  it('add the year to a short date from another year', () => {
    expect(formatShortDate(new Date(2027, 0, 5, 9, 0), today)).toBe('Tue 5 Jan 2027')
  })

  it('write the long date in full', () => {
    expect(formatLongDate(at(0, 9, 0))).toBe('Monday 19 October 2026')
  })

  it('label today, tomorrow and yesterday, and use the short date otherwise', () => {
    expect(formatRelativeDay(at(0, 23, 59), today)).toBe('Today')
    expect(formatRelativeDay(at(1, 0, 0), today)).toBe('Tomorrow')
    expect(formatRelativeDay(at(-1, 8, 0), today)).toBe('Yesterday')
    expect(formatRelativeDay(at(-3, 13, 50), today)).toBe('Fri 16 Oct')
  })

  it('count calendar days, not hours', () => {
    expect(dayOffset(at(1, 0, 1), at(0, 23, 59))).toBe(1)
    expect(dayOffset(at(-1, 23, 59), today)).toBe(-1)
    expect(isSameLocalDay(at(0, 0, 0), at(0, 23, 59))).toBe(true)
  })

  it('show the scheduled day of a table row as Today or the short date', () => {
    expect(formatScheduleDay(at(0, 10, 0), today)).toBe('Today')
    expect(formatScheduleDay(at(1, 8, 0), today)).toBe('Tue 20 Oct')
  })

  it('show a scheduled window with its date', () => {
    expect(formatScheduled(at(0, 15, 30), at(0, 17, 0), today)).toBe('Mon 19 Oct, 3:30–5:00 PM')
  })

  it('show the last update as a time today, Yesterday, or the short date', () => {
    expect(formatUpdated(at(0, 10, 31), today)).toBe('10:31 AM')
    expect(formatUpdated(at(-1, 16, 40), today)).toBe('Yesterday')
    expect(formatUpdated(at(-2, 9, 35), today)).toBe('Sat 17 Oct')
  })

  it('show a moment as the time today and with the short date otherwise', () => {
    expect(formatMoment(at(0, 10, 20), today)).toBe('10:20 AM')
    expect(formatMoment(at(-1, 13, 20), today)).toBe('Sun 18 Oct, 1:20 PM')
  })
})

describe('durations', () => {
  it.each([
    [106 * 60_000, '1 h 46 min'],
    [107 * 60_000, '1 h 47 min'],
    [168 * 60_000, '2 h 48 min'],
    [120 * 60_000, '2 h'],
    [45 * 60_000, '45 min'],
    [-5_000, '0 min'],
  ])('write %i ms as %s', (milliseconds, expected) => {
    expect(formatDuration(milliseconds)).toBe(expected)
  })
})

describe('local date and time to an instant', () => {
  it('reads the date and time in the browser time zone and returns UTC', () => {
    const instant = localDateTimeToInstant('2026-10-20', '09:00')

    expect(instant).toBe(new Date(2026, 9, 20, 9, 0).toISOString())
    expect(instant).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:00\.000Z$/)
  })

  it('accepts a time with seconds', () => {
    expect(localDateTimeToInstant('2026-10-20', '11:00:00')).toBe(
      new Date(2026, 9, 20, 11, 0).toISOString(),
    )
  })

  it.each([
    ['', '09:00'],
    ['2026-10-20', ''],
    ['2026-02-30', '09:00'],
    ['20/10/2026', '09:00'],
    ['2026-10-20', '25:00'],
    ['2026-10-20', '9 AM'],
  ])('refuses %j at %j', (date, time) => {
    expect(localDateTimeToInstant(date, time)).toBeNull()
  })
})

describe('labels', () => {
  it.each([
    ['Unassigned', 'Unassigned'],
    ['Assigned', 'Assigned'],
    ['InProgress', 'In progress'],
    ['OnHold', 'On hold'],
    ['Completed', 'Completed'],
    ['Cancelled', 'Cancelled'],
  ])('name the status %s as %s', (status, label) => {
    expect(jobStatusLabel(status)).toBe(label)
  })

  it.each(['Low', 'Normal', 'High', 'Urgent'])('name the priority %s as itself', (priority) => {
    expect(jobPriorityLabel(priority)).toBe(priority)
  })

  it('name the trades and roles as the interface shows them', () => {
    expect(tradeLabel('Hvac')).toBe('HVAC')
    expect(tradeLabel('FireSafety')).toBe('Fire safety')
    expect(roleLabel('Administrator')).toBe('Dispatcher')
    expect(roleLabel('Technician')).toBe('Technician')
    expect(historyActionLabel('PutOnHold')).toBe('Put on hold')
  })

  it('show a value the client does not know as it arrived', () => {
    expect(jobStatusLabel('Escalated')).toBe('Escalated')
    expect(jobPriorityLabel('Critical')).toBe('Critical')
    expect(jobStatusLabel('toString')).toBe('toString')
  })

  it('recognise only the contract values', () => {
    expect(isJobStatus('OnHold')).toBe(true)
    expect(isJobStatus('onhold')).toBe(false)
    expect(isJobStatus(1)).toBe(false)
    expect(isJobPriority('Urgent')).toBe(true)
    expect(isJobPriority('urgent')).toBe(false)
  })
})
