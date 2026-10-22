import { dayOffset, formatShortDate, formatTime, type Instant } from '@/shared/format/date-time'
import { formatJobNumber } from '@/shared/format/job-number'
import { tradeLabel } from '@/shared/format/labels'

import type { Technician } from './jobs-api'

export interface TradeGroups {
  readonly sameTrade: readonly Technician[]
  readonly otherTrades: readonly Technician[]
}

export type DetailPartKind = 'text' | 'job-number' | 'moment'

export interface DetailPart {
  readonly text: string
  readonly kind: DetailPartKind
}

function rank(technician: Technician, trade: string | null): readonly number[] {
  return [
    trade === null || technician.trade === trade ? 0 : 1,
    technician.availableForAssignment ? 0 : 1,
    technician.openJobCount,
  ]
}

export function assignOrder(
  technicians: readonly Technician[],
  trade: string | null,
): Technician[] {
  return technicians
    .map((technician, index) => ({ technician, index, keys: rank(technician, trade) }))
    .sort((first, second) => {
      for (let key = 0; key < first.keys.length; key += 1) {
        const difference = (first.keys[key] ?? 0) - (second.keys[key] ?? 0)
        if (difference !== 0) {
          return difference
        }
      }
      return first.index - second.index
    })
    .map(({ technician }) => technician)
}

export function groupByTrade(ordered: readonly Technician[], trade: string): TradeGroups {
  return {
    sameTrade: ordered.filter((technician) => technician.trade === trade),
    otherTrades: ordered.filter((technician) => technician.trade !== trade),
  }
}

export function openJobsLabel(count: number): string {
  return count === 1 ? '1 open job' : `${count} open jobs`
}

function unavailableReason(technician: Technician): string {
  return technician.isActive ? 'Off shift' : 'Inactive'
}

function plain(text: string): DetailPart {
  return { text, kind: 'text' }
}

function jobNumberPart(number: number): DetailPart {
  return { text: formatJobNumber(number), kind: 'job-number' }
}

function momentParts(
  value: Instant,
  now: Date,
  todayLead: string,
  otherDayLead: string,
): DetailPart[] {
  const time: DetailPart = { text: formatTime(value), kind: 'moment' }
  if (dayOffset(value, now) === 0) {
    return [plain(todayLead), time]
  }
  return [
    plain(otherDayLead),
    { text: formatShortDate(value, now), kind: 'moment' },
    plain(' at '),
    time,
  ]
}

function hasPassed(value: Instant, now: Date): boolean {
  return new Date(value).getTime() < now.getTime()
}

function scheduleClause(technician: Technician, now: Date): DetailPart[] {
  const current = technician.currentJob
  if (current !== null && current.status === 'InProgress') {
    if (hasPassed(current.scheduledEnd, now)) {
      return [
        plain(' · still on '),
        jobNumberPart(current.number),
        ...momentParts(current.scheduledEnd, now, ', due ', ', due '),
      ]
    }
    return [
      plain(' · finishes '),
      jobNumberPart(current.number),
      ...momentParts(current.scheduledEnd, now, ' by ', ' on '),
    ]
  }
  const next = technician.nextJob
  if (next !== null) {
    return [
      plain(' · next '),
      jobNumberPart(next.number),
      ...momentParts(next.scheduledStart, now, ' at ', ' on '),
    ]
  }
  return []
}

export function assignRowDetail(technician: Technician, trade: string, now: Date): DetailPart[] {
  const tradeText = tradeLabel(technician.trade)
  if (!technician.availableForAssignment) {
    return [plain(`${tradeText} · ${unavailableReason(technician)}`)]
  }
  const summary = plain(`${tradeText} · ${openJobsLabel(technician.openJobCount)}`)
  return technician.trade === trade ? [summary, ...scheduleClause(technician, now)] : [summary]
}

export function technicianOptionLabel(technician: Technician): string {
  const tradeText = tradeLabel(technician.trade)
  if (!technician.availableForAssignment) {
    return `${technician.displayName} — ${tradeText}, ${unavailableReason(technician).toLowerCase()}`
  }
  return `${technician.displayName} — ${tradeText}, ${openJobsLabel(technician.openJobCount)}`
}

export function matchesSearch(technician: Technician, query: string): boolean {
  const wanted = query.trim().toLocaleLowerCase()
  return wanted.length === 0 || technician.displayName.toLocaleLowerCase().includes(wanted)
}
