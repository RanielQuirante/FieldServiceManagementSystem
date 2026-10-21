import type { Page, TestInfo } from '@playwright/test'

import { anyOf, consolePatterns, expect, stubSignedOutRestore, test } from './fixtures.ts'
import { createJobCopy, jobsCopy, signInCopy } from './support/copy.ts'
import { openInApp } from './support/run.ts'

const viewports = [
  { width: 375, height: 812 },
  { width: 768, height: 1024 },
  { width: 1280, height: 800 },
] as const

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

async function capture(page: Page, testInfo: TestInfo, name: string): Promise<void> {
  await testInfo.attach(name, {
    body: await page.screenshot({ fullPage: true, caret: 'initial' }),
    contentType: 'image/png',
  })
}

test.describe('E2E-15 responsive layouts (W1 pages)', () => {
  test.use({ expectedConsole: anyOf(consolePatterns.anonymousRefresh) })

  test('the W1 pages have no horizontal page scroll at 375, 768 and 1280 px and the jobs table scrolls in its region', async ({
    adminPage: page,
    anonymousPage,
    api,
  }, testInfo) => {
    const seeded = await api.findJobByNumber(1042)
    await stubSignedOutRestore(anonymousPage)
    await anonymousPage.goto('/sign-in')
    await page.goto('/jobs')

    for (const viewport of viewports) {
      await anonymousPage.setViewportSize(viewport)
      await expect(
        anonymousPage.getByRole('heading', { level: 1, name: signInCopy.heading }),
      ).toBeVisible()
      const signIn = await pageWidths(anonymousPage)
      expect(signIn.scroll, `sign-in at ${viewport.width}`).toBeLessThanOrEqual(signIn.inner)
      await capture(anonymousPage, testInfo, `sign-in ${viewport.width}`)

      await page.setViewportSize(viewport)
      await openInApp(page, '/jobs')
      const region = page.getByRole('region', { name: jobsCopy.table })
      await expect(region.locator('tbody tr')).toHaveCount(12)
      const jobs = await pageWidths(page)
      expect(jobs.scroll, `jobs at ${viewport.width}`).toBeLessThanOrEqual(jobs.inner)
      const table = await region.evaluate((element) => ({
        scroll: element.scrollWidth,
        client: element.clientWidth,
      }))
      expect(table.scroll, `jobs table width at ${viewport.width}`).toBeGreaterThanOrEqual(980)
      expect(
        table.scroll,
        `jobs table scrolls inside its region at ${viewport.width}`,
      ).toBeGreaterThan(table.client)
      await capture(page, testInfo, `jobs ${viewport.width}`)

      await openInApp(page, `/jobs/${seeded.id}`)
      await expect(page.getByRole('heading', { level: 1, name: seeded.title })).toBeVisible()
      const detail = await pageWidths(page)
      expect(detail.scroll, `job detail at ${viewport.width}`).toBeLessThanOrEqual(detail.inner)
      await capture(page, testInfo, `job detail ${viewport.width}`)

      await openInApp(page, '/jobs/new')
      await expect(
        page.getByRole('heading', { level: 1, name: createJobCopy.heading }),
      ).toBeVisible()
      const form = await pageWidths(page)
      expect(form.scroll, `new job at ${viewport.width}`).toBeLessThanOrEqual(form.inner)
      await capture(page, testInfo, `new job ${viewport.width}`)
    }
  })
})
