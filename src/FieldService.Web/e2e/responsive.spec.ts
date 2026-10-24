import type { Locator, Page, TestInfo } from '@playwright/test'

import { anyOf, consolePatterns, expect, stubSignedOutRestore, test } from './fixtures.ts'
import { demoTechnicians } from './support/api.ts'
import {
  assignCopy,
  cancelCopy,
  createJobCopy,
  dashboardCopy,
  jobsCopy,
  notFoundCopy,
  reportCopy,
  signInCopy,
  techniciansCopy,
} from './support/copy.ts'
import { brokenTimesAndDates } from './support/dashboard.ts'
import { createUnassignedJob, rowNamed } from './support/jobs.ts'
import { openInApp } from './support/run.ts'

const viewports = [
  { width: 375, height: 812 },
  { width: 768, height: 1024 },
  { width: 1280, height: 800 },
] as const

const workloadListBelow = 640

const techniciansListBelow = 900

interface Widths {
  readonly scroll: number
  readonly inner: number
}

async function pageWidths(page: Page): Promise<Widths> {
  return page.evaluate(() => ({
    scroll: document.documentElement.scrollWidth,
    inner: window.innerWidth,
  }))
}

async function expectNoPageScroll(page: Page, label: string): Promise<void> {
  const widths = await pageWidths(page)
  expect(widths.scroll, `${label}: page width against the window`).toBeLessThanOrEqual(widths.inner)
}

async function regionWidths(region: Locator): Promise<{ scroll: number; client: number }> {
  return region.evaluate((element) => ({
    scroll: element.scrollWidth,
    client: element.clientWidth,
  }))
}

async function expectOneForm(table: Locator, list: Locator, listShown: boolean): Promise<void> {
  await expect(listShown ? list : table).toBeVisible()
  await expect(listShown ? table : list).toBeHidden()
}

async function expectWorkloadForm(page: Page, width: number): Promise<void> {
  await expectOneForm(
    page.getByRole('table', { name: dashboardCopy.workload }),
    page.getByRole('list', { name: dashboardCopy.workload }),
    width < workloadListBelow,
  )
}

async function expectTechniciansForm(page: Page, width: number): Promise<void> {
  await expectOneForm(
    page.getByRole('region', { name: techniciansCopy.table, exact: true }),
    page.getByRole('list', { name: techniciansCopy.table, exact: true }),
    width < techniciansListBelow,
  )
}

async function capture(page: Page, testInfo: TestInfo, name: string): Promise<void> {
  await testInfo.attach(name, {
    body: await page.screenshot({ fullPage: true, caret: 'initial' }),
    contentType: 'image/png',
  })
}

test.describe('E2E-15 responsive layouts (W1, W2, W5 and W6 pages)', () => {
  test.use({ expectedConsole: anyOf(consolePatterns.anonymousRefresh) })

  test('every page has no horizontal page scroll at 375, 768 and 1280 px, the jobs table scrolls in its region, and the people lists replace the technician tables on narrow widths', async ({
    adminPage: page,
    adminSession,
    anonymousPage,
    api,
    runId,
  }, testInfo) => {
    const seeded = await api.findJobByNumber(1042)
    const cancelled = await api.findJobByNumber(1036)
    const report = await api.findJobByNumber(1039)
    const assignable = await createUnassignedJob(api, runId, 'responsive')
    const greeting = page.getByRole('heading', {
      level: 1,
      name: dashboardCopy.greeting(adminSession.user.displayName),
    })
    const dialog = page.getByRole('dialog', { name: cancelCopy.dialogTitle })
    await stubSignedOutRestore(anonymousPage)
    await anonymousPage.goto('/sign-in')
    await page.goto('/')
    await expect(greeting).toBeVisible()

    for (const viewport of viewports) {
      const width = viewport.width
      await anonymousPage.setViewportSize(viewport)
      await expect(
        anonymousPage.getByRole('heading', { level: 1, name: signInCopy.heading }),
      ).toBeVisible()
      await expectNoPageScroll(anonymousPage, `sign-in at ${width}`)
      await capture(anonymousPage, testInfo, `sign-in ${width}`)

      await page.setViewportSize(viewport)
      await openInApp(page, '/')
      await expect(greeting).toBeVisible()
      await expect(
        page.getByRole('region', { name: dashboardCopy.jobsByStatus }).getByRole('link'),
      ).toHaveCount(6)
      await expectWorkloadForm(page, width)
      await expectNoPageScroll(page, `dashboard at ${width}`)
      await capture(page, testInfo, `dashboard ${width}`)

      await openInApp(page, '/jobs')
      const region = page.getByRole('region', { name: jobsCopy.table })
      await expect(region.locator('tbody tr')).toHaveCount(12)
      await expectNoPageScroll(page, `jobs at ${width}`)
      const table = await regionWidths(region)
      expect(table.scroll, `jobs table width at ${width}`).toBeGreaterThanOrEqual(980)
      expect(table.scroll, `jobs table scrolls inside its region at ${width}`).toBeGreaterThan(
        table.client,
      )
      await capture(page, testInfo, `jobs ${width}`)

      await openInApp(page, `/jobs/${seeded.id}`)
      await expect(page.getByRole('heading', { level: 1, name: seeded.title })).toBeVisible()
      await expectNoPageScroll(page, `job detail at ${width}`)
      await capture(page, testInfo, `job detail ${width}`)

      await openInApp(page, `/jobs/${assignable.id}`)
      await expect(
        page
          .getByRole('region', { name: assignCopy.heading })
          .getByRole('radio', { name: rowNamed(demoTechnicians.assignments.name) }),
      ).toBeEnabled()
      await expectNoPageScroll(page, `job detail with the assign panel at ${width}`)
      await capture(page, testInfo, `job detail with the assign panel ${width}`)
      await page.getByRole('main').getByRole('button', { name: cancelCopy.trigger }).click()
      await expect(dialog).toBeVisible()
      await expectNoPageScroll(page, `the open cancel dialog at ${width}`)
      const dialogBox = await dialog.boundingBox()
      expect(dialogBox, 'the cancel dialog box').not.toBeNull()
      expect(
        (dialogBox?.x ?? -1) + (dialogBox?.width ?? 0),
        'the dialog inside the window',
      ).toBeLessThanOrEqual(width)
      await capture(page, testInfo, `cancel dialog ${width}`)
      await dialog.getByRole('button', { name: cancelCopy.keep }).click()
      await expect(dialog).toHaveCount(0)

      await openInApp(page, `/jobs/${cancelled.id}`)
      await expect(page.getByRole('heading', { level: 1, name: cancelled.title })).toBeVisible()
      await expect(page.getByRole('main').getByText(/^Cancelled (?:at|on) /)).toBeVisible()
      await expectNoPageScroll(page, `cancelled job at ${width}`)
      await capture(page, testInfo, `cancelled job ${width}`)

      await openInApp(page, '/jobs/new')
      await expect(
        page.getByRole('heading', { level: 1, name: createJobCopy.heading }),
      ).toBeVisible()
      await expectNoPageScroll(page, `new job at ${width}`)
      await capture(page, testInfo, `new job ${width}`)

      await openInApp(page, '/technicians')
      await expect(
        page.getByRole('heading', { level: 1, name: techniciansCopy.heading }),
      ).toBeVisible()
      await expectTechniciansForm(page, width)
      await expectNoPageScroll(page, `technicians at ${width}`)
      await capture(page, testInfo, `technicians ${width}`)

      await openInApp(page, `/jobs/${report.id}/inspection`)
      await expect(page.getByRole('heading', { level: 1, name: report.title })).toBeVisible()
      await expect(page.getByRole('region', { name: reportCopy.results })).toBeVisible()
      await expectNoPageScroll(page, `inspection report at ${width}`)
      await capture(page, testInfo, `inspection report ${width}`)

      await openInApp(page, '/no-such-page')
      await expect(
        page.getByRole('heading', { level: 1, name: notFoundCopy.heading }),
      ).toBeVisible()
      await expectNoPageScroll(page, `not found at ${width}`)
      await capture(page, testInfo, `not found ${width}`)
    }

    await page.setViewportSize({ width: 320, height: 800 })
    await openInApp(page, `/jobs/${cancelled.id}`)
    await expect(page.getByRole('heading', { level: 1, name: cancelled.title })).toBeVisible()
    const moments = await brokenTimesAndDates(page.getByRole('main'))
    expect(moments.checked, 'times and dates on the cancelled job at 320 px').toBeGreaterThan(0)
    expect(moments.broken, 'times and dates broken across lines at 320 px (D6-05)').toEqual([])
    await expectNoPageScroll(page, 'cancelled job at 320')
    await capture(page, testInfo, 'cancelled job 320')
  })
})
