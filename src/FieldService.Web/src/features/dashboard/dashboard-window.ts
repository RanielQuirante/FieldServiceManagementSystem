export interface DashboardWindow {
  readonly from: string
  readonly to: string
}

export const maximumWindowMilliseconds = 31 * 24 * 60 * 60 * 1_000

export function startOfLocalDay(now: Date): Date {
  return new Date(now.getFullYear(), now.getMonth(), now.getDate())
}

export function nextLocalMidnight(now: Date): Date {
  return new Date(now.getFullYear(), now.getMonth(), now.getDate() + 1)
}

export function dashboardWindow(now: Date): DashboardWindow {
  return {
    from: startOfLocalDay(now).toISOString(),
    to: nextLocalMidnight(now).toISOString(),
  }
}
