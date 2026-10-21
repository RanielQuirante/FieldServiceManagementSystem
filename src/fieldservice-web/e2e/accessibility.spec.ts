import { randomUUID } from 'node:crypto'

import { AxeBuilder } from '@axe-core/playwright'
import type { Locator, Page, TestInfo } from '@playwright/test'

import {
  anyOf,
  consolePatterns,
  expect,
  fulfilStub,
  problemBody,
  stubSignedOutRestore,
  test,
} from './fixtures.ts'
import { demoAccounts, type JobStatus } from './support/api.ts'
import {
  createJobCopy,
  jobDetailCopy,
  jobsCopy,
  notFoundCopy,
  shellCopy,
  signInCopy,
  statusLabels,
  toastCopy,
} from './support/copy.ts'
import { e2eTitle, openInApp, readJsonFile, resultsFile, writeJsonFile } from './support/run.ts'

const axeTags = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa']

const widths = [
  { width: 1280, height: 800 },
  { width: 375, height: 812 },
] as const

const statuses: readonly JobStatus[] = [
  'Unassigned',
  'Assigned',
  'InProgress',
  'OnHold',
  'Completed',
  'Cancelled',
]

const emptyJobList = {
  items: [],
  page: 1,
  pageSize: 12,
  totalCount: 0,
  statusCounts: statuses.map((status) => ({ status, count: 0 })),
}

const jobListRequest = /\/api\/jobs\?/

const teal = 'rgb(15, 118, 110)'

const errorRed = 'rgb(180, 35, 24)'

const disabledText = 'rgb(124, 140, 161)'

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

async function audit(page: Page, testInfo: TestInfo, runId: string, state: string): Promise<void> {
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

async function pageScrollWidth(page: Page): Promise<number> {
  return page.evaluate(() => document.documentElement.scrollWidth)
}

async function raiseUnhandledRejection(page: Page, marker: string): Promise<void> {
  await page.evaluate((text) => {
    void Promise.reject(new Error(text))
  }, marker)
}

function errorToast(page: Page): Locator {
  return page
    .getByRole('region', { name: shellCopy.notifications })
    .getByRole('alert')
    .filter({ hasText: toastCopy.errorTitle })
}

async function searchJobs(page: Page, text: string): Promise<void> {
  const box = page.getByRole('searchbox', { name: jobsCopy.search })
  await box.fill(text)
  await box.press('Enter')
}

function jobsRegion(page: Page): Locator {
  return page.getByRole('region', { name: jobsCopy.table })
}

function tableRows(page: Page): Locator {
  return jobsRegion(page).locator('tbody tr')
}

async function borderColor(locator: Locator): Promise<string> {
  return locator.evaluate((element) => getComputedStyle(element).borderTopColor)
}

async function iconColor(button: Locator): Promise<string> {
  return button.locator('svg').evaluate((element) => getComputedStyle(element).color)
}

async function fitsInRegion(region: Locator, text: Locator): Promise<boolean> {
  const bounds = await region.evaluate((element) => {
    const box = element.getBoundingClientRect()
    return {
      left: box.left + element.clientLeft,
      right: box.left + element.clientLeft + element.clientWidth,
    }
  })
  const box = await text.boundingBox()
  return box !== null && box.x >= bounds.left - 0.5 && box.x + box.width <= bounds.right + 0.5
}

interface Coverage {
  readonly steps: number
  readonly max: number
  readonly worst: string
  readonly reachedStart: boolean
  readonly toastMeasured: boolean
}

async function shiftTabCoverage(page: Page, start: Locator, limit: number): Promise<Coverage> {
  await start.focus()
  let max = 0
  let worst = 'none'
  let toastMeasured = false
  for (let step = 1; step <= limit; step += 1) {
    await page.keyboard.press('Shift+Tab')
    const sample = await page.evaluate(
      ({ notifications, skipLink }) => {
        const host = document.querySelector(`[aria-label="${notifications}"]`)
        const toasts = host === null ? [] : [...host.querySelectorAll('[role="alert"]')]
        const measured = toasts.some((toast) => toast.getBoundingClientRect().height > 0)
        const active = document.activeElement
        if (!(active instanceof HTMLElement) || active === document.body) {
          return { done: true, coverage: 0, label: 'body', measured }
        }
        const box = active.getBoundingClientRect()
        const area = box.width * box.height
        let coverage = 0
        for (const toast of toasts) {
          if (toast.contains(active) || area === 0) {
            continue
          }
          const over = toast.getBoundingClientRect()
          const width = Math.max(0, Math.min(box.right, over.right) - Math.max(box.left, over.left))
          const height = Math.max(
            0,
            Math.min(box.bottom, over.bottom) - Math.max(box.top, over.top),
          )
          coverage = Math.max(coverage, (width * height) / area)
        }
        const name = active.getAttribute('aria-label') ?? active.textContent ?? ''
        const label = `${active.tagName.toLowerCase()} "${name.trim().slice(0, 40)}"`
        return { done: name.trim() === skipLink, coverage, label, measured }
      },
      { notifications: shellCopy.notifications, skipLink: shellCopy.skipLink },
    )
    toastMeasured = toastMeasured || sample.measured
    if (sample.coverage > max) {
      max = sample.coverage
      worst = sample.label
    }
    if (sample.done) {
      return { steps: step, max, worst, reachedStart: true, toastMeasured }
    }
  }
  return { steps: limit, max, worst, reachedStart: false, toastMeasured }
}

async function holdNextRequest(page: Page, url: string | RegExp): Promise<() => void> {
  let release: () => void = () => undefined
  const held = new Promise<void>((resolve) => {
    release = resolve
  })
  await page.route(
    url,
    async (route) => {
      await held
      await route.continue()
    },
    { times: 1 },
  )
  return () => release()
}

async function elementsStillMoving(page: Page): Promise<string[]> {
  return page.evaluate(() =>
    [...document.querySelectorAll('*')]
      .filter((element) => {
        const style = getComputedStyle(element)
        const durations = `${style.transitionDuration},${style.animationDuration}`
        return durations.split(',').some((value) => Number.parseFloat(value) > 0)
      })
      .map((element) => `${element.tagName.toLowerCase()}.${[...element.classList].join('.')}`),
  )
}

async function titlePaint(page: Page): Promise<{ fill: string; image: string; color: string }> {
  return page.getByRole('heading', { level: 1 }).evaluate((element) => {
    const style = getComputedStyle(element)
    return {
      fill: style.getPropertyValue('-webkit-text-fill-color'),
      image: style.backgroundImage,
      color: style.color,
    }
  })
}

function tomorrowParts(): { iso: string; typed: string } {
  const iso = new Date(Date.now() + 86_400_000).toISOString().slice(0, 10)
  const [year, month, day] = iso.split('-')
  return { iso, typed: `${month}${day}${year}` }
}

async function tabUntilFocused(page: Page, target: Locator, limit: number): Promise<number> {
  for (let presses = 1; presses <= limit; presses += 1) {
    await page.keyboard.press('Tab')
    if (await target.evaluate((element) => element === document.activeElement)) {
      return presses
    }
  }
  return limit + 1
}

interface SignInFailure {
  readonly status: number
  readonly code: string
  readonly text: string
  readonly headers: Readonly<Record<string, string>>
}

function textbox(page: Page, name: string, exact: boolean): Locator {
  return page.getByRole('textbox', { name, exact })
}

async function submitSignIn(page: Page): Promise<void> {
  await page.getByLabel(signInCopy.email, { exact: true }).fill(demoAccounts.administrator)
  await page.getByLabel(signInCopy.password, { exact: true }).fill(randomUUID())
  await page.getByRole('button', { name: signInCopy.submit, exact: true }).click()
}

const signInConsole = anyOf(
  consolePatterns.anonymousRefresh,
  consolePatterns.signInUnauthorized,
  consolePatterns.signInRefused,
  consolePatterns.signInRateLimited,
  consolePatterns.signInServerError,
)

test.describe('E2E-19 accessibility of the W1 routes and states', () => {
  for (const viewport of widths) {
    test(`sign-in states pass axe at ${viewport.width} px`, async ({
      anonymousPage: page,
      guards,
      runId,
    }, testInfo) => {
      guards.allowConsole(signInConsole)
      await stubSignedOutRestore(page)
      await page.setViewportSize(viewport)
      await page.goto('/sign-in?redirect=%2Fjobs')
      await expect(page.getByRole('heading', { level: 1, name: signInCopy.heading })).toBeVisible()
      await audit(page, testInfo, runId, `sign-in ${viewport.width}`)
      const email = page.getByLabel(signInCopy.email, { exact: true })
      const password = page.getByLabel(signInCopy.password, { exact: true })
      const submit = page.getByRole('button', { name: signInCopy.submit, exact: true })
      await page.keyboard.press('Tab')
      await expect(email).toBeFocused()
      await page.keyboard.press('Tab')
      await expect(password).toBeFocused()
      await page.keyboard.press('Tab')
      await expect(page.getByRole('button', { name: signInCopy.showPassword })).toBeFocused()
      await page.keyboard.press('Tab')
      await expect(submit).toBeFocused()

      await submit.click()

      await expect(email).toBeFocused()
      await expect(email).toHaveAccessibleDescription(signInCopy.emailRequired)
      await expect(password).toHaveAttribute('aria-invalid', 'true')
      await expect.poll(() => borderColor(email)).toBe(teal)
      await expect.poll(() => borderColor(password)).toBe(errorRed)
      await audit(page, testInfo, runId, `sign-in field errors ${viewport.width}`)
      const failures: readonly SignInFailure[] = [
        {
          status: 401,
          code: 'auth.invalid-credentials',
          text: signInCopy.invalidCredentials,
          headers: {},
        },
        {
          status: 403,
          code: 'auth.wrong-application',
          text: signInCopy.technicianAccount,
          headers: {},
        },
        {
          status: 429,
          code: 'rate-limit.exceeded',
          text: 'Try again in 42 seconds.',
          headers: { 'Retry-After': '42' },
        },
        { status: 500, code: 'server.error', text: 'Sign-in could not be completed.', headers: {} },
      ]
      for (const failure of failures) {
        await fulfilStub(
          page,
          '**/api/auth/sign-in',
          {
            status: failure.status,
            body: problemBody(failure.status, failure.code, 'Problem'),
            headers: failure.headers,
          },
          1,
        )
        await submitSignIn(page)
        await expect(page.getByRole('alert')).toContainText(failure.text)
        await audit(page, testInfo, runId, `sign-in ${failure.status} ${viewport.width}`)
      }
      await expect(page).toHaveURL('/sign-in?redirect=%2Fjobs')
    })

    test(`W1 routes and states pass axe at ${viewport.width} px`, async ({
      adminPage: page,
      api,
      guards,
      runId,
    }, testInfo) => {
      const marker = `E2E ${runId} toast ${viewport.width}`
      guards.allowPageErrors(consolePatterns.reportedError(marker))
      guards.allowConsole(
        anyOf(
          consolePatterns.reportedError(marker),
          consolePatterns.jobNotFound,
          consolePatterns.jobsServerError,
        ),
      )
      const seeded = await api.findJobByNumber(1042)
      await page.setViewportSize(viewport)
      await page.goto('/jobs')
      await expect(tableRows(page)).toHaveCount(12)
      await audit(page, testInfo, runId, `jobs ${viewport.width}`)

      await raiseUnhandledRejection(page, marker)

      await expect(errorToast(page)).toBeVisible()
      await audit(page, testInfo, runId, `jobs with an error toast ${viewport.width}`)
      await errorToast(page).getByRole('button', { name: toastCopy.dismiss }).click()
      await expect(errorToast(page)).toHaveCount(0)
      await searchJobs(page, `no match ${runId}`)
      await expect(page.getByRole('heading', { name: jobsCopy.noMatchesTitle })).toBeVisible()
      await audit(page, testInfo, runId, `jobs no matches ${viewport.width}`)
      await fulfilStub(
        page,
        jobListRequest,
        { status: 500, body: problemBody(500, 'server.error', 'An unexpected error occurred.') },
        1,
      )
      await page.getByRole('button', { name: jobsCopy.clearFilters }).click()
      await expect(page.getByRole('alert').filter({ hasText: jobsCopy.loadFailed })).toBeVisible()
      await audit(page, testInfo, runId, `jobs load error ${viewport.width}`)
      await page.getByRole('button', { name: jobsCopy.tryAgain }).click()
      await expect(tableRows(page)).toHaveCount(12)
      await searchJobs(page, `JOB-${seeded.number}`)
      await tableRows(page).getByRole('link', { name: seeded.title }).click()
      await expect(page.getByRole('heading', { level: 1, name: seeded.title })).toBeVisible()
      await audit(page, testInfo, runId, `job detail ${viewport.width}`)
      await page.getByRole('link', { name: shellCopy.newJob }).click()
      await expect(
        page.getByRole('combobox', { name: createJobCopy.checklistTemplate }),
      ).toBeEnabled()

      await page.getByRole('button', { name: createJobCopy.create }).click()

      const titleField = page.getByRole('textbox', { name: createJobCopy.title, exact: true })
      const customerField = page.getByRole('textbox', { name: createJobCopy.customer, exact: true })
      await expect(titleField).toBeFocused()
      await expect(titleField).toHaveAccessibleDescription(createJobCopy.titleRequired)
      await expect(customerField).toHaveAttribute('aria-invalid', 'true')
      await expect.poll(() => borderColor(titleField)).toBe(teal)
      await expect.poll(() => borderColor(customerField)).toBe(errorRed)
      await audit(page, testInfo, runId, `new job field errors ${viewport.width}`)
      await openInApp(page, `/jobs/${randomUUID()}`)
      await expect(
        page.getByRole('heading', { level: 2, name: jobDetailCopy.notFoundTitle }),
      ).toBeVisible()
      await audit(page, testInfo, runId, `job not found ${viewport.width}`)
      await openInApp(page, '/no-such-page')
      await expect(
        page.getByRole('heading', { level: 1, name: notFoundCopy.heading }),
      ).toBeVisible()
      await audit(page, testInfo, runId, `page not found ${viewport.width}`)
    })
  }

  test('every W1 route and state reflows at 320 px without page scroll', async ({
    adminPage: page,
    anonymousPage,
    api,
    guards,
    runId,
  }, testInfo) => {
    const marker = `E2E ${runId} toast 320`
    guards.allowPageErrors(consolePatterns.reportedError(marker))
    guards.allowConsole(
      anyOf(consolePatterns.reportedError(marker), consolePatterns.jobNotFound, signInConsole),
    )
    const viewport = { width: 320, height: 800 }
    const seeded = await api.findJobByNumber(1042)
    await stubSignedOutRestore(anonymousPage)
    await anonymousPage.setViewportSize(viewport)
    await anonymousPage.goto('/sign-in?redirect=%2Fjobs')
    await expect(
      anonymousPage.getByRole('heading', { level: 1, name: signInCopy.heading }),
    ).toBeVisible()
    expect(await pageScrollWidth(anonymousPage), 'sign-in').toBeLessThanOrEqual(320)
    await fulfilStub(
      anonymousPage,
      '**/api/auth/sign-in',
      { status: 500, body: problemBody(500, 'server.error', 'Problem') },
      1,
    )
    await submitSignIn(anonymousPage)
    await expect(anonymousPage.getByRole('alert')).toContainText('Sign-in could not be completed.')
    expect(
      await pageScrollWidth(anonymousPage),
      'sign-in failure with a reference',
    ).toBeLessThanOrEqual(320)

    await page.setViewportSize(viewport)
    await page.goto('/jobs')
    await expect(tableRows(page)).toHaveCount(12)
    expect(await pageScrollWidth(page), 'jobs').toBeLessThanOrEqual(320)
    await raiseUnhandledRejection(page, marker)
    await expect(errorToast(page)).toBeVisible()
    expect(await pageScrollWidth(page), 'jobs with an error toast').toBeLessThanOrEqual(320)
    const coverage = await shiftTabCoverage(
      page,
      page.getByRole('button', { name: jobsCopy.next }),
      80,
    )
    await testInfo.attach('toast coverage at 320', {
      body: JSON.stringify(coverage, null, 2),
      contentType: 'application/json',
    })
    writeJsonFile(resultsFile('axe', `${runId}-toast-coverage.json`), coverage)
    expect(coverage.reachedStart, 'Shift+Tab reached the skip link').toBe(true)
    expect(coverage.toastMeasured, 'the toast was on screen while focus moved').toBe(true)
    expect(
      coverage.max,
      `largest share of the focused element under the toast (${coverage.worst})`,
    ).toBeLessThan(1)
    await errorToast(page).getByRole('button', { name: toastCopy.dismiss }).click()

    await searchJobs(page, `no match ${runId}`)
    const noMatches = page.getByRole('heading', { name: jobsCopy.noMatchesTitle })
    await expect(noMatches).toBeVisible()
    expect(
      await fitsInRegion(jobsRegion(page), noMatches),
      'no-matches title inside the region',
    ).toBe(true)
    expect(
      await fitsInRegion(jobsRegion(page), page.getByText(jobsCopy.noMatchesBody)),
      'no-matches text inside the region',
    ).toBe(true)
    expect(await pageScrollWidth(page), 'jobs no matches').toBeLessThanOrEqual(320)
    await fulfilStub(page, jobListRequest, { status: 200, body: emptyJobList }, 1)
    await page.getByRole('button', { name: jobsCopy.clearFilters }).click()
    const noJobs = page.getByRole('heading', { name: jobsCopy.noJobsTitle })
    await expect(noJobs).toBeVisible()
    expect(await fitsInRegion(jobsRegion(page), noJobs), 'no-jobs title inside the region').toBe(
      true,
    )
    expect(
      await fitsInRegion(jobsRegion(page), page.getByText(jobsCopy.noJobsBody)),
      'no-jobs text inside the region',
    ).toBe(true)
    expect(await pageScrollWidth(page), 'jobs no jobs yet').toBeLessThanOrEqual(320)

    await searchJobs(page, `JOB-${seeded.number}`)
    const release = await holdNextRequest(page, `**/api/jobs/${seeded.id}`)
    await tableRows(page).getByRole('link', { name: seeded.title }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Loading job' })).toBeAttached()
    expect(await pageScrollWidth(page), 'job detail loading').toBeLessThanOrEqual(320)
    release()
    await expect(page.getByRole('heading', { level: 1, name: seeded.title })).toBeVisible()
    expect(await pageScrollWidth(page), 'job detail').toBeLessThanOrEqual(320)
    await page.getByRole('link', { name: shellCopy.newJob }).click()
    await page.getByRole('button', { name: createJobCopy.create }).click()
    await expect(
      page.getByRole('textbox', { name: createJobCopy.title, exact: true }),
    ).toBeFocused()
    expect(await pageScrollWidth(page), 'new job with field errors').toBeLessThanOrEqual(320)
    await openInApp(page, `/jobs/${randomUUID()}`)
    await expect(
      page.getByRole('heading', { level: 2, name: jobDetailCopy.notFoundTitle }),
    ).toBeVisible()
    expect(await pageScrollWidth(page), 'job not found').toBeLessThanOrEqual(320)
    await openInApp(page, '/no-such-page')
    await expect(page.getByRole('heading', { level: 1, name: notFoundCopy.heading })).toBeVisible()
    expect(await pageScrollWidth(page), 'page not found').toBeLessThanOrEqual(320)
  })

  test('reduced motion removes every transition and forced colours keep page titles visible', async ({
    adminPage: page,
    api,
  }) => {
    const seeded = await api.findJobByNumber(1042)
    await page.goto('/jobs')
    await expect(tableRows(page)).toHaveCount(12)
    expect(
      (await elementsStillMoving(page)).length,
      'transitions without a motion preference',
    ).toBeGreaterThan(0)
    expect((await titlePaint(page)).fill, 'gradient title without forced colours').toBe(
      'rgba(0, 0, 0, 0)',
    )

    await page.emulateMedia({ reducedMotion: 'reduce' })

    expect(await elementsStillMoving(page)).toEqual([])
    await page.emulateMedia({ reducedMotion: 'reduce', forcedColors: 'active' })
    const titled = [
      { path: '/jobs', title: jobsCopy.heading },
      { path: `/jobs/${seeded.id}`, title: seeded.title },
      { path: '/jobs/new', title: createJobCopy.heading },
    ]
    for (const { path, title } of titled) {
      await openInApp(page, path)
      await expect(page.getByRole('heading', { level: 1, name: title })).toBeVisible()
      const paint = await titlePaint(page)
      expect(paint.image, `title background on ${path}`).toBe('none')
      expect(paint.fill, `title fill on ${path} is an opaque colour`).toMatch(/^rgb\(/)
    }
  })

  test('a job is created by keyboard alone and the date picker stop shows focus', async ({
    adminPage: page,
    api,
    runId,
  }) => {
    const title = e2eTitle(runId, 'Exterior signage lights out (keyboard)')
    const date = tomorrowParts()
    const field = (name: string, exact: boolean = true): Locator => textbox(page, name, exact)
    const dateField = page.getByLabel(createJobCopy.date, { exact: true })
    const startField = page.getByLabel(createJobCopy.start, { exact: true })
    const endField = page.getByLabel(createJobCopy.end, { exact: true })
    const templateField = page.getByRole('combobox', { name: createJobCopy.checklistTemplate })
    await page.goto('/jobs')
    await expect(page.getByRole('heading', { level: 1, name: jobsCopy.heading })).toBeFocused()

    await page.keyboard.press('Shift+Tab')
    await expect(page.getByRole('link', { name: shellCopy.newJob })).toBeFocused()
    await page.keyboard.press('Enter')
    await expect(page).toHaveURL('/jobs/new')
    await expect(page.getByRole('heading', { level: 1, name: createJobCopy.heading })).toBeFocused()
    await expect(templateField).toBeEnabled()
    await page.keyboard.press('Tab')
    await expect(field(createJobCopy.title)).toBeFocused()
    await page.keyboard.type(title)
    await page.keyboard.press('Tab')
    await expect(field(createJobCopy.description)).toBeFocused()
    await page.keyboard.type('Both pylon signs at the north entrance are dark after 6 PM.')
    await page.keyboard.press('Tab')
    await expect(field(createJobCopy.equipment, false)).toBeFocused()
    await page.keyboard.press('Tab')
    await expect(field(createJobCopy.customer)).toBeFocused()
    await page.keyboard.type('Riverside Shopping Center')
    await page.keyboard.press('Tab')
    await expect(field(createJobCopy.siteAddress)).toBeFocused()
    await page.keyboard.type('500 Riverside Boulevard')
    await page.keyboard.press('Tab')
    await page.keyboard.press('Tab')
    await page.keyboard.press('Tab')
    await expect(field(createJobCopy.contactPhone, false)).toBeFocused()
    await page.keyboard.press('Tab')
    await expect(page.getByRole('radio', { name: 'Normal' })).toBeFocused()
    await page.keyboard.press('ArrowRight')
    await expect(page.getByRole('radio', { name: 'High' })).toBeChecked()
    await page.keyboard.press('Tab')
    await expect(dateField).toBeFocused()
    await page.keyboard.press('Tab')
    await page.keyboard.press('Tab')
    await page.keyboard.press('Tab')
    const pickerStop = await dateField.evaluate((element) => ({
      active: document.activeElement === element,
      focusWithin: element.matches(':focus-within'),
      border: getComputedStyle(element).borderTopColor,
    }))
    expect(pickerStop.active, 'the fourth stop belongs to the date field').toBe(true)
    expect(pickerStop.focusWithin, 'the date field matches :focus-within on its picker stop').toBe(
      true,
    )
    await expect.poll(() => borderColor(dateField)).toBe(teal)
    await page.keyboard.press('Shift+Tab')
    await page.keyboard.press('Shift+Tab')
    await page.keyboard.press('Shift+Tab')
    await page.keyboard.type(date.typed)
    expect(
      await tabUntilFocused(page, startField, 4),
      'Tab stops from the date to the start',
    ).toBeLessThanOrEqual(3)
    await page.keyboard.type('0900A')
    expect(
      await tabUntilFocused(page, endField, 6),
      'Tab stops from the start to the end',
    ).toBeLessThanOrEqual(5)
    await page.keyboard.type('1100A')
    expect(
      await tabUntilFocused(page, templateField, 6),
      'Tab stops from the end to the template',
    ).toBeLessThanOrEqual(5)
    await page.keyboard.type('General')
    await expect(templateField).toHaveValue(/.+/)
    await page.keyboard.press('Tab')
    await expect(page.getByRole('button', { name: createJobCopy.create })).toBeFocused()
    const created = page.waitForResponse(
      (response) => response.url().endsWith('/api/jobs') && response.request().method() === 'POST',
    )
    await page.keyboard.press('Enter')

    const response = await created
    expect(response.status()).toBe(201)
    const job = (await response.json()) as { id: string }
    await expect(page).toHaveURL(`/jobs/${job.id}`)
    await expect(page.getByRole('heading', { level: 1, name: title })).toBeVisible()
    const stored = await api.getJob(job.id)
    expect(stored).toMatchObject({
      title,
      priority: 'High',
      status: 'Unassigned',
      checklistTemplate: { name: 'General electrical inspection' },
    })
    expect(Date.parse(stored.scheduledStart)).toBe(Date.parse(`${date.iso}T09:00:00Z`))
    expect(Date.parse(stored.scheduledEnd)).toBe(Date.parse(`${date.iso}T11:00:00Z`))
  })

  test('design review checks: pagination chevrons, empty-state cell, focus after Clear filters and Try again', async ({
    adminPage: page,
    api,
    guards,
    runId,
  }) => {
    guards.allowConsole(consolePatterns.jobsServerError)
    const lastPage = Math.ceil((await api.getJobs({ pageSize: 12 })).totalCount / 12)
    await page.goto('/jobs')
    await expect(tableRows(page)).toHaveCount(12)

    await expect.poll(() => iconColor(page.getByRole('button', { name: jobsCopy.next }))).toBe(teal)
    await expect
      .poll(() => iconColor(page.getByRole('button', { name: jobsCopy.previous })))
      .toBe(disabledText)
    await openInApp(page, `/jobs?page=${lastPage}`)
    await expect(page.getByRole('button', { name: jobsCopy.next })).toBeDisabled()
    await expect
      .poll(() => iconColor(page.getByRole('button', { name: jobsCopy.previous })))
      .toBe(teal)
    await expect
      .poll(() => iconColor(page.getByRole('button', { name: jobsCopy.next })))
      .toBe(disabledText)

    await searchJobs(page, `no match ${runId}`)
    await expect(page.getByRole('heading', { name: jobsCopy.noMatchesTitle })).toBeVisible()
    const cell = jobsRegion(page).getByRole('cell')
    const padding = await cell.evaluate((element) => getComputedStyle(element).padding)
    expect(padding).toBe('0px')
    expect(await jobsRegion(page).evaluate((element) => element.scrollWidth)).toBe(980)
    await page.getByRole('button', { name: jobsCopy.clearFilters }).click()
    await expect(page.getByRole('searchbox', { name: jobsCopy.search })).toBeFocused()

    await fulfilStub(
      page,
      jobListRequest,
      { status: 500, body: problemBody(500, 'server.error', 'An unexpected error occurred.') },
      1,
    )
    await page
      .getByRole('group', { name: jobsCopy.statusFilter })
      .getByRole('button', { name: new RegExp(`^${statusLabels.OnHold}`) })
      .click()
    await expect(page.getByRole('alert').filter({ hasText: jobsCopy.loadFailed })).toBeVisible()
    await page.getByRole('button', { name: jobsCopy.tryAgain }).click()
    await expect(tableRows(page).first()).toBeVisible()
    await expect(jobsRegion(page)).toBeFocused()
  })
})
