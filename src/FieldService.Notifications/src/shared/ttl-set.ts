import type { Clock } from './clock.ts'

export interface TtlSetOptions {
  readonly clock: Clock
  readonly ttlMs: number
  readonly capacity: number
}

export interface TtlSetEntry<Value> {
  readonly addedAt: number
  readonly expiresAt: number
  readonly value: Value
}

export interface TtlSet<Value> {
  readonly capacity: number
  size(): number
  has(key: string): boolean
  isFull(): boolean
  add(key: string, value: Value): void
  evictFirst(isEvictable: (entry: TtlSetEntry<Value>, now: number) => boolean): boolean
}

export function createTtlSet<Value>(options: TtlSetOptions): TtlSet<Value> {
  const { clock, ttlMs, capacity } = options
  if (!Number.isInteger(ttlMs) || ttlMs <= 0) {
    throw new RangeError('ttlMs must be a positive integer')
  }
  if (!Number.isInteger(capacity) || capacity <= 0) {
    throw new RangeError('capacity must be a positive integer')
  }

  const entries = new Map<string, TtlSetEntry<Value>>()

  function removeExpired(now: number): void {
    for (const [key, entry] of entries) {
      if (entry.expiresAt > now) {
        return
      }
      entries.delete(key)
    }
  }

  return {
    capacity,
    size: () => {
      removeExpired(clock.now())
      return entries.size
    },
    has: (key) => {
      const entry = entries.get(key)
      return entry !== undefined && entry.expiresAt > clock.now()
    },
    isFull: () => {
      removeExpired(clock.now())
      return entries.size >= capacity
    },
    add: (key, value) => {
      const now = clock.now()
      entries.delete(key)
      entries.set(key, { addedAt: now, expiresAt: now + ttlMs, value })
    },
    evictFirst: (isEvictable) => {
      const now = clock.now()
      for (const [key, entry] of entries) {
        if (isEvictable(entry, now)) {
          entries.delete(key)
          return true
        }
      }
      return false
    },
  }
}
