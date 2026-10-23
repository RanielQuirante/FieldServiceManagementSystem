import { shallowRef, type ShallowRef } from 'vue'

import { isApiError, type ApiError } from '@/shared/api/api-error'

export type AsyncStatus = 'idle' | 'loading' | 'content' | 'empty' | 'error'

export interface AsyncState<Data> {
  readonly status: ShallowRef<AsyncStatus>
  readonly data: ShallowRef<Data | null>
  readonly error: ShallowRef<ApiError | null>
  run(): Promise<void>
  refresh(): Promise<boolean>
}

export function useAsyncState<Data>(
  load: () => Promise<Data>,
  isEmpty: (data: Data) => boolean = () => false,
): AsyncState<Data> {
  const status = shallowRef<AsyncStatus>('idle')
  const data = shallowRef<Data | null>(null)
  const error = shallowRef<ApiError | null>(null)
  let latest = 0

  async function run(): Promise<void> {
    latest += 1
    const request = latest
    status.value = 'loading'
    error.value = null
    try {
      const result = await load()
      if (request !== latest) {
        return
      }
      data.value = result
      status.value = isEmpty(result) ? 'empty' : 'content'
    } catch (failure) {
      if (request !== latest) {
        return
      }
      status.value = 'error'
      if (!isApiError(failure)) {
        throw failure
      }
      error.value = failure
    }
  }

  async function refresh(): Promise<boolean> {
    if (status.value !== 'content' && status.value !== 'empty') {
      return false
    }
    latest += 1
    const request = latest
    try {
      const result = await load()
      if (request !== latest) {
        return false
      }
      data.value = result
      status.value = isEmpty(result) ? 'empty' : 'content'
      return true
    } catch (failure) {
      if (!isApiError(failure)) {
        throw failure
      }
      return false
    }
  }

  return { status, data, error, run, refresh }
}
