import { randomUUID } from 'node:crypto'
import { existsSync } from 'node:fs'

import type { Locator, Page } from '@playwright/test'

import {
  anyOf,
  consolePatterns,
  expect,
  fulfilStub,
  problemBody,
  stubSignedOutRestore,
  test,
} from './fixtures.ts'
import { demoAccounts, demoTechnicians } from './support/api.ts'
import {
  cancelCopy,
  createJobCopy,
  dashboardCopy,
  jobDetailCopy,
  jobsCopy,
  notFoundCopy,
  reportCopy,
  shellCopy,
  signInCopy,
  techniciansCopy,
  toastCopy,
} from './support/copy.ts'
import { createUnassignedJob } from './support/jobs.ts'
import { openInApp, resultsFile } from './support/run.ts'

const designWidths = [
  { width: 375, height: 812 },
  { width: 768, height: 1024 },
  { width: 1280, height: 800 },
  { width: 1440, height: 900 },
] as const

const submittedReportJob = 1039

async function captureAtEveryWidth(page: Page, state: string, fullPage = true): Promise<string[]> {
  const paths: string[] = []
  for (const viewport of designWidths) {
    await page.setViewportSize(viewport)
    const path = resultsFile('design-qa', `${state}-${viewport.width}.png`)
    await page.screenshot({ path, fullPage, animations: 'disabled' })
    paths.push(path)
  }
  return paths
}

const overlayInView = false

function errorToast(page: Page): Locator {
  return page
    .getByRole('region', { name: shellCopy.notifications })
    .getByRole('alert')
    .filter({ hasText: toastCopy.errorTitle })
}

test.describe('E2E-20 design QA screenshots', () => {
  test('the sign-in error is captured at 375, 768, 1280 and 1440 px', async ({
    anonymousPage: page,
    guards,
  }) => {
    guards.allowConsole(anyOf(consolePatterns.anonymousRefresh, consolePatterns.signInUnauthorized))
    await stubSignedOutRestore(page)
    await page.goto('/sign-in')
    await expect(page.getByRole('heading', { level: 1, name: signInCopy.heading })).toBeVisible()
    await fulfilStub(
      page,
      '**/api/auth/sign-in',
      {
        status: 401,
        body: problemBody(401, 'auth.invalid-credentials', 'The credentials are not valid.'),
      },
      1,
    )
    await page.getByLabel(signInCopy.email, { exact: true }).fill(demoAccounts.administrator)
    await page.getByLabel(signInCopy.password, { exact: true }).fill(randomUUID())
    await page.getByRole('button', { name: signInCopy.submit, exact: true }).click()
    await expect(page.getByRole('alert')).toContainText(signInCopy.invalidCredentials)

    const captured = await captureAtEveryWidth(page, 'sign-in-error')

    expect(captured.filter((path) => existsSync(path))).toHaveLength(designWidths.length)
  })

  test('the dashboard, jobs with a toast, job detail with the cancel dialog, new job with field errors, the inspection report, technicians and both not-found pages are captured at 375, 768, 1280 and 1440 px', async ({
    adminPage: page,
    adminSession,
    api,
    guards,
    runId,
  }) => {
    const marker = `E2E ${runId} design QA toast`
    guards.allowPageErrors(consolePatterns.reportedError(marker))
    guards.allowConsole(anyOf(consolePatterns.reportedError(marker), consolePatterns.jobNotFound))
    const job = await createUnassignedJob(api, runId, 'design QA cancel dialog')
    const report = await api.getJob((await api.findJobByNumber(submittedReportJob)).id)
    const captured: string[] = []

    await page.goto('/')
    await expect(
      page.getByRole('heading', {
        level: 1,
        name: dashboardCopy.greeting(adminSession.user.displayName),
      }),
    ).toBeVisible()
    await expect(
      page
        .getByRole('region', { name: dashboardCopy.jobsByStatus })
        .getByRole('link', { name: /, \d+(?:, .+)?$/ }),
    ).toHaveCount(6)
    await expect
      .poll(() =>
        page.getByRole('table', { name: dashboardCopy.workload }).getByRole('row').count(),
      )
      .toBeGreaterThan(1)
    captured.push(...(await captureAtEveryWidth(page, 'dashboard')))

    await openInApp(page, '/jobs')
    await expect(page.getByRole('heading', { level: 1, name: jobsCopy.heading })).toBeVisible()
    await expect(
      page.getByRole('region', { name: jobsCopy.table }).locator('tbody tr').first(),
    ).toBeVisible()
    await page.evaluate((text) => {
      void Promise.reject(new Error(text))
    }, marker)
    await expect(errorToast(page)).toBeVisible()
    captured.push(...(await captureAtEveryWidth(page, 'jobs-with-a-toast', overlayInView)))
    await errorToast(page).getByRole('button', { name: toastCopy.dismiss }).click()
    await expect(errorToast(page)).toHaveCount(0)

    await openInApp(page, `/jobs/${job.id}`)
    await expect(page.getByRole('heading', { level: 1, name: job.title })).toBeVisible()
    await page
      .getByRole('main')
      .getByRole('button', { name: cancelCopy.trigger, exact: true })
      .click()
    const dialog = page.getByRole('dialog', { name: cancelCopy.dialogTitle })
    await expect(dialog).toBeVisible()
    captured.push(...(await captureAtEveryWidth(page, 'job-detail-cancel-dialog', overlayInView)))
    await dialog.getByRole('button', { name: cancelCopy.keep, exact: true }).click()
    await expect(dialog).toBeHidden()

    await page.getByRole('link', { name: shellCopy.newJob }).click()
    await expect(
      page.getByRole('combobox', { name: createJobCopy.checklistTemplate }),
    ).toBeEnabled()
    await page.getByRole('button', { name: createJobCopy.create }).click()
    await expect(
      page.getByRole('textbox', { name: createJobCopy.title, exact: true }),
    ).toHaveAccessibleDescription(createJobCopy.titleRequired)
    captured.push(...(await captureAtEveryWidth(page, 'new-job-field-errors')))

    await openInApp(page, `/jobs/${report.id}/inspection`)
    await expect(page.getByRole('heading', { level: 1, name: report.title })).toBeVisible()
    await expect(page.getByRole('region', { name: reportCopy.results })).toBeVisible()
    captured.push(...(await captureAtEveryWidth(page, 'inspection-report')))

    await openInApp(page, '/technicians')
    await expect(
      page.getByRole('heading', { level: 1, name: techniciansCopy.heading }),
    ).toBeVisible()
    await expect(
      page.getByRole('main').getByText(demoTechnicians.assignments.name).first(),
    ).toBeVisible()
    await expect(page.getByText(techniciansCopy.loading)).toHaveCount(0)
    captured.push(...(await captureAtEveryWidth(page, 'technicians')))

    await openInApp(page, `/jobs/${randomUUID()}`)
    await expect(
      page.getByRole('heading', { level: 2, name: jobDetailCopy.notFoundTitle }),
    ).toBeVisible()
    captured.push(...(await captureAtEveryWidth(page, 'job-not-found')))

    await openInApp(page, '/no-such-page')
    await expect(page.getByRole('heading', { level: 1, name: notFoundCopy.heading })).toBeVisible()
    captured.push(...(await captureAtEveryWidth(page, 'page-not-found')))

    expect(captured.filter((path) => existsSync(path))).toHaveLength(8 * designWidths.length)
  })
})
