import { onScopeDispose, shallowRef, type ShallowRef } from 'vue'

import { nextGreetingChange } from '../dashboard-copy'

export function useGreetingClock(): ShallowRef<Date> {
  const now = shallowRef(new Date())
  let timer: ReturnType<typeof setTimeout> | undefined

  function schedule(): void {
    const current = new Date()
    timer = setTimeout(tick, nextGreetingChange(current).getTime() - current.getTime())
  }

  function tick(): void {
    now.value = new Date()
    schedule()
  }

  schedule()

  onScopeDispose(() => {
    if (timer !== undefined) {
      clearTimeout(timer)
      timer = undefined
    }
  })

  return now
}
