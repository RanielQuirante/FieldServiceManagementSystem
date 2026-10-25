import { randomUUID } from 'node:crypto'

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
import { demoAccounts, demoTechnicians, type JobStatus } from './support/api.ts'
import {
  assignCopy,
  createJobCopy,
  historyCopy,
  jobDetailCopy,
  jobsCopy,
  liveCopy,
  notFoundCopy,
  shellCopy,
  signInCopy,
  statusLabels,
  toastCopy,
} from './support/copy.ts'
import {
  assignRowName,
  createAssignedJob,
  createOnHoldJob,
  createUnassignedJob,
  expectedAssignOrder,
  holdNoteFor,
  jobWithDraftInspection,
  momentSpansOf,
  rowNamed,
  seededJobInStatus,
} from './support/jobs.ts'
import { audit } from './support/axe.ts'
import { e2eTitle, openInApp, resultsFile, writeJsonFile } from './support/run.ts'
import { SocketGate } from './support/websocket.ts'

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

function historyItems(page: Page): Locator {
  return page.getByRole('region', { name: jobDetailCopy.history }).getByRole('listitem')
}

function assignPanel(page: Page): Locator {
  return page.getByRole('region', { name: assignCopy.heading })
}

function assignRow(page: Page, displayName: string): Locator {
  return assignPanel(page).getByRole('radio', { name: rowNamed(displayName) })
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
        const toasts = host === null ? [] : [...host.querySelectorAll('[data-toast-id]')]
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

async function arrowDownUntilFocused(page: Page, target: Locator, limit: number): Promise<number> {
  for (let presses = 0; presses <= limit; presses += 1) {
    if (await target.evaluate((element) => element === document.activeElement)) {
      return presses
    }
    await page.keyboard.press('ArrowDown')
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

test.describe('E2E-19 accessibility of the W1 to W3 routes and states', () => {
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

    test(`W1 to W3 routes and states pass axe at ${viewport.width} px`, async ({
      adminPage: page,
      api,
      guards,
      technician,
      runId,
    }, testInfo) => {
      const marker = `E2E ${runId} toast ${viewport.width}`
      guards.allowPageErrors(consolePatterns.reportedError(marker))
      guards.allowConsole(
        anyOf(
          consolePatterns.reportedError(marker),
          consolePatterns.jobNotFound,
          consolePatterns.jobsServerError,
          consolePatterns.assignmentConflict,
        ),
      )
      const seeded = await seededJobInStatus(api, 'InProgress')
      const assignable = await createUnassignedJob(api, runId, `axe ${viewport.width}`)
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
      await expect(tableRows(page)).toHaveCount(1)
      await tableRows(page).getByRole('link', { name: seeded.title, exact: true }).click()
      await expect(page.getByRole('heading', { level: 1, name: seeded.title })).toBeVisible()
      await audit(page, testInfo, runId, `job detail ${viewport.width}`)
      await openInApp(page, `/jobs/${assignable.id}`)
      const lena = assignRow(page, demoTechnicians.assignments.name)
      await expect(lena).toBeEnabled()
      await audit(page, testInfo, runId, `job detail with the assign panel ${viewport.width}`)
      await lena.check()
      const assignToLena = assignPanel(page).getByRole('button', {
        name: assignCopy.assignTo(demoTechnicians.assignments.name),
      })
      await expect(assignToLena).toBeVisible()
      await audit(page, testInfo, runId, `assign panel with a technician chosen ${viewport.width}`)
      await fulfilStub(
        page,
        `**/api/jobs/${assignable.id}/assignment`,
        {
          status: 409,
          body: problemBody(
            409,
            'concurrency.conflict',
            'The resource was changed by someone else.',
          ),
        },
        1,
      )
      await assignToLena.click()
      await expect(
        assignPanel(page)
          .getByRole('alert')
          .filter({ hasText: assignCopy.changedTitle(`JOB-${assignable.number}`) }),
      ).toBeVisible()
      await expect(assignToLena).toBeFocused()
      await audit(
        page,
        testInfo,
        runId,
        `job detail after a stale-version conflict ${viewport.width}`,
      )
      const lenaApi = await technician(demoTechnicians.assignments.email)
      const note = holdNoteFor(runId)
      const held = await createOnHoldJob(api, lenaApi, runId, `axe ${viewport.width} on hold`, note)
      await openInApp(page, `/jobs/${held.id}`)
      await expect(historyItems(page)).toHaveCount(4)
      await expect(historyItems(page).last()).toContainText(historyCopy.note(note))
      await audit(page, testInfo, runId, `job detail on hold with its note ${viewport.width}`)
      await lenaApi.resume(held.id)
      await openInApp(page, '/jobs?status=InProgress')
      await expect(
        page
          .getByRole('group', { name: jobsCopy.statusFilter })
          .getByRole('button', { name: new RegExp(`^${statusLabels.InProgress}`) }),
      ).toHaveAttribute('aria-pressed', 'true')
      const heldRow = tableRows(page).filter({
        has: page.getByRole('link', { name: held.title, exact: true }),
      })
      await expect(heldRow).toHaveCount(1)
      await audit(page, testInfo, runId, `jobs filtered by In progress ${viewport.width}`)
      await heldRow.getByRole('link', { name: held.title, exact: true }).click()
      await expect(historyItems(page)).toHaveCount(5)
      await audit(
        page,
        testInfo,
        runId,
        `job detail after start, hold and resume ${viewport.width}`,
      )
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

  test('every W1 to W3 route and state reflows at 320 px without page scroll', async ({
    adminPage: page,
    anonymousPage,
    api,
    guards,
    sentinelSink,
    technician,
    runId,
  }, testInfo) => {
    const marker = `E2E ${runId} toast 320`
    guards.allowPageErrors(consolePatterns.reportedError(marker))
    guards.allowConsole(
      anyOf(consolePatterns.reportedError(marker), consolePatterns.jobNotFound, signInConsole),
    )
    const viewport = { width: 320, height: 800 }
    const seeded = await seededJobInStatus(api, 'InProgress')
    const assignable = await createUnassignedJob(api, runId, 'reflow 320')
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
    await expect(tableRows(page)).toHaveCount(1)
    const release = await holdNextRequest(page, `**/api/jobs/${seeded.id}`)
    await tableRows(page).getByRole('link', { name: seeded.title, exact: true }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Loading job' })).toBeAttached()
    expect(await pageScrollWidth(page), 'job detail loading').toBeLessThanOrEqual(320)
    release()
    await expect(page.getByRole('heading', { level: 1, name: seeded.title })).toBeVisible()
    expect(await pageScrollWidth(page), 'job detail').toBeLessThanOrEqual(320)
    await openInApp(page, `/jobs/${assignable.id}`)
    const lena = assignRow(page, demoTechnicians.assignments.name)
    await expect(lena).toBeEnabled()
    expect(await pageScrollWidth(page), 'job detail with the assign panel').toBeLessThanOrEqual(320)
    const moments = await momentSpansOf(
      assignPanel(page).getByRole('group', { name: assignCopy.group }),
    )
    await testInfo.attach('assign row moments at 320', {
      body: JSON.stringify(moments, null, 2),
      contentType: 'application/json',
    })
    expect(moments.length, 'times and dates in the assign rows').toBeGreaterThan(0)
    expect(
      moments.filter((moment) => moment.whiteSpace !== 'nowrap' || moment.lineBoxes !== 1),
      'times and dates that wrap at 320 px',
    ).toEqual([])
    await lena.check()
    await expect(
      assignPanel(page).getByRole('button', {
        name: assignCopy.assignTo(demoTechnicians.assignments.name),
      }),
    ).toBeVisible()
    expect(
      await pageScrollWidth(page),
      'assign panel with a technician chosen',
    ).toBeLessThanOrEqual(320)
    const lenaApi = await technician(demoTechnicians.assignments.email)
    const longNote = `HoldNote${runId}`.repeat(40).slice(0, 500)
    sentinelSink('hold note', longNote)
    const held = await createOnHoldJob(api, lenaApi, runId, 'reflow 320 on hold', longNote)
    await openInApp(page, `/jobs/${held.id}`)
    const history = page.getByRole('region', { name: jobDetailCopy.history })
    await expect(historyItems(page).last()).toContainText(historyCopy.note(longNote))
    expect(
      await pageScrollWidth(page),
      'job detail on hold with a 500-character note without spaces',
    ).toBeLessThanOrEqual(320)
    expect(
      await fitsInRegion(history, history.getByText(historyCopy.note(longNote))),
      'the 500-character note inside the history region',
    ).toBe(true)
    await lenaApi.resume(held.id)
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

  test('a job is created and assigned by keyboard alone and the date picker stop shows focus', async ({
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
    const technicianField = page.getByRole('combobox', { name: createJobCopy.technician })
    const technicians = await api.technicians()
    const lena = await api.technicianNamed(demoTechnicians.assignments.name)
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
    await expect(technicianField).toBeFocused()
    await expect(technicianField).toHaveValue('')
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

    const search = assignPanel(page).getByRole('searchbox', { name: assignCopy.search })
    await expect(search).toBeEnabled()
    const electricalRows = assignPanel(page)
      .getByRole('group', { name: assignCopy.group })
      .getByRole('radio')
    const electricalOrder = expectedAssignOrder(technicians, stored.checklistTemplate.trade)
    await expect(electricalRows).toHaveCount(electricalOrder.length)
    for (const [index, member] of electricalOrder.entries()) {
      await expect(electricalRows.nth(index)).toHaveAccessibleName(
        assignRowName(member, stored.checklistTemplate.trade),
      )
    }
    expect(
      await tabUntilFocused(page, search, 40),
      'Tab stops from the page to the technician search',
    ).toBeLessThanOrEqual(40)
    await page.keyboard.press('Tab')
    const row = assignRow(page, lena.displayName)
    expect(
      await arrowDownUntilFocused(page, row, 8),
      'ArrowDown presses from the first row to Lena Park',
    ).toBeLessThanOrEqual(8)
    await expect(row).toBeFocused()
    await page.keyboard.press('Space')
    await expect(row).toBeChecked()
    await page.keyboard.press('Tab')
    const assignToLena = assignPanel(page).getByRole('button', {
      name: assignCopy.assignTo(lena.displayName),
    })
    await expect(assignToLena).toBeFocused()
    const assigned = page.waitForResponse(
      (response) =>
        response.url().endsWith(`/api/jobs/${job.id}/assignment`) &&
        response.request().method() === 'PUT',
    )
    await page.keyboard.press('Enter')

    expect((await assigned).status()).toBe(200)
    await expect(
      page.getByRole('status').filter({ hasText: assignCopy.assigned(lena.displayName) }),
    ).toBeVisible()
    expect(await api.getJob(job.id)).toMatchObject({
      status: 'Assigned',
      assignedTechnician: { id: lena.id },
    })
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

test.describe('E2E-19 accessibility of the W4 live states', () => {
  test('the Live pill with an event toast and a row flash, the Reconnecting banner and Back online pass axe at 1280 and 375 px and reflow at 320 px', async ({
    adminPage: page,
    api,
    guards,
    technician,
    runId,
  }, testInfo) => {
    const lenaApi = await technician(demoTechnicians.assignments.email)
    const lena = lenaApi.user
    const job = await createAssignedJob(api, runId, 'live states', lena.id)
    const gate = new SocketGate()
    await gate.install(page, guards)
    const pill = page.getByRole('banner').getByText(liveCopy.pillAnyState)
    const notice = page.getByRole('main').getByRole('status').first()
    const notifications = page.getByRole('region', { name: shellCopy.notifications })
    const row = jobsRegion(page)
      .locator('tbody tr')
      .filter({ has: page.getByRole('link', { name: job.title, exact: true }) })

    await page.goto('/jobs')
    await expect(pill).toHaveText(liveCopy.pillLive, { timeout: 10_000 })
    await lenaApi.start(job.id)
    await expect(
      notifications.getByText(liveCopy.started(lena.displayName), { exact: true }),
    ).toBeVisible()
    await expect(row).toContainText(liveCopy.justNow)

    await audit(page, testInfo, runId, 'jobs with an event toast and a row flash 1280')
    await page.setViewportSize(widths[1])
    await expect(pill).toHaveText(liveCopy.pillLive)
    await audit(page, testInfo, runId, 'jobs with an event toast and a row flash 375')

    await gate.drop()
    await expect(notice).toHaveText(liveCopy.reconnectingBanner, { timeout: 6_000 })
    await expect(pill).toHaveText(liveCopy.pillReconnecting)
    await audit(page, testInfo, runId, 'the Reconnecting banner 375')
    await page.setViewportSize({ width: 320, height: 800 })
    expect(await pageScrollWidth(page), 'the Reconnecting banner at 320 px').toBeLessThanOrEqual(
      320,
    )
    await page.setViewportSize(widths[0])
    await audit(page, testInfo, runId, 'the Reconnecting banner 1280')

    gate.allow()
    await expect(notice).toHaveText(liveCopy.backOnlineBanner, { timeout: 12_000 })
    await audit(page, testInfo, runId, 'Back online 1280')
  })
})

async function captureState(page: Page, testInfo: TestInfo, name: string): Promise<void> {
  const path = testInfo.outputPath(`${name}.png`)
  await page.screenshot({ path, fullPage: true })
  await testInfo.attach(name, { path, contentType: 'image/png' })
}

test.describe('E2E-19 accessibility of the W5 inspection report', () => {
  test.use({
    expectedConsole: anyOf(consolePatterns.jobNotFound, consolePatterns.inspectionServerError),
  })

  test('the submitted, not submitted, not started, loading, failed and not-found reports pass axe at 1280 and 375 px, reflow at 320 px, and keep their title in forced colours and reduced transparency', async ({
    adminPage: page,
    api,
    runId,
  }, testInfo) => {
    const submitted = await api.getJob((await api.findJobByNumber(1039)).id)
    const draft = await jobWithDraftInspection(api)
    const notStarted = await seededJobInStatus(api, 'Assigned')
    const states = [
      { slug: 'submitted', job: submitted },
      { slug: 'not-submitted', job: draft },
      { slug: 'not-started', job: notStarted },
    ] as const
    const reportOf = (id: string): string => `/jobs/${id}/inspection`

    await page.goto('/jobs')
    await expect(tableRows(page)).toHaveCount(12)
    for (const viewport of widths) {
      await page.setViewportSize(viewport)
      for (const state of states) {
        await openInApp(page, reportOf(state.job.id))
        await expect(page.getByRole('heading', { level: 1, name: state.job.title })).toBeVisible()
        await expect(page.getByRole('region', { name: 'Inspection results' })).toBeVisible()
        await audit(page, testInfo, runId, `report ${state.slug} ${viewport.width}`)
        await captureState(page, testInfo, `w5-report-${state.slug}-${viewport.width}`)
      }

      const release = await holdNextRequest(page, `**/api/jobs/${draft.id}/inspection`)
      await openInApp(page, reportOf(draft.id))
      await expect(
        page.getByRole('status').filter({ hasText: 'Loading inspection report' }),
      ).toBeAttached()
      await audit(page, testInfo, runId, `report loading ${viewport.width}`)
      await captureState(page, testInfo, `w5-report-loading-${viewport.width}`)
      release()
      await expect(page.getByRole('heading', { level: 1, name: draft.title })).toBeVisible()

      await fulfilStub(
        page,
        `**/api/jobs/${submitted.id}/inspection`,
        { status: 500, body: problemBody(500, 'server.error', 'An unexpected error occurred.') },
        1,
      )
      await openInApp(page, reportOf(submitted.id))
      await expect(page.getByText('The inspection report could not be loaded.')).toBeVisible()
      await audit(page, testInfo, runId, `report load failure ${viewport.width}`)
      await captureState(page, testInfo, `w5-report-failure-${viewport.width}`)

      await openInApp(page, reportOf(randomUUID()))
      await expect(
        page.getByRole('heading', { level: 1, name: jobDetailCopy.notFoundHeading }),
      ).toBeVisible()
      await audit(page, testInfo, runId, `report job not found ${viewport.width}`)
    }

    await page.setViewportSize({ width: 320, height: 800 })
    for (const state of states) {
      await openInApp(page, reportOf(state.job.id))
      await expect(page.getByRole('heading', { level: 1, name: state.job.title })).toBeVisible()
      await expect(page.getByRole('region', { name: 'Inspection results' })).toBeVisible()
      expect(await pageScrollWidth(page), `report ${state.slug} at 320 px`).toBeLessThanOrEqual(320)
      await captureState(page, testInfo, `w5-report-${state.slug}-320`)
    }

    await page.setViewportSize({ width: 768, height: 1024 })
    for (const state of states) {
      await openInApp(page, reportOf(state.job.id))
      await expect(page.getByRole('heading', { level: 1, name: state.job.title })).toBeVisible()
      await captureState(page, testInfo, `w5-report-${state.slug}-768`)
    }

    await page.setViewportSize(widths[0])
    await page.emulateMedia({ forcedColors: 'active' })
    await openInApp(page, reportOf(submitted.id))
    await expect(page.getByRole('heading', { level: 1, name: submitted.title })).toBeVisible()
    const paint = await titlePaint(page)
    expect(paint.image, 'report title background in forced colours').toBe('none')
    expect(paint.fill, 'report title fill in forced colours').toMatch(/^rgb\(/)
    await captureState(page, testInfo, 'w5-report-forced-colours-1280')
    await page.emulateMedia({ forcedColors: 'none' })

    const cdp = await page.context().newCDPSession(page)
    try {
      await cdp.send('Emulation.setEmulatedMedia', {
        features: [{ name: 'prefers-reduced-transparency', value: 'reduce' }],
      })
      expect(
        await page.evaluate(() => matchMedia('(prefers-reduced-transparency: reduce)').matches),
        'reduced transparency is emulated',
      ).toBe(true)
      await openInApp(page, reportOf(draft.id))
      await expect(page.getByRole('heading', { level: 1, name: draft.title })).toBeVisible()
      await audit(page, testInfo, runId, 'report reduced transparency 1280')
      await captureState(page, testInfo, 'w5-report-reduced-transparency-1280')
    } finally {
      await cdp.send('Emulation.setEmulatedMedia', { features: [] })
      await cdp.detach()
    }
  })
})
