import { setTimeout as delay } from 'node:timers/promises'

import { currentRunId, readJsonFile, resultsFile, writeJsonFile } from './run.ts'

export const signInBudget = 7

export const refreshBudget = 25

export const socketTicketBudget = 25

export const administratorRequestBudget = 250

export const refreshWindowMilliseconds = 60_000

export type AuthCall = 'sign-in' | 'refresh' | 'socket-ticket'

export interface BudgetReserve {
  readonly refreshes: number
  readonly tickets: number
  readonly administratorRequests: number
}

export const testReserve: BudgetReserve = { refreshes: 3, tickets: 3, administratorRequests: 60 }

interface BudgetState {
  readonly signIns: number
  readonly signInTimes?: readonly number[]
  readonly refreshes: readonly number[]
  readonly tickets?: readonly number[]
  readonly administratorRequests?: readonly number[]
  readonly pauses?: readonly number[]
  readonly violations: readonly string[]
}

export interface BudgetSummary {
  readonly signIns: number
  readonly busiestSignInMinute: number
  readonly refreshes: number
  readonly busiestRefreshMinute: number
  readonly tickets: number
  readonly busiestTicketMinute: number
  readonly administratorRequests: number
  readonly busiestAdministratorMinute: number
  readonly pauses: number
  readonly pauseMilliseconds: number
  readonly violations: readonly string[]
}

const emptyState: BudgetState = {
  signIns: 0,
  signInTimes: [],
  refreshes: [],
  tickets: [],
  administratorRequests: [],
  pauses: [],
  violations: [],
}

const anonymousAuthPaths: ReadonlySet<string> = new Set([
  '/api/auth/sign-in',
  '/api/auth/refresh',
  '/api/auth/sign-out',
])

function budgetFile(runId: string): string {
  return resultsFile('budget', `${runId}.json`)
}

function readState(runId: string): BudgetState {
  return readJsonFile<BudgetState>(budgetFile(runId), emptyState)
}

function callsWithin(calls: readonly number[], end: number): number {
  return calls.filter((at) => at > end - refreshWindowMilliseconds && at <= end).length
}

function busiestMinute(calls: readonly number[]): number {
  return calls.reduce((most, at) => Math.max(most, callsWithin(calls, at)), 0)
}

export function countsAgainstTheUserLimit(url: string): boolean {
  const path = new URL(url).pathname
  return path.startsWith('/api/') && !anonymousAuthPaths.has(path)
}

export function recordAdministratorRequest(at: number = Date.now()): void {
  const runId = currentRunId()
  const state = readState(runId)
  const administratorRequests = [...(state.administratorRequests ?? []), at]
  const inWindow = callsWithin(administratorRequests, at)
  const violations =
    inWindow === administratorRequestBudget + 1
      ? [
          ...state.violations,
          `Administrator request budget exceeded: ${inWindow} authenticated API requests in 60 seconds, the budget is ${administratorRequestBudget} (the API allows 300 a minute per user).`,
        ]
      : state.violations
  writeJsonFile(budgetFile(runId), { ...state, administratorRequests, violations })
}

export function recordAuthCall(call: AuthCall, at: number = Date.now()): string | null {
  const runId = currentRunId()
  const state = readState(runId)
  let violation: string | null = null
  let next: BudgetState
  if (call === 'sign-in') {
    const signIns = state.signIns + 1
    if (signIns > signInBudget) {
      violation = `Sign-in budget exceeded: sign-in ${signIns} of this run, the budget is ${signInBudget} per run.`
    }
    next =
      violation === null
        ? { ...state, signIns, signInTimes: [...(state.signInTimes ?? []), at] }
        : { ...state, signIns }
  } else if (call === 'refresh') {
    const refreshes = [...state.refreshes, at]
    const inWindow = callsWithin(refreshes, at)
    if (inWindow > refreshBudget) {
      violation = `Refresh budget exceeded: ${inWindow} refresh calls in 60 seconds, the budget is ${refreshBudget}.`
    }
    next = { ...state, refreshes }
  } else {
    const tickets = [...(state.tickets ?? []), at]
    const inWindow = callsWithin(tickets, at)
    if (inWindow > socketTicketBudget) {
      violation = `Socket ticket budget exceeded: ${inWindow} ticket requests in 60 seconds, the budget is ${socketTicketBudget}.`
    }
    next = { ...state, tickets }
  }
  if (violation !== null) {
    next = { ...next, violations: [...next.violations, violation] }
  }
  writeJsonFile(budgetFile(runId), next)
  return violation
}

function pauseUntilRoom(
  calls: readonly number[],
  needed: number,
  limit: number,
  now: number,
): number {
  const recent = calls
    .filter((at) => at > now - refreshWindowMilliseconds)
    .sort((first, second) => first - second)
  const excess = recent.length + needed - limit
  if (excess <= 0) {
    return 0
  }
  const freedAt = (recent[excess - 1] ?? now) + refreshWindowMilliseconds
  return Math.max(freedAt - now + 50, 50)
}

export function budgetPauseFor(
  reserve: BudgetReserve,
  now: number = Date.now(),
  runId: string = currentRunId(),
): number {
  const state = readState(runId)
  return Math.max(
    pauseUntilRoom(state.refreshes, reserve.refreshes, refreshBudget, now),
    pauseUntilRoom(state.tickets ?? [], reserve.tickets, socketTicketBudget, now),
    pauseUntilRoom(
      state.administratorRequests ?? [],
      reserve.administratorRequests,
      administratorRequestBudget,
      now,
    ),
  )
}

export async function waitForBudgetRoom(
  reserve: BudgetReserve,
  runId: string = currentRunId(),
): Promise<number> {
  let waited = 0
  let pause = budgetPauseFor(reserve, Date.now(), runId)
  while (pause > 0) {
    await delay(pause)
    waited += pause
    pause = budgetPauseFor(reserve, Date.now(), runId)
  }
  if (waited > 0) {
    const state = readState(runId)
    writeJsonFile(budgetFile(runId), { ...state, pauses: [...(state.pauses ?? []), waited] })
  }
  return waited
}

export function budgetViolations(runId: string = currentRunId()): readonly string[] {
  return readState(runId).violations
}

export function budgetSummary(runId: string): BudgetSummary {
  const state = readState(runId)
  const tickets = state.tickets ?? []
  const requests = state.administratorRequests ?? []
  const pauses = state.pauses ?? []
  return {
    signIns: state.signIns,
    busiestSignInMinute: busiestMinute(state.signInTimes ?? []),
    refreshes: state.refreshes.length,
    busiestRefreshMinute: busiestMinute(state.refreshes),
    tickets: tickets.length,
    busiestTicketMinute: busiestMinute(tickets),
    administratorRequests: requests.length,
    busiestAdministratorMinute: busiestMinute(requests),
    pauses: pauses.length,
    pauseMilliseconds: pauses.reduce((total, pause) => total + pause, 0),
    violations: state.violations,
  }
}
