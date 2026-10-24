export type Instant = Date | string

const weekdaysShort = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'] as const

const weekdaysLong = [
  'Sunday',
  'Monday',
  'Tuesday',
  'Wednesday',
  'Thursday',
  'Friday',
  'Saturday',
] as const

const monthsShort = [
  'Jan',
  'Feb',
  'Mar',
  'Apr',
  'May',
  'Jun',
  'Jul',
  'Aug',
  'Sep',
  'Oct',
  'Nov',
  'Dec',
] as const

const monthsLong = [
  'January',
  'February',
  'March',
  'April',
  'May',
  'June',
  'July',
  'August',
  'September',
  'October',
  'November',
  'December',
] as const

const millisecondsPerDay = 86_400_000

interface ClockReading {
  readonly hour: number
  readonly minute: string
  readonly meridiem: 'AM' | 'PM'
}

function toDate(value: Instant): Date {
  return value instanceof Date ? value : new Date(value)
}

function clockOf(date: Date): ClockReading {
  const hours = date.getHours()
  return {
    hour: hours % 12 === 0 ? 12 : hours % 12,
    minute: String(date.getMinutes()).padStart(2, '0'),
    meridiem: hours < 12 ? 'AM' : 'PM',
  }
}

function startOfDay(date: Date): number {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate()).getTime()
}

export function dayOffset(value: Instant, now: Date = new Date()): number {
  return Math.round((startOfDay(toDate(value)) - startOfDay(now)) / millisecondsPerDay)
}

export function isSameLocalDay(first: Instant, second: Instant): boolean {
  return dayOffset(first, toDate(second)) === 0
}

export function formatTime(value: Instant): string {
  const clock = clockOf(toDate(value))
  return `${clock.hour}:${clock.minute} ${clock.meridiem}`
}

export function formatWeekday(value: Instant): string {
  return weekdaysShort[toDate(value).getDay()] ?? ''
}

export function formatShortDate(value: Instant, now: Date = new Date()): string {
  const date = toDate(value)
  const label = `${weekdaysShort[date.getDay()]} ${date.getDate()} ${monthsShort[date.getMonth()]}`
  return date.getFullYear() === now.getFullYear() ? label : `${label} ${date.getFullYear()}`
}

export function formatLongDate(value: Instant): string {
  const date = toDate(value)
  return `${weekdaysLong[date.getDay()]} ${date.getDate()} ${monthsLong[date.getMonth()]} ${date.getFullYear()}`
}

export const noBreakSpace = '\u{00a0}'

export function unbroken(text: string): string {
  return text.replace(/ /g, noBreakSpace)
}

export function formatTimeWindow(start: Instant, end: Instant, now: Date = new Date()): string {
  const from = toDate(start)
  const to = toDate(end)
  if (!isSameLocalDay(from, to)) {
    return `${formatTime(from)}–${formatShortDate(to, now)}, ${formatTime(to)}`
  }
  const opening = clockOf(from)
  const closing = clockOf(to)
  if (opening.meridiem === closing.meridiem) {
    return `${opening.hour}:${opening.minute}–${formatTime(to)}`
  }
  return `${formatTime(from)}–${formatTime(to)}`
}

export function formatRelativeDay(value: Instant, now: Date = new Date()): string {
  switch (dayOffset(value, now)) {
    case 0:
      return 'Today'
    case 1:
      return 'Tomorrow'
    case -1:
      return 'Yesterday'
    default:
      return formatShortDate(value, now)
  }
}

export function formatScheduleDay(value: Instant, now: Date = new Date()): string {
  return dayOffset(value, now) === 0 ? 'Today' : formatShortDate(value, now)
}

export function formatScheduled(start: Instant, end: Instant, now: Date = new Date()): string {
  return `${formatShortDate(start, now)}, ${formatTimeWindow(start, end, now)}`
}

export function formatScheduledUnbroken(
  start: Instant,
  end: Instant,
  now: Date = new Date(),
): string {
  return unbroken(formatScheduled(start, end, now))
    .split(`,${noBreakSpace}`)
    .join(', ')
}

export function formatUpdated(value: Instant, now: Date = new Date()): string {
  const offset = dayOffset(value, now)
  if (offset === 0) {
    return formatTime(value)
  }
  return offset === -1 ? 'Yesterday' : formatShortDate(value, now)
}

export function formatMoment(value: Instant, now: Date = new Date()): string {
  return dayOffset(value, now) === 0
    ? formatTime(value)
    : `${formatShortDate(value, now)}, ${formatTime(value)}`
}

export function formatMomentUnbroken(value: Instant, now: Date = new Date()): string {
  return unbroken(formatMoment(value, now)).replace(`,${noBreakSpace}`, ', ')
}

export function formatDuration(milliseconds: number): string {
  const totalMinutes = Math.max(Math.floor(milliseconds / 60_000), 0)
  const hours = Math.floor(totalMinutes / 60)
  const minutes = totalMinutes % 60
  if (hours === 0) {
    return `${minutes}${noBreakSpace}min`
  }
  const whole = `${hours}${noBreakSpace}h`
  return minutes === 0 ? whole : `${whole}${noBreakSpace}${minutes}${noBreakSpace}min`
}

export const separator = `${noBreakSpace}\u{00b7} `

const datePattern = /^(\d{4})-(\d{2})-(\d{2})$/

const timePattern = /^(\d{2}):(\d{2})(?::(\d{2})(?:\.\d+)?)?$/

export function localDateTimeToInstant(date: string, time: string): string | null {
  const day = datePattern.exec(date)
  const clock = timePattern.exec(time)
  if (day === null || clock === null) {
    return null
  }
  const year = Number(day[1])
  const month = Number(day[2]) - 1
  const dayOfMonth = Number(day[3])
  const hours = Number(clock[1])
  const minutes = Number(clock[2])
  if (hours > 23 || minutes > 59) {
    return null
  }
  const result = new Date(year, month, dayOfMonth, hours, minutes, 0, 0)
  if (
    Number.isNaN(result.getTime()) ||
    result.getFullYear() !== year ||
    result.getMonth() !== month ||
    result.getDate() !== dayOfMonth
  ) {
    return null
  }
  return result.toISOString()
}
