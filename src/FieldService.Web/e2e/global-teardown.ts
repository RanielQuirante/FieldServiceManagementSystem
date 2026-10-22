import { join } from 'node:path'
import process from 'node:process'

import type { FullConfig } from '@playwright/test'

import type { CspViolation } from './fixtures.ts'
import { demoPassword } from './support/api.ts'
import { budgetSummary } from './support/budget.ts'
import { logsRoot, readRunFiles } from './support/run.ts'
import { readRunSentinels, scanFolder, type Sentinel } from './support/sentinels.ts'
import { sessionSummary } from './support/sessions.ts'

const scannedFolders = ['api', 'notifications'] as const

export default async function globalTeardown(config: FullConfig): Promise<void> {
  const runId = process.env.E2E_RUN_ID
  if (runId === undefined || runId.length === 0 || config.projects.length === 0) {
    process.stdout.write('e2e teardown: no run id, nothing to scan\n')
    return
  }
  const sentinels: Sentinel[] = [
    ...readRunSentinels(runId),
    { label: 'demo password', value: demoPassword() },
  ]
  const logs = logsRoot()
  const problems: string[] = []
  const scanned: string[] = []
  for (const name of scannedFolders) {
    const scan = scanFolder(join(logs, name), sentinels, logs)
    scanned.push(`${scan.folder} ${scan.files} file(s)`)
    if (scan.files === 0) {
      problems.push(`No log file was found under ${scan.folder}, so the secret scan cannot pass.`)
    }
    for (const hit of scan.hits) {
      problems.push(`${hit.file} line ${hit.line} contains a ${hit.label}.`)
    }
  }
  const violations = readRunFiles<CspViolation[]>('csp', runId).flat()
  for (const violation of violations) {
    problems.push(
      `CSP violation of ${violation.directive} on ${violation.page}: blocked ${violation.blocked}.`,
    )
  }
  const budget = budgetSummary(runId)
  problems.push(...budget.violations)
  const sessions = sessionSummary(runId)
  if (sessions.signedOut !== sessions.opened) {
    problems.push(
      `Sessions opened by the run: ${sessions.opened} (${sessions.openedBy.join('; ')}); signed out: ${sessions.signedOut} (${sessions.signedOutBy.join('; ')}). Every session must be signed out.`,
    )
  }
  process.stdout.write(
    [
      `e2e teardown for run ${runId}`,
      `  sentinels: ${sentinels.length}; scanned: ${scanned.join(', ')}`,
      `  CSP violations in the run: ${violations.length}`,
      `  sign-ins: ${budget.signIns}; refresh calls: ${budget.refreshes}; busiest minute: ${budget.busiestRefreshMinute}`,
      `  sessions opened: ${sessions.opened}; signed out: ${sessions.signedOut}`,
      '',
    ].join('\n'),
  )
  if (problems.length > 0) {
    throw new Error(`The e2e teardown found ${problems.length} problem(s):\n${problems.join('\n')}`)
  }
}
