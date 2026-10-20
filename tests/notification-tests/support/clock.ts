export interface FakeClock {
  now(): number
  set(epochMilliseconds: number): void
  advance(milliseconds: number): void
}

export const defaultClockStart = Date.UTC(2026, 9, 20, 12, 0, 0)

export function createFakeClock(start = defaultClockStart): FakeClock {
  let current = start
  return {
    now: () => current,
    set: (epochMilliseconds) => {
      current = epochMilliseconds
    },
    advance: (milliseconds) => {
      current += milliseconds
    },
  }
}
