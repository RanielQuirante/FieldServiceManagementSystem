import type { Locator, Page, Response, TestInfo } from '@playwright/test'

import { anyOf, consolePatterns, expect, fulfilStub, problemBody, test } from './fixtures.ts'
import {
  demoTechnicians,
  type DashboardSummary,
  type DashboardUnassignedJob,
  type Technician,
} from './support/api.ts'
import { audit } from './support/axe.ts'
import {
  dashboardCopy,
  documentTitle,
  jobsCopy,
  liveCopy,
  shellCopy,
  statusLabels,
  tradeLabels,
} from './support/copy.ts'
import {
  brokenTimesAndDates,
  emptySummary,
  expectedTiles,
  heroSentence,
  holdNext,
  isDashboardSummary,
  isTechniciansRead,
  shiftLabelOf,
  summaryUrl,
  techniciansUrl,
  trackRequests,
  utcDayWindow,
} from './support/dashboard.ts'
import { createAssignedJob, holdNoteFor } from './support/jobs.ts'
import { SocketGate } from './support/websocket.ts'

const serverError = {
  status: 500,
  body: problemBody(500, 'server.error', 'An unexpected error occurred.'),
} as const

const failureReference = (serverError.body as { correlationId: string }).correlationId

const markerName = '__e2eDashboardMarker'

const newEntryMilliseconds = 12_000

interface DashboardParts {
  readonly hero: Locator
  readonly status: Locator
  readonly tiles: Locator
  readonly needs: Locator
  readonly activity: Locator
  readonly entries: Locator
  readonly workloadTable: Locator
  readonly workloadList: Locator
}

function partsOf(page: Page, displayName: string): DashboardParts {
  const status = page.getByRole('region', { name: dashboardCopy.jobsByStatus })
  const activity = page.getByRole('region', { name: dashboardCopy.liveActivity })
  return {
    hero: page.getByRole('region', { name: dashboardCopy.greeting(displayName) }),
    status,
    tiles: status.getByRole('link'),
    needs: page.getByRole('region', { name: dashboardCopy.needsTechnician }),
    activity,
    entries: activity.getByRole('listitem'),
    workloadTable: page.getByRole('table', { name: dashboardCopy.workload }),
    workloadList: page.getByRole('list', { name: dashboardCopy.workload }),
  }
}

function dataRows(table: Locator): Locator {
  return table.getByRole('row').filter({ has: table.page().getByRole('cell') })
}

function navigation(page: Page): Locator {
  return page.getByRole('navigation', { name: shellCopy.mainNavigation })
}

function pill(page: Page): Locator {
  return page.getByRole('banner').getByText(liveCopy.pillAnyState)
}

function newTagShown(shown: boolean): boolean {
  return (document.querySelector('.live-activity .count-capsule--new') !== null) === shown
}

function tileName(label: string, value: number): RegExp {
  return new RegExp(`^${label}, ${value}(?:, .+)?$`)
}

async function leaveForJobs(page: Page): Promise<void> {
  await navigation(page).getByRole('link', { name: jobsCopy.heading }).click()
  await expect(page).toHaveURL('/jobs')
  await expect(page.getByRole('combobox', { name: jobsCopy.technicianFilter })).toBeEnabled()
}

async function openDashboard(page: Page): Promise<void> {
  await navigation(page).getByRole('link', { name: dashboardCopy.heading }).click()
  await expect(page).toHaveURL('/')
}

async function readItems<T>(response: Response): Promise<T> {
  return (await response.json()) as T
}

async function expectReadyDashboard(
  parts: DashboardParts,
  summary: DashboardSummary,
  technicians: readonly Technician[],
): Promise<void> {
  await expect(
    parts.hero.getByText(heroSentence(summary.openJobs, summary.range), { exact: true }),
  ).toBeVisible()
  await expect(
    parts.hero.getByText(`${summary.technicians.onShift} of ${summary.technicians.total}`, {
      exact: true,
    }),
  ).toBeVisible()
  await expect(parts.hero.getByText(dashboardCopy.onShiftCaption, { exact: true })).toBeVisible()
  await expect(
    parts.hero.getByText(String(summary.openJobs.withTechnician), { exact: true }),
  ).toBeVisible()
  await expect(parts.hero.getByText(dashboardCopy.assignedCaption, { exact: true })).toBeVisible()
  await expect(
    parts.status.getByText(
      dashboardCopy.statusSubline(
        summary.openJobs.total,
        summary.range.completed,
        summary.range.cancelled,
      ),
      { exact: true },
    ),
  ).toBeVisible()
  const tiles = expectedTiles(summary)
  await expect(parts.tiles).toHaveCount(tiles.length)
  for (const [index, tile] of tiles.entries()) {
    const link = parts.tiles.nth(index)
    await expect(link).toHaveAccessibleName(
      dashboardCopy.tileName(tile.label, tile.value, tile.subline),
    )
    await expect(link).toHaveAttribute('href', `/jobs?status=${tile.status}`)
  }
  const needsRows = parts.needs.getByRole('listitem')
  await expect(needsRows).toHaveCount(summary.needsTechnician.length)
  for (const [index, job] of summary.needsTechnician.entries()) {
    const row = needsRows.nth(index)
    await expect(row).toContainText(`JOB-${job.number}`)
    await expect(row).toContainText(job.title)
    await expect(
      row.getByRole('link', { name: dashboardCopy.assign(`JOB-${job.number}`), exact: true }),
    ).toHaveAttribute('href', `/jobs/${job.id}`)
  }
  const unassignedWords = summary.openJobs.unassigned === 1 ? 'unassigned job' : 'unassigned jobs'
  await expect(
    parts.needs.getByText(`${summary.openJobs.unassigned} ${unassignedWords}`, { exact: true }),
  ).toBeAttached()
  const viewAll = parts.needs.getByRole('link', {
    name: dashboardCopy.viewAll(summary.openJobs.unassigned),
  })
  if (summary.openJobs.unassigned > summary.needsTechnician.length) {
    await expect(viewAll).toHaveAttribute('href', '/jobs?status=Unassigned')
  } else {
    await expect(viewAll).toHaveCount(0)
  }
  await expect(parts.entries).toHaveCount(summary.recentActivity.length)
  for (const [index, entry] of summary.recentActivity.entries()) {
    const row = parts.entries.nth(index)
    await expect(row).toContainText(entry.actor.displayName)
    await expect(
      row.getByRole('link', { name: `JOB-${entry.jobNumber}`, exact: true }),
    ).toHaveAttribute('href', `/jobs/${entry.jobId}`)
    await expect(row.getByText(dashboardCopy.newTag, { exact: true })).toHaveCount(0)
  }
  const rows = dataRows(parts.workloadTable)
  await expect(rows).toHaveCount(technicians.length)
  for (const [index, technician] of technicians.entries()) {
    const row = rows.nth(index)
    await expect(row).toContainText(technician.displayName)
    await expect(row).toContainText(tradeLabels[technician.trade])
    await expect(row).toContainText(shiftLabelOf(technician))
  }
}

function crossingMidnight(job: DashboardUnassignedJob): DashboardUnassignedJob {
  const day = new Date(Date.now() + 2 * 86_400_000).toISOString().slice(0, 10)
  const next = new Date(Date.parse(`${day}T00:00:00Z`) + 86_400_000).toISOString().slice(0, 10)
  return { ...job, scheduledStart: `${day}T22:00:00Z`, scheduledEnd: `${next}T02:00:00Z` }
}

async function scheduleOverflow(row: Locator): Promise<number> {
  return row.evaluate((element) => {
    const column = element.querySelector('.needs-technician__text')
    const schedule = element.querySelector('.needs-technician__schedule')
    if (column === null || schedule === null) {
      return Number.NaN
    }
    const range = document.createRange()
    range.selectNodeContents(schedule)
    const right = Math.max(...[...range.getClientRects()].map((rect) => rect.right))
    return right - column.getBoundingClientRect().right
  })
}

async function capture(page: Page, testInfo: TestInfo, name: string): Promise<void> {
  const path = testInfo.outputPath(`${name}.png`)
  await page.screenshot({ path, fullPage: true })
  await testInfo.attach(name, { path, contentType: 'image/png' })
}

test.describe('E2E-04 dashboard from the API (W6-AC1)', () => {
  test.use({
    expectedConsole: anyOf(
      consolePatterns.dashboardServerError,
      consolePatterns.techniciansServerError,
    ),
  })

  test('the dashboard shows the hero, six status tiles, Needs a technician, Live activity and the workload from its two API calls, each part with its loading, empty and error form and one Try again for the summary', async ({
    adminPage: page,
    adminSession,
    runId,
  }, testInfo) => {
    const parts = partsOf(page, adminSession.user.displayName)
    const summaryAnswer = page.waitForResponse((response) => isDashboardSummary(response.request()))
    const techniciansAnswer = page.waitForResponse((response) =>
      isTechniciansRead(response.request()),
    )

    await page.goto('/')

    const summaryResponse = await summaryAnswer
    const summary = await readItems<DashboardSummary>(summaryResponse)
    const technicians = (await readItems<{ items: Technician[] }>(await techniciansAnswer)).items
    const requested = new URL(summaryResponse.url()).searchParams
    expect({ from: requested.get('from'), to: requested.get('to') }).toEqual(utcDayWindow())
    await expect(page).toHaveTitle(documentTitle(dashboardCopy.heading))
    await expect(parts.hero.getByRole('heading', { level: 1 })).toBeFocused()
    await expect(
      navigation(page).getByRole('link', { name: dashboardCopy.heading }),
    ).toHaveAttribute('aria-current', 'page')
    await expectReadyDashboard(parts, summary, technicians)
    await audit(page, testInfo, runId, 'dashboard 1280')
    await capture(page, testInfo, 'w6-dashboard-1280')

    await page.setViewportSize({ width: 375, height: 812 })
    await expect(parts.workloadTable).toBeHidden()
    const people = parts.workloadList.getByRole('listitem')
    await expect(people).toHaveCount(technicians.length)
    for (const [index, technician] of technicians.entries()) {
      await expect(people.nth(index)).toContainText(technician.displayName)
      await expect(people.nth(index)).toContainText(shiftLabelOf(technician))
    }
    await audit(page, testInfo, runId, 'dashboard 375')
    await capture(page, testInfo, 'w6-dashboard-375')
    await page.setViewportSize({ width: 320, height: 800 })
    const times = await brokenTimesAndDates(page.getByRole('main'))
    expect(times.checked, 'times and dates on the dashboard at 320 px').toBeGreaterThan(0)
    expect(times.broken, 'times and dates broken across lines at 320 px').toEqual([])
    await page.setViewportSize({ width: 1280, height: 800 })

    await parts.tiles.first().click()
    await expect(page).toHaveURL('/jobs?status=Unassigned')
    await expect(
      page
        .getByRole('group', { name: jobsCopy.statusFilter })
        .getByRole('button', { name: new RegExp(`^${statusLabels.Unassigned}`) }),
    ).toHaveAttribute('aria-pressed', 'true')
    await expect(page.getByRole('combobox', { name: jobsCopy.technicianFilter })).toBeEnabled()

    const heldSummary = await holdNext(page, summaryUrl)
    const heldTechnicians = await holdNext(page, techniciansUrl)
    await openDashboard(page)
    await expect(
      parts.status.getByRole('status').filter({ hasText: dashboardCopy.loadingCounts }),
    ).toBeAttached()
    await expect(parts.tiles).toHaveCount(0)
    await expect(parts.hero.getByRole('heading', { level: 1 })).toBeVisible()
    await expect(parts.hero.getByText(dashboardCopy.onShiftCaption, { exact: true })).toBeVisible()
    await expect(parts.hero.getByRole('paragraph')).toHaveCount(0)
    await expect(parts.needs.getByRole('listitem')).toHaveCount(0)
    await expect(parts.entries).toHaveCount(0)
    await expect(
      page.getByRole('status').filter({ hasText: dashboardCopy.loadingTechnicians }),
    ).toBeAttached()
    await audit(page, testInfo, runId, 'dashboard loading 1280')
    await capture(page, testInfo, 'w6-dashboard-loading-1280')
    heldSummary.release()
    heldTechnicians.release()
    await expect(parts.tiles).toHaveCount(6)
    await expect(dataRows(parts.workloadTable)).toHaveCount(technicians.length)

    await leaveForJobs(page)
    await fulfilStub(page, summaryUrl, serverError, 1)
    await fulfilStub(page, techniciansUrl, serverError, 1)
    await openDashboard(page)
    const countsFailure = parts.status
      .getByRole('status')
      .filter({ hasText: dashboardCopy.countsFailed })
    await expect(countsFailure).toContainText(dashboardCopy.failureMessage)
    await expect(countsFailure).toContainText(`Reference ${failureReference}`)
    await expect(parts.status.getByRole('button', { name: dashboardCopy.tryAgain })).toHaveCount(1)
    await expect(parts.needs.getByText(dashboardCopy.needsFailed, { exact: true })).toBeVisible()
    await expect(parts.needs.getByRole('button')).toHaveCount(0)
    await expect(
      parts.activity.getByText(dashboardCopy.activityFailed, { exact: true }),
    ).toBeVisible()
    await expect(parts.activity.getByRole('button')).toHaveCount(0)
    await expect(parts.hero.getByRole('heading', { level: 1 })).toBeVisible()
    await expect(parts.hero.getByText(dashboardCopy.onShiftCaption)).toHaveCount(0)
    const workloadSection = page.getByRole('region', { name: dashboardCopy.workload })
    const workloadFailure = workloadSection
      .getByRole('status')
      .filter({ hasText: dashboardCopy.techniciansFailed })
    await expect(workloadFailure).toContainText(dashboardCopy.failureMessage)
    await expect(
      page.getByRole('main').getByRole('button', { name: dashboardCopy.tryAgain }),
    ).toHaveCount(2)
    await audit(page, testInfo, runId, 'dashboard load failure 1280')
    await capture(page, testInfo, 'w6-dashboard-failure-1280')

    await fulfilStub(page, summaryUrl, serverError, 1)
    await parts.status.getByRole('button', { name: dashboardCopy.tryAgain }).click()
    const countsAlert = parts.status
      .getByRole('alert')
      .filter({ hasText: dashboardCopy.countsFailed })
    await expect(countsAlert.getByRole('button', { name: dashboardCopy.tryAgain })).toBeFocused()
    await countsAlert.getByRole('button', { name: dashboardCopy.tryAgain }).click()
    await expect(parts.tiles).toHaveCount(6)
    await expect(
      parts.status.getByRole('heading', { level: 2, name: dashboardCopy.jobsByStatus }),
    ).toBeFocused()
    await workloadSection.getByRole('button', { name: dashboardCopy.tryAgain }).click()
    await expect(dataRows(parts.workloadTable)).toHaveCount(technicians.length)
    await expect(
      page.getByRole('region', { name: dashboardCopy.workload, exact: true }).last(),
    ).toBeFocused()

    await leaveForJobs(page)
    await fulfilStub(page, summaryUrl, { status: 200, body: emptySummary(utcDayWindow()) }, 1)
    await fulfilStub(page, techniciansUrl, { status: 200, body: { items: [] } }, 1)
    await openDashboard(page)
    await expect(parts.hero.getByText('No open jobs.', { exact: true })).toBeVisible()
    await expect(parts.tiles).toHaveCount(6)
    for (const [index, tile] of expectedTiles(emptySummary(utcDayWindow())).entries()) {
      await expect(parts.tiles.nth(index)).toHaveAccessibleName(
        dashboardCopy.tileName(tile.label, tile.value, tile.subline),
      )
    }
    await expect(
      parts.needs.getByRole('heading', { level: 3, name: dashboardCopy.needsEmpty }),
    ).toBeVisible()
    await expect(parts.needs.getByRole('link')).toHaveCount(0)
    await expect(
      parts.activity.getByRole('heading', { level: 3, name: dashboardCopy.activityEmpty }),
    ).toBeVisible()
    await expect(
      page.getByRole('heading', { level: 3, name: dashboardCopy.workloadEmpty }),
    ).toBeVisible()
    await audit(page, testInfo, runId, 'dashboard empty 1280')
    await capture(page, testInfo, 'w6-dashboard-empty-1280')

    await leaveForJobs(page)
    await page.route(
      summaryUrl,
      async (route) => {
        const response = await route.fetch()
        const body = (await response.json()) as DashboardSummary
        const [first, ...rest] = body.needsTechnician
        await route.fulfill({
          response,
          json:
            first === undefined
              ? body
              : { ...body, needsTechnician: [crossingMidnight(first), ...rest] },
        })
      },
      { times: 1 },
    )
    await page.setViewportSize({ width: 320, height: 800 })
    await openDashboard(page)
    const firstNeed = parts.needs.getByRole('listitem').first()
    await expect(firstNeed).toBeVisible()
    const overflow = await scheduleOverflow(firstNeed)
    expect(
      overflow,
      'the cross-midnight schedule ends inside its column at 320 px (D6-06)',
    ).toBeLessThanOrEqual(0.5)
    await capture(page, testInfo, 'w6-dashboard-cross-midnight-320')
    await page.setViewportSize({ width: 1280, height: 800 })
  })
})

test.describe('E2E-04 live dashboard (W6-AC2)', () => {
  test('an event adds a New entry for 12 seconds and updates the tiles without a reload, and a reconnect refetches the summary and the technicians and marks nothing', async ({
    adminPage: page,
    adminSession,
    api,
    guards,
    technician,
    runId,
    sentinelSink,
  }, testInfo) => {
    const lenaApi = await technician(demoTechnicians.assignments.email)
    const lena = lenaApi.user
    const note = holdNoteFor(runId)
    sentinelSink('hold note', note)
    const job = await createAssignedJob(api, runId, 'dashboard live', lena.id)
    const jobNumber = `JOB-${job.number}`
    const parts = partsOf(page, adminSession.user.displayName)
    const gate = new SocketGate()
    await gate.install(page, guards)
    const summaries = trackRequests(page, isDashboardSummary)
    const technicianReads = trackRequests(page, isTechniciansRead)
    const firstSummary = page.waitForResponse((response) => isDashboardSummary(response.request()))

    await page.goto('/')

    const initial = await readItems<DashboardSummary>(await firstSummary)
    await expect(pill(page)).toHaveText(liveCopy.pillLive, { timeout: 10_000 })
    await expect(parts.tiles).toHaveCount(6)
    const jobEntry = (sentence: string): Locator =>
      parts.entries.filter({
        has: page.getByRole('link', { name: jobNumber, exact: true }),
        hasText: sentence,
      })
    await expect(
      jobEntry(`${adminSession.user.displayName} assigned ${jobNumber} to ${lena.displayName}`),
    ).toHaveCount(1)
    await page.evaluate(
      ([name, value]) => {
        ;(window as unknown as Record<string, string>)[name] = value
      },
      [markerName, runId] as const,
    )
    const summariesBefore = summaries.length
    const techniciansBefore = technicianReads.length

    const started = await lenaApi.start(job.id)
    expect(started.status).toBe('InProgress')

    await page.waitForFunction(newTagShown, true, { polling: 'raf', timeout: 5_000 })
    const newSeenAt = Date.now()
    const startedEntry = jobEntry(`${lena.displayName} started ${jobNumber}`)
    await expect(startedEntry).toBeVisible()
    const newTag = startedEntry.getByText(dashboardCopy.newTag, { exact: true })
    await expect(newTag).toBeVisible()
    await expect(parts.entries.first()).toContainText(`${lena.displayName} started ${jobNumber}`)
    await expect(parts.tiles.nth(2)).toHaveAccessibleName(
      tileName('In progress', initial.openJobs.inProgress + 1),
    )
    await expect(parts.tiles.nth(1)).toHaveAccessibleName(
      tileName('Assigned', initial.openJobs.assigned - 1),
    )
    await expect
      .poll(() => summaries.length, { message: 'summary requests after the event' })
      .toBeGreaterThan(summariesBefore)
    await expect
      .poll(() => technicianReads.length, { message: 'technician requests after the event' })
      .toBeGreaterThan(techniciansBefore)
    expect(
      await page.evaluate(
        (name) => (window as unknown as Record<string, string | undefined>)[name],
        markerName,
      ),
      'no reload after the event',
    ).toBe(runId)
    await audit(page, testInfo, runId, 'dashboard with a New entry 1280')
    await capture(page, testInfo, 'w6-dashboard-new-entry-1280')

    expect(await gate.drop()).toBe(1)
    await expect(pill(page)).toHaveText(liveCopy.pillReconnecting, { timeout: 2_000 })
    const held = await lenaApi.hold(job.id, note)
    expect(held.status).toBe('OnHold')
    const restoredAt = Date.now()
    const summariesAtRestore = summaries.length
    const techniciansAtRestore = technicianReads.length
    gate.allow()

    await expect(pill(page)).toHaveText(liveCopy.pillLive, { timeout: 12_000 })
    const heldEntry = jobEntry(`${lena.displayName} put ${jobNumber} on hold`)
    await expect(heldEntry).toBeVisible()
    await expect(heldEntry.getByText(dashboardCopy.newTag, { exact: true })).toHaveCount(0)
    await expect(parts.tiles.nth(3)).toHaveAccessibleName(
      tileName('On hold', initial.openJobs.onHold + 1),
    )
    await expect(parts.tiles.nth(2)).toHaveAccessibleName(
      tileName('In progress', initial.openJobs.inProgress),
    )
    await expect
      .poll(() => summaries.length, { message: 'summary requests after the reconnect' })
      .toBeGreaterThan(summariesAtRestore)
    await expect
      .poll(() => technicianReads.length, { message: 'technician requests after the reconnect' })
      .toBeGreaterThan(techniciansAtRestore)
    expect(
      await page.evaluate(
        (name) => (window as unknown as Record<string, string | undefined>)[name],
        markerName,
      ),
      'no reload after the reconnect',
    ).toBe(runId)
    testInfo.annotations.push({
      type: 'reconnect',
      description: `Live again ${Date.now() - restoredAt} ms after the gate opened`,
    })

    await page.waitForFunction(newTagShown, false, {
      polling: 'raf',
      timeout: newEntryMilliseconds + 3_000,
    })
    const shownFor = Date.now() - newSeenAt
    await expect(newTag).toHaveCount(0)
    testInfo.annotations.push({ type: 'New entry', description: `shown for ${shownFor} ms` })
    expect(shownFor, 'the New tag shows for 12 seconds').toBeGreaterThanOrEqual(
      newEntryMilliseconds - 300,
    )
    expect(shownFor, 'the New tag shows for 12 seconds').toBeLessThanOrEqual(
      newEntryMilliseconds + 1_000,
    )
    await expect(startedEntry).toBeVisible()
  })
})
