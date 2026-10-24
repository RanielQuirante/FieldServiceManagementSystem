import type { Locator, Page, TestInfo } from '@playwright/test'

import { consolePatterns, expect, fulfilStub, problemBody, test } from './fixtures.ts'
import { demoTechnicians, type Technician } from './support/api.ts'
import { audit } from './support/axe.ts'
import {
  dashboardCopy,
  documentTitle,
  notFoundCopy,
  shellCopy,
  techniciansCopy,
  tradeLabels,
} from './support/copy.ts'
import {
  holdNext,
  isOnShift,
  isTechniciansRead,
  overflowsOfRows,
  shiftLabelOf,
  techniciansUrl,
} from './support/dashboard.ts'
import { openInApp } from './support/run.ts'

const serverError = {
  status: 500,
  body: problemBody(500, 'server.error', 'An unexpected error occurred.'),
} as const

const failureReference = (serverError.body as { correlationId: string }).correlationId

const phoneQuery = '014-78'

const phoneMatch = 'Aisha Karim'

function navigation(page: Page): Locator {
  return page.getByRole('navigation', { name: shellCopy.mainNavigation })
}

function chips(page: Page): Locator {
  return page.getByRole('group', { name: techniciansCopy.shiftFilter })
}

function chip(page: Page, label: string, count: number | null): Locator {
  return chips(page).getByRole('button', {
    name: count === null ? label : `${label}, ${count}`,
    exact: true,
  })
}

function table(page: Page): Locator {
  return page.getByRole('region', { name: techniciansCopy.table, exact: true })
}

function tableRows(page: Page): Locator {
  return table(page)
    .getByRole('row')
    .filter({ has: page.getByRole('cell') })
}

function people(page: Page): Locator {
  return page.getByRole('list', { name: techniciansCopy.table, exact: true }).getByRole('listitem')
}

function search(page: Page): Locator {
  return page.getByRole('searchbox', { name: techniciansCopy.search })
}

function announcement(page: Page): Locator {
  return page
    .getByRole('main')
    .getByRole('status')
    .filter({ hasText: /^(?:Showing \d+ of \d+ technicians|No technicians match these filters)$/ })
}

interface Counts {
  readonly all: number
  readonly onShift: number
  readonly offShift: number
}

function countsOf(technicians: readonly Technician[]): Counts {
  const onShift = technicians.filter(isOnShift).length
  return { all: technicians.length, onShift, offShift: technicians.length - onShift }
}

async function expectCounts(page: Page, counts: Counts): Promise<void> {
  await expect(chip(page, techniciansCopy.all, counts.all)).toBeVisible()
  await expect(chip(page, techniciansCopy.onShift, counts.onShift)).toBeVisible()
  await expect(chip(page, techniciansCopy.offShift, counts.offShift)).toBeVisible()
}

async function expectRows(page: Page, names: readonly string[]): Promise<void> {
  await expect(tableRows(page)).toHaveCount(names.length)
  for (const [index, name] of names.entries()) {
    await expect(tableRows(page).nth(index)).toContainText(name)
  }
}

async function expectTechnicianRow(row: Locator, technician: Technician): Promise<void> {
  await expect(row).toContainText(technician.email)
  await expect(row).toContainText(tradeLabels[technician.trade])
  await expect(row).toContainText(shiftLabelOf(technician))
  await expect(row).toContainText(technician.phoneNumber)
  const job = technician.currentJob ?? technician.nextJob
  if (job !== null) {
    await expect(row.getByRole('link', { name: `JOB-${job.number}` })).toHaveAttribute(
      'href',
      `/jobs/${job.id}`,
    )
  }
}

async function leaveForNotFound(page: Page): Promise<void> {
  await openInApp(page, '/no-such-page')
  await expect(page.getByRole('heading', { level: 1, name: notFoundCopy.heading })).toBeVisible()
}

async function openTechnicians(page: Page): Promise<void> {
  await navigation(page).getByRole('link', { name: techniciansCopy.heading }).click()
  await expect(page).toHaveURL('/technicians')
}

async function capture(page: Page, testInfo: TestInfo, name: string): Promise<void> {
  const path = testInfo.outputPath(`${name}.png`)
  await page.screenshot({ path, fullPage: true })
  await testInfo.attach(name, { path, contentType: 'image/png' })
}

test.describe('E2E-05 technicians list (W6-AC3)', () => {
  test.use({ expectedConsole: consolePatterns.techniciansServerError })

  test('/technicians lists every technician with trade, shift, open jobs, the current or next job and phone, filters by shift and searches with unchanged counts and the filters in the address, shows people lists on narrow widths, and has its loading, empty, failure and Inactive forms', async ({
    adminPage: page,
    runId,
  }, testInfo) => {
    const answer = page.waitForResponse((response) => isTechniciansRead(response.request()))

    await page.goto('/technicians')

    const technicians = ((await (await answer).json()) as { items: Technician[] }).items
    const counts = countsOf(technicians)
    const active = technicians.filter((technician) => technician.isActive)
    const names = technicians.map((technician) => technician.displayName)
    const offShift = technicians.filter((technician) => !isOnShift(technician))
    await expect(
      page.getByRole('heading', { level: 1, name: techniciansCopy.heading }),
    ).toBeVisible()
    await expect(page).toHaveTitle(documentTitle(techniciansCopy.heading))
    await expect(
      navigation(page).getByRole('link', { name: techniciansCopy.heading }),
    ).toHaveAttribute('aria-current', 'page')
    await expect(
      page
        .getByRole('main')
        .getByText(techniciansCopy.subline(active.filter(isOnShift).length, active.length), {
          exact: true,
        }),
    ).toBeVisible()
    await expectCounts(page, counts)
    await expect(chip(page, techniciansCopy.all, counts.all)).toHaveAttribute(
      'aria-pressed',
      'true',
    )
    await expectRows(page, names)
    for (const [index, technician] of technicians.entries()) {
      await expectTechnicianRow(tableRows(page).nth(index), technician)
    }
    await audit(page, testInfo, runId, 'technicians 1280')
    await capture(page, testInfo, 'w6-technicians-1280')

    await chip(page, techniciansCopy.offShift, counts.offShift).click()
    await expect(page).toHaveURL('/technicians?shift=off-shift')
    await expectRows(
      page,
      offShift.map((technician) => technician.displayName),
    )
    testInfo.annotations.push({
      type: 'off shift',
      description: offShift.map((technician) => technician.displayName).join(', '),
    })
    await expectCounts(page, counts)
    await expect(announcement(page)).toHaveText(
      techniciansCopy.showing(offShift.length, technicians.length),
    )
    await audit(page, testInfo, runId, 'technicians off shift 1280')
    await chip(page, techniciansCopy.onShift, counts.onShift).click()
    await expect(page).toHaveURL('/technicians?shift=on-shift')
    await expectRows(
      page,
      technicians.filter(isOnShift).map((technician) => technician.displayName),
    )

    await chip(page, techniciansCopy.all, counts.all).click()
    await expect(page).toHaveURL('/technicians')
    await search(page).fill(phoneQuery)
    await expectRows(page, [phoneMatch])
    await expect(page).toHaveURL(`/technicians?search=${phoneQuery}`)
    await expectCounts(page, counts)
    await expect(announcement(page)).toHaveText(techniciansCopy.showing(1, technicians.length))
    await search(page).fill('plumb')
    await expectRows(
      page,
      technicians
        .filter((technician) => technician.trade === 'Plumbing')
        .map((technician) => technician.displayName),
    )
    await search(page).fill(`no match ${runId}`)
    await expect(
      page.getByRole('heading', { name: techniciansCopy.noMatchesTitle }).first(),
    ).toBeVisible()
    await expectCounts(page, counts)
    await audit(page, testInfo, runId, 'technicians no matches 1280')
    await page.getByRole('button', { name: techniciansCopy.clearFilters }).click()
    await expect(page).toHaveURL('/technicians')
    await expect(search(page)).toBeFocused()
    await expect(search(page)).toHaveValue('')
    await expectRows(page, names)

    await openInApp(page, '/technicians?shift=on-shift&search=park')
    await expect(chip(page, techniciansCopy.onShift, counts.onShift)).toHaveAttribute(
      'aria-pressed',
      'true',
    )
    await expect(search(page)).toHaveValue('park')
    await expectRows(page, [demoTechnicians.assignments.name])
    await openInApp(page, '/technicians')
    await expectRows(page, names)

    for (const viewport of [
      { width: 768, height: 1024 },
      { width: 375, height: 812 },
    ]) {
      await page.setViewportSize(viewport)
      await expect(table(page)).toBeHidden()
      await expect(people(page)).toHaveCount(technicians.length)
      for (const [index, technician] of technicians.entries()) {
        const item = people(page).nth(index)
        await expect(item).toContainText(technician.displayName)
        await expect(item).toContainText(shiftLabelOf(technician))
        await expect(
          item.getByRole('link', {
            name: techniciansCopy.call(technician.displayName, technician.phoneNumber),
          }),
        ).toHaveAttribute('href', /^tel:\+?\d+$/)
      }
      await audit(page, testInfo, runId, `technicians people list ${viewport.width}`)
      await capture(page, testInfo, `w6-technicians-${viewport.width}`)
    }
    await page.setViewportSize({ width: 320, height: 800 })
    const emails = await overflowsOfRows(people(page), '.technicians-table__email')
    expect(emails.checked, 'people-list emails measured at 320 px').toBe(technicians.length)
    expect(emails.overflows, 'people-list emails past their row at 320 px (D6-04)').toEqual([])
    await capture(page, testInfo, 'w6-technicians-320')
    await page.setViewportSize({ width: 1280, height: 800 })

    await page.route(techniciansUrl, async (route) => {
      const response = await route.fetch()
      const body = (await response.json()) as { items: Technician[] }
      await route.fulfill({
        response,
        json: {
          items: body.items.map((technician) =>
            technician.displayName === demoTechnicians.offShift.name
              ? { ...technician, isActive: false, availableForAssignment: false }
              : technician,
          ),
        },
      })
    })
    await navigation(page).getByRole('link', { name: dashboardCopy.heading }).click()
    const workloadRow = page
      .getByRole('table', { name: dashboardCopy.workload })
      .getByRole('row')
      .filter({ hasText: demoTechnicians.offShift.name })
    await expect(workloadRow).toContainText(techniciansCopy.inactive)
    await openTechnicians(page)
    const inactiveRow = tableRows(page).filter({ hasText: demoTechnicians.offShift.name })
    await expect(inactiveRow).toContainText(techniciansCopy.inactive)
    await expect(inactiveRow).not.toContainText(techniciansCopy.offShift)
    const activeAfter = active.filter(
      (technician) => technician.displayName !== demoTechnicians.offShift.name,
    )
    await expect(
      page
        .getByRole('main')
        .getByText(
          techniciansCopy.subline(activeAfter.filter(isOnShift).length, activeAfter.length),
          { exact: true },
        ),
    ).toBeVisible()
    await expectCounts(page, counts)
    await chip(page, techniciansCopy.offShift, counts.offShift).click()
    await expect(tableRows(page).filter({ hasText: demoTechnicians.offShift.name })).toContainText(
      techniciansCopy.inactive,
    )
    await page.setViewportSize({ width: 375, height: 812 })
    await expect(people(page).filter({ hasText: demoTechnicians.offShift.name })).toContainText(
      techniciansCopy.inactive,
    )
    await audit(page, testInfo, runId, 'technicians with an inactive technician 375')
    await page.setViewportSize({ width: 1280, height: 800 })
    await page.unroute(techniciansUrl)

    await leaveForNotFound(page)
    const held = await holdNext(page, techniciansUrl)
    await openTechnicians(page)
    await expect(
      page.getByRole('main').getByRole('status').filter({ hasText: techniciansCopy.loading }),
    ).toBeAttached()
    await expect(chip(page, techniciansCopy.all, null)).toBeDisabled()
    await expect(chip(page, techniciansCopy.offShift, null)).toBeDisabled()
    await expect(search(page)).toBeDisabled()
    await audit(page, testInfo, runId, 'technicians loading 1280')
    await capture(page, testInfo, 'w6-technicians-loading-1280')
    held.release()
    await expectRows(page, names)

    await leaveForNotFound(page)
    await fulfilStub(page, techniciansUrl, serverError, 1)
    await openTechnicians(page)
    const failure = page
      .getByRole('main')
      .getByRole('status')
      .filter({ hasText: techniciansCopy.loadFailed })
    await expect(failure).toContainText(`Reference ${failureReference}`)
    await expect(chip(page, techniciansCopy.all, null)).toBeDisabled()
    await expect(search(page)).toBeDisabled()
    await expect(page.getByRole('main').getByText(/ on shift$/)).toHaveCount(0)
    await audit(page, testInfo, runId, 'technicians load failure 1280')
    await capture(page, testInfo, 'w6-technicians-failure-1280')
    await failure.getByRole('button', { name: techniciansCopy.tryAgain }).click()
    await expectRows(page, names)
    await expect(table(page)).toBeFocused()

    await leaveForNotFound(page)
    await fulfilStub(page, techniciansUrl, { status: 200, body: { items: [] } }, 1)
    await openTechnicians(page)
    await expect(
      page.getByRole('heading', { name: techniciansCopy.noTechniciansTitle }).first(),
    ).toBeVisible()
    await expectCounts(page, { all: 0, onShift: 0, offShift: 0 })
    await audit(page, testInfo, runId, 'technicians empty 1280')
  })
})
