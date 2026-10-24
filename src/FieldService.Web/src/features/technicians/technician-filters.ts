import type { LocationQuery, LocationQueryRaw } from 'vue-router'

import { tradeLabel } from '@/shared/format/labels'

import type { Technician } from './technicians-api'

export type ShiftFilter = 'all' | 'on-shift' | 'off-shift'

export interface TechnicianFilters {
  readonly shift: ShiftFilter
  readonly search: string
}

export interface ShiftCounts {
  readonly all: number
  readonly onShift: number
  readonly offShift: number
}

export interface OnShiftSummary {
  readonly onShift: number
  readonly total: number
}

export const shiftFilters: readonly ShiftFilter[] = ['all', 'on-shift', 'off-shift']

export const noTechnicianFilters: TechnicianFilters = { shift: 'all', search: '' }

export const technicianSearchMaximumLength = 100

export const phoneSearchMinimumDigits = 3

function firstValue(value: LocationQuery[string] | undefined): string | null {
  if (Array.isArray(value)) {
    return value.find((item): item is string => typeof item === 'string') ?? null
  }
  return typeof value === 'string' ? value : null
}

function digitsOf(text: string): string {
  return text.replace(/\D/g, '')
}

export function isOnShift(technician: Technician): boolean {
  return technician.isActive && technician.isOnShift
}

export function matchesShift(technician: Technician, shift: ShiftFilter): boolean {
  if (shift === 'all') {
    return true
  }
  return isOnShift(technician) === (shift === 'on-shift')
}

export function matchesTechnicianSearch(technician: Technician, query: string): boolean {
  const wanted = query.trim().toLocaleLowerCase()
  if (wanted.length === 0) {
    return true
  }
  const texts = [technician.displayName, technician.email, tradeLabel(technician.trade)]
  if (texts.some((text) => text.toLocaleLowerCase().includes(wanted))) {
    return true
  }
  const digits = digitsOf(wanted)
  return (
    digits.length >= phoneSearchMinimumDigits && digitsOf(technician.phoneNumber).includes(digits)
  )
}

export function filterTechnicians(
  technicians: readonly Technician[],
  filters: TechnicianFilters,
): Technician[] {
  return technicians.filter(
    (technician) =>
      matchesShift(technician, filters.shift) &&
      matchesTechnicianSearch(technician, filters.search),
  )
}

export function shiftCounts(technicians: readonly Technician[]): ShiftCounts {
  const onShift = technicians.filter(isOnShift).length
  return { all: technicians.length, onShift, offShift: technicians.length - onShift }
}

export function onShiftSummary(technicians: readonly Technician[]): OnShiftSummary {
  const active = technicians.filter((technician) => technician.isActive)
  return { onShift: active.filter(isOnShift).length, total: active.length }
}

export function technicianFiltersFromQuery(query: LocationQuery): TechnicianFilters {
  const shift = firstValue(query.shift)
  return {
    shift: shift === 'on-shift' || shift === 'off-shift' ? shift : 'all',
    search: (firstValue(query.search) ?? '').slice(0, technicianSearchMaximumLength),
  }
}

export function queryFromTechnicianFilters(filters: TechnicianFilters): LocationQueryRaw {
  const query: LocationQueryRaw = {}
  if (filters.shift !== 'all') {
    query.shift = filters.shift
  }
  if (filters.search.trim().length > 0) {
    query.search = filters.search
  }
  return query
}

export function hasActiveTechnicianFilters(filters: TechnicianFilters): boolean {
  return filters.shift !== 'all' || filters.search.trim().length > 0
}
