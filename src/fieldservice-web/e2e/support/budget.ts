import { currentRunId, readJsonFile, resultsFile, writeJsonFile } from './run.ts'

export const signInBudget = 7

export const refreshBudget = 25

export const refreshWindowMilliseconds = 60_000

export type AuthCall = 'sign-in' | 'refresh'

interface BudgetState {
  readonly signIns: number
  readonly refreshes: readonly number[]
  readonly violations: readonly string[]
}

export interface BudgetSummary {
  readonly signIns: number
  readonly refreshes: number
  readonly busiestRefreshMinute: number
  readonly violations: readonly string[]
}

const emptyState: BudgetState = { signIns: 0, refreshes: [], violations: [] }

function budgetFile(runId: string): string {
  return resultsFile('budget', `${runId}.json`)
}

function refreshesWithin(refreshes: readonly number[], end: number): number {
  return refreshes.filter((at) => at > end - refreshWindowMilliseconds && at <= end).length
}

export function recordAuthCall(call: AuthCall, at: number = Date.now()): string | null {
  const runId = currentRunId()
  const state = readJsonFile<BudgetState>(budgetFile(runId), emptyState)
  let violation: string | null = null
  let next: BudgetState
  if (call === 'sign-in') {
    const signIns = state.signIns + 1
    if (signIns > signInBudget) {
      violation = `Sign-in budget exceeded: sign-in ${signIns} of this run, the budget is ${signInBudget} per run.`
    }
    next = { ...state, signIns }
  } else {
    const refreshes = [...state.refreshes, at]
    const inWindow = refreshesWithin(refreshes, at)
    if (inWindow > refreshBudget) {
      violation = `Refresh budget exceeded: ${inWindow} refresh calls in 60 seconds, the budget is ${refreshBudget}.`
    }
    next = { ...state, refreshes }
  }
  if (violation !== null) {
    next = { ...next, violations: [...next.violations, violation] }
  }
  writeJsonFile(budgetFile(runId), next)
  return violation
}

export function budgetViolations(runId: string = currentRunId()): readonly string[] {
  return readJsonFile<BudgetState>(budgetFile(runId), emptyState).violations
}

export function budgetSummary(runId: string): BudgetSummary {
  const state = readJsonFile<BudgetState>(budgetFile(runId), emptyState)
  const busiest = state.refreshes.reduce(
    (most, at) => Math.max(most, refreshesWithin(state.refreshes, at)),
    0,
  )
  return {
    signIns: state.signIns,
    refreshes: state.refreshes.length,
    busiestRefreshMinute: busiest,
    violations: state.violations,
  }
}
