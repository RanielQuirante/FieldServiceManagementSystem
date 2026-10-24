import { defineStore } from 'pinia'
import { computed, shallowRef } from 'vue'

import { isApiError, type ApiError } from '@/shared/api/api-error'

import {
  onShiftSummary,
  shiftCounts,
  type OnShiftSummary,
  type ShiftCounts,
} from './technician-filters'
import * as techniciansApi from './technicians-api'
import type { Technician } from './technicians-api'

export type TechniciansStatus = 'idle' | 'loading' | 'ready' | 'error'

export const useTechniciansStore = defineStore('technicians', () => {
  const items = shallowRef<readonly Technician[] | null>(null)
  const status = shallowRef<TechniciansStatus>('idle')
  const error = shallowRef<ApiError | null>(null)
  let request = 0
  let running: Promise<void> | null = null

  const counts = computed<ShiftCounts | null>(() =>
    items.value === null ? null : shiftCounts(items.value),
  )

  const onShift = computed<OnShiftSummary | null>(() =>
    items.value === null ? null : onShiftSummary(items.value),
  )

  async function performLoad(): Promise<void> {
    request += 1
    const current = request
    status.value = 'loading'
    error.value = null
    try {
      const result = await techniciansApi.getTechnicians()
      if (current !== request) {
        return
      }
      items.value = result
      status.value = 'ready'
    } catch (failure) {
      if (current !== request) {
        return
      }
      status.value = 'error'
      if (!isApiError(failure)) {
        throw failure
      }
      error.value = failure
    }
  }

  async function load(): Promise<void> {
    const started = performLoad()
    running = started
    try {
      await started
    } finally {
      if (running === started) {
        running = null
      }
    }
  }

  async function refresh(): Promise<boolean> {
    if (running !== null) {
      await running.catch(() => undefined)
    }
    if (items.value === null || status.value !== 'ready') {
      return false
    }
    request += 1
    const current = request
    try {
      const result = await techniciansApi.getTechnicians()
      if (current !== request) {
        return false
      }
      items.value = result
      return true
    } catch (failure) {
      if (current !== request && isApiError(failure)) {
        return false
      }
      throw failure
    }
  }

  function close(): void {
    request += 1
    running = null
    items.value = null
    status.value = 'idle'
    error.value = null
  }

  return { items, status, error, counts, onShift, load, refresh, close }
})
