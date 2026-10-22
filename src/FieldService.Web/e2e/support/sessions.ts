import { currentRunId, readJsonFile, resultsFile, writeJsonFile } from './run.ts'

export type SessionEvent = 'opened' | 'signed-out'

interface SessionLedger {
  readonly opened: readonly string[]
  readonly signedOut: readonly string[]
}

export interface SessionSummary {
  readonly opened: number
  readonly signedOut: number
  readonly openedBy: readonly string[]
  readonly signedOutBy: readonly string[]
}

const emptyLedger: SessionLedger = { opened: [], signedOut: [] }

function ledgerFile(runId: string): string {
  return resultsFile('sessions', `${runId}.json`)
}

export function recordSessionEvent(event: SessionEvent, kind: string): void {
  const file = ledgerFile(currentRunId())
  const ledger = readJsonFile<SessionLedger>(file, emptyLedger)
  writeJsonFile(
    file,
    event === 'opened'
      ? { ...ledger, opened: [...ledger.opened, kind] }
      : { ...ledger, signedOut: [...ledger.signedOut, kind] },
  )
}

export function sessionSummary(runId: string): SessionSummary {
  const ledger = readJsonFile<SessionLedger>(ledgerFile(runId), emptyLedger)
  return {
    opened: ledger.opened.length,
    signedOut: ledger.signedOut.length,
    openedBy: ledger.opened,
    signedOutBy: ledger.signedOut,
  }
}
