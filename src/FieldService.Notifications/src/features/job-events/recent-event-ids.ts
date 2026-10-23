import type { Clock } from '../../shared/clock.ts'
import { createTtlSet } from '../../shared/ttl-set.ts'

export const recentEventIdCapacity = 10_000
export const maximumToleranceSeconds = 300
export const retentionMarginSeconds = 60

export interface RecentEventIds {
  readonly retentionMs: number
  add(id: string): boolean
}

export function retentionSecondsFor(toleranceSeconds: number): number {
  if (
    !Number.isInteger(toleranceSeconds) ||
    toleranceSeconds < 1 ||
    toleranceSeconds > maximumToleranceSeconds
  ) {
    throw new RangeError(`toleranceSeconds must be an integer from 1 to ${maximumToleranceSeconds}`)
  }
  return 2 * toleranceSeconds + retentionMarginSeconds
}

export function createRecentEventIds(clock: Clock, toleranceSeconds: number): RecentEventIds {
  const retentionMs = retentionSecondsFor(toleranceSeconds) * 1000
  const ids = createTtlSet<null>({ clock, ttlMs: retentionMs, capacity: recentEventIdCapacity })

  return {
    retentionMs,
    add: (id) => {
      if (ids.has(id)) {
        return false
      }
      if (ids.isFull()) {
        ids.evictFirst(() => true)
      }
      ids.add(id, null)
      return true
    },
  }
}
