import { AxeBuilder } from '@axe-core/playwright'
import { expect, type Page, type TestInfo } from '@playwright/test'

import { readJsonFile, resultsFile, writeJsonFile } from './run.ts'

export const axeTags = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa']

interface AuditRecord {
  readonly test: string
  readonly state: string
  readonly violations: number
  readonly needsReview: readonly string[]
}

function recordAudit(runId: string, record: AuditRecord): void {
  const file = resultsFile('axe', `${runId}-audits.json`)
  writeJsonFile(file, [...readJsonFile<AuditRecord[]>(file, []), record])
}

export async function audit(
  page: Page,
  testInfo: TestInfo,
  runId: string,
  state: string,
): Promise<void> {
  const results = await new AxeBuilder({ page }).withTags(axeTags).analyze()
  const needsReview = results.incomplete.map((item) => ({
    rule: item.id,
    impact: item.impact,
    nodes: item.nodes.map((node) => node.target.join(' ')),
  }))
  await testInfo.attach(`axe needs review: ${state}`, {
    body: JSON.stringify(needsReview, null, 2),
    contentType: 'application/json',
  })
  recordAudit(runId, {
    test: testInfo.title,
    state,
    violations: results.violations.length,
    needsReview: results.incomplete.map((item) => item.id),
  })
  const violations = results.violations.map(
    (violation) =>
      `${violation.id} (${violation.impact ?? 'no impact'}): ${violation.nodes
        .map((node) => node.target.join(' '))
        .join(' | ')}`,
  )
  expect(violations, `axe violations in the state "${state}"`).toEqual([])
}
