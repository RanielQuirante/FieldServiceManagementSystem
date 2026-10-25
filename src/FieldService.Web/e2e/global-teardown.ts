import { realpathSync } from 'node:fs'
import process from 'node:process'
import { fileURLToPath } from 'node:url'

import type { FullConfig } from '@playwright/test'

import type { CspViolation } from './fixtures.ts'
import { demoPassword } from './support/api.ts'
import { budgetSummary, signInBudget } from './support/budget.ts'
import { describeScan, scanRunLogs, sendLogSeeds } from './support/log-scan.ts'
import { isComposeRun, readRunFiles, readRunStart } from './support/run.ts'
import {
  handOverReportSecrets,
  readRunSentinels,
  recordRunSentinels,
  removeRunSentinelFiles,
  type Sentinel,
} from './support/sentinels.ts'
import { sessionSummary } from './support/sessions.ts'

const apiSignInLimit = 10

const reportScanModule = fileURLToPath(new URL('./support/report-scan.ts', import.meta.url))

const unscannedOutput =
  "The report scan, e2e/support/report-scan.ts, is not among the reporters of this run, because --reporter replaced the reporters of playwright.config.ts. Nothing scans test-results/ or playwright-report/, and a failed test's trace or error context there can hold the demo password or a token: delete both folders right after reading them, and run tests only with the configured reporters."

interface TeardownOutcome {
  readonly lines: readonly string[]
  readonly problems: readonly string[]
}

async function scanRun(runId: string): Promise<TeardownOutcome> {
  const compose = isComposeRun()
  const start = readRunStart(runId)
  const seeds = await sendLogSeeds(start.baseUrl, runId, compose)
  recordRunSentinels(runId, 'log-seeds', seeds.sentinels)
  const sentinels: Sentinel[] = [
    ...readRunSentinels(runId),
    { label: 'demo password', value: demoPassword() },
  ]
  const logs = await scanRunLogs({ sentinels, seeds, compose, startedAt: start.startedAt })
  const problems: string[] = [...seeds.problems, ...logs.problems]
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
  return {
    lines: [
      `e2e teardown for run ${runId} (${compose ? 'compose' : 'host'} stack, ${start.baseUrl}, started ${start.startedAt})`,
      `  sentinels: ${sentinels.length}; log seeds: ${seeds.requests} request(s); controls found after ${logs.waitedMilliseconds} ms`,
      ...logs.scans.map((scan) => `  log scan ${describeScan(scan)}`),
      ...(logs.nginxCorrelationIds === null
        ? []
        : [
            `  nginx correlation ids: ${logs.nginxCorrelationIds} non-empty, each checked against ^[A-Za-z0-9._-]{1,64}$`,
          ]),
      `  CSP violations in the run: ${violations.length}`,
      `  sign-ins: ${budget.signIns} (budget ${signInBudget} a run); busiest minute of the sign-ins inside the budget: ${budget.busiestSignInMinute} (the API allows ${apiSignInLimit} a minute per address)`,
      `  refresh calls: ${budget.refreshes}; busiest minute: ${budget.busiestRefreshMinute}`,
      `  socket tickets: ${budget.tickets}; busiest ticket minute: ${budget.busiestTicketMinute}; budget pauses: ${budget.pauses} (${budget.pauseMilliseconds} ms)`,
      `  administrator API requests: ${budget.administratorRequests}; busiest minute: ${budget.busiestAdministratorMinute}`,
      `  sessions opened: ${sessions.opened}; signed out: ${sessions.signedOut}`,
    ],
    problems,
  }
}

function isReportScan(name: string): boolean {
  try {
    return realpathSync(name) === realpathSync(reportScanModule)
  } catch {
    return false
  }
}

export default async function globalTeardown(config: FullConfig): Promise<void> {
  const runId = process.env.E2E_RUN_ID
  if (runId === undefined || runId.length === 0 || config.projects.length === 0) {
    process.stdout.write('e2e teardown: no run id, nothing to scan\n')
    return
  }
  const reportScanned = config.reporter.some(([name]) => isReportScan(name))
  if (!reportScanned) {
    process.stderr.write(`e2e teardown warning: ${unscannedOutput}\n`)
  }
  let outcome: TeardownOutcome
  let removed: number
  try {
    outcome = await scanRun(runId)
  } finally {
    handOverReportSecrets(runId, readRunSentinels(runId))
    removed = removeRunSentinelFiles(runId)
  }
  process.stdout.write(
    [
      ...outcome.lines,
      `  report scan among the reporters: ${reportScanned ? 'yes' : 'no'}`,
      `  sentinel files removed after the scan: ${removed}`,
      '',
    ].join('\n'),
  )
  const problems = reportScanned ? outcome.problems : [...outcome.problems, unscannedOutput]
  if (problems.length > 0) {
    throw new Error(`The e2e teardown found ${problems.length} problem(s):\n${problems.join('\n')}`)
  }
}
