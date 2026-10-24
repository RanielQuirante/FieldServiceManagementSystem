import { describe, expect, it } from 'vitest'

import { listSourceFiles, readSource } from '@/app/styles/__tests__/stylesheet-source'

import {
  dayOffset,
  formatDuration,
  formatLongDate,
  formatMoment,
  formatMomentUnbroken,
  formatRelativeDay,
  formatScheduleDay,
  formatScheduled,
  formatScheduledUnbroken,
  formatShortDate,
  formatTime,
  formatTimeWindow,
  formatUpdated,
  formatWeekday,
  isSameLocalDay,
  localDateTimeToInstant,
  noBreakSpace,
  separator,
  unbroken,
} from '../date-time'
import { formatJobNumber } from '../job-number'
import {
  currentOrNextJob,
  emailParts,
  openJobsLabel,
  startsWhen,
  telephoneHref,
  type TechnicianJob,
} from '../technician-job'
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

const keptSpace = '\u{00a0}'

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
    [2_970 * 60_000, '49 h 30 min'],
  ])(
    'write %i ms as %s, joining every number and unit with a no-break space',
    (milliseconds, read) => {
      const duration = formatDuration(milliseconds)

      expect(duration).toBe(read.split(' ').join(keptSpace))
      expect(duration).not.toContain(' ')
    },
  )
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

describe('text that never breaks inside', () => {
  it('joins the parts of a time or a date with no-break spaces', () => {
    expect(noBreakSpace).toBe('\u{00a0}')
    expect(unbroken('1:50 PM')).toBe(`1:50${keptSpace}PM`)
    expect(unbroken('Tue 20 Oct')).toBe(`Tue${keptSpace}20${keptSpace}Oct`)
    expect(unbroken('9:00–11:00 AM')).toBe(`9:00–11:00${keptSpace}AM`)
  })

  it('keeps a moment whole inside its time today', () => {
    expect(formatMomentUnbroken(at(0, 9, 45), today)).toBe(`9:45${noBreakSpace}AM`)
    expect(formatMomentUnbroken(at(0, 9, 45), today)).not.toContain(' ')
  })

  it('keeps a moment on another day whole inside its date and its time, with an ordinary space after the comma', () => {
    const moment = formatMomentUnbroken(at(1, 9, 45), today)

    expect(moment).toBe(`Tue${noBreakSpace}20${noBreakSpace}Oct, 9:45${noBreakSpace}AM`)
    expect(moment.split(' ')).toEqual([
      `Tue${noBreakSpace}20${noBreakSpace}Oct,`,
      `9:45${noBreakSpace}AM`,
    ])
    expect(formatMomentUnbroken(new Date(2027, 0, 5, 13, 5), today)).toBe(
      `Tue${noBreakSpace}5${noBreakSpace}Jan${noBreakSpace}2027, 1:05${noBreakSpace}PM`,
    )
  })

  it('reads the same as the moment it keeps whole', () => {
    for (const value of [at(0, 9, 45), at(-1, 13, 20), new Date(2027, 0, 5, 13, 5)]) {
      expect(formatMomentUnbroken(value, today).split(noBreakSpace).join(' ')).toBe(
        formatMoment(value, today),
      )
    }
  })

  it('keeps the date and the window of a schedule whole, with an ordinary space after the comma', () => {
    const schedule = formatScheduledUnbroken(at(0, 15, 30), at(0, 17, 0), today)

    expect(schedule).toBe(`Mon${keptSpace}19${keptSpace}Oct, 3:30–5:00${keptSpace}PM`)
    expect(schedule.split(' ')).toEqual([
      `Mon${keptSpace}19${keptSpace}Oct,`,
      `3:30–5:00${keptSpace}PM`,
    ])
  })

  it('keeps each date and time of a schedule across midnight whole, breaking only after a comma or the dash', () => {
    const schedule = formatScheduledUnbroken(at(0, 23, 0), at(1, 1, 0), today)

    expect(schedule).toBe(
      `Mon${keptSpace}19${keptSpace}Oct, 11:00${keptSpace}PM–Tue${keptSpace}20${keptSpace}Oct, 1:00${keptSpace}AM`,
    )
    expect(schedule.split(' ')).toEqual([
      `Mon${keptSpace}19${keptSpace}Oct,`,
      `11:00${keptSpace}PM–Tue${keptSpace}20${keptSpace}Oct,`,
      `1:00${keptSpace}AM`,
    ])
    expect(
      formatScheduledUnbroken(new Date(2027, 0, 5, 9, 0), new Date(2027, 0, 5, 10, 0), today),
    ).toBe(`Tue${keptSpace}5${keptSpace}Jan${keptSpace}2027, 9:00–10:00${keptSpace}AM`)
  })

  it('reads the same as the schedule it keeps whole', () => {
    for (const [start, end] of [
      [at(0, 15, 30), at(0, 17, 0)],
      [at(0, 23, 0), at(1, 1, 0)],
      [at(-1, 11, 0), at(-1, 13, 0)],
    ] as const) {
      expect(formatScheduledUnbroken(start, end, today).split(keptSpace).join(' ')).toBe(
        formatScheduled(start, end, today),
      )
    }
  })

  it('joins a no-break space, the middle dot and an ordinary space into the separator', () => {
    expect(separator).toBe('\u{00a0}\u{00b7} ')
    expect(noBreakSpace).toBe(keptSpace)
  })

  it('names the short weekday of an instant', () => {
    expect(formatWeekday(new Date(2026, 9, 16, 13, 50))).toBe('Fri')
    expect(formatWeekday(today)).toBe('Mon')
  })
})

describe('a technician’s current or next job', () => {
  function reference(start: Date, status: TechnicianJob['status'] = 'Assigned'): TechnicianJob {
    return {
      id: '01a15400-0000-7000-8000-000000001055',
      number: 1055,
      status,
      scheduledStart: start.toISOString(),
      scheduledEnd: new Date(start.getTime() + 90 * 60_000).toISOString(),
    }
  }

  it('prefers the current job, then the next one, then none', () => {
    const current = reference(new Date(2026, 9, 19, 9, 0), 'InProgress')
    const next = reference(new Date(2026, 9, 19, 13, 0))

    expect(currentOrNextJob({ currentJob: current, nextJob: next }, today)).toEqual({
      kind: 'current',
      job: current,
    })
    expect(currentOrNextJob({ currentJob: null, nextJob: next }, today)).toEqual({
      kind: 'next',
      job: next,
      when: `at 1:00${keptSpace}PM`,
    })
    expect(currentOrNextJob({ currentJob: null, nextJob: null }, today)).toEqual({ kind: 'none' })
  })

  it('names a next start on another day by its date and time', () => {
    expect(startsWhen(new Date(2026, 9, 20, 9, 0), today)).toBe(
      `on Tue${keptSpace}20${keptSpace}Oct at 9:00${keptSpace}AM`,
    )
  })

  it('breaks an email only before its @ and keeps an address without one whole', () => {
    expect(emailParts('aisha.karim@fieldservice.example')).toEqual({
      local: 'aisha.karim',
      domain: '@fieldservice.example',
    })
    expect(emailParts('no-at-sign')).toEqual({ local: 'no-at-sign', domain: '' })
  })

  it('dials the digits of a phone number, keeping a leading plus', () => {
    expect(telephoneHref('(555) 014-7810')).toBe('tel:5550147810')
    expect(telephoneHref('+1 (555) 014-7810')).toBe('tel:+15550147810')
    expect(telephoneHref('n/a')).toBeNull()
  })

  it('counts open jobs in the singular for one', () => {
    expect(openJobsLabel(1)).toBe('open job')
    expect(openJobsLabel(0)).toBe('open jobs')
    expect(openJobsLabel(3)).toBe('open jobs')
  })
})

describe('separators and no-break spaces in the source', () => {
  const applicationFiles = [...listSourceFiles('.ts'), ...listSourceFiles('.vue')].filter(
    (file) =>
      !file.includes('__tests__/') &&
      !file.startsWith('shared/testing/') &&
      file !== 'shared/api/schema.gen.ts',
  )

  const middleDotEscape = /\\u00b7|\\u\{0*b7\}|\\xb7|&middot;|&#183;|&#x0*b7;/i

  const noBreakEscape = /\\u00a0|\\u\{0*a0\}|\\xa0|&nbsp;|&#160;|&#x0*a0;/i

  it('takes every middle-dot separator from separator, leaving only the document title its own', () => {
    expect(applicationFiles.length).toBeGreaterThan(0)
    expect(applicationFiles.filter((file) => readSource(file).includes('\u{00b7}'))).toEqual([
      'shared/composables/use-page-title.ts',
    ])
    expect(applicationFiles.filter((file) => middleDotEscape.test(readSource(file)))).toEqual([
      'shared/format/date-time.ts',
    ])
  })

  it('writes no invisible no-break space into the code, taking it from noBreakSpace', () => {
    expect(applicationFiles.filter((file) => readSource(file).includes(keptSpace))).toEqual([])
    expect(applicationFiles.filter((file) => noBreakEscape.test(readSource(file)))).toEqual([
      'shared/format/date-time.ts',
    ])
  })
})
