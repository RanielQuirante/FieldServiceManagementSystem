import { randomUUID } from 'node:crypto'

import type { Page, Request } from '@playwright/test'

import { consolePatterns, expect, test } from './fixtures.ts'
import type { JobStatus } from './support/api.ts'
import {
  createJobCopy,
  documentTitle,
  jobDetailCopy,
  jobsCopy,
  notFoundCopy,
  shellCopy,
  statusLabels,
} from './support/copy.ts'
import { e2eTitle, openInApp } from './support/run.ts'

const createExample = {
  title: 'Exterior signage lights out',
  description:
    'Both pylon signs at the north entrance are dark after 6 PM. Timer and photocell suspected.',
  equipment: 'RTU-2, 10-ton packaged rooftop unit',
  customer: 'Riverside Shopping Center',
  siteAddress: '500 Riverside Boulevard',
  siteAccessNotes: 'Meet security at the north office',
  contactName: 'Grace Liu',
  contactPhone: '(555) 014-8812',
  template: 'General electrical inspection',
} as const

const statuses: readonly JobStatus[] = [
  'Unassigned',
  'Assigned',
  'InProgress',
  'OnHold',
  'Completed',
  'Cancelled',
]

function tomorrowInUtc(): string {
  return new Date(Date.now() + 86_400_000).toISOString().slice(0, 10)
}

function field(page: Page, name: string, exact = true) {
  return page.getByRole('textbox', { name, exact })
}

async function fillCreateForm(page: Page, title: string, date: string): Promise<void> {
  await field(page, createJobCopy.title).fill(title)
  await field(page, createJobCopy.description).fill(createExample.description)
  await field(page, createJobCopy.equipment, false).fill(`  ${createExample.equipment}  `)
  await field(page, createJobCopy.customer).fill(createExample.customer)
  await field(page, createJobCopy.siteAccessNotes, false).fill(createExample.siteAccessNotes)
  await field(page, createJobCopy.contactName, false).fill(createExample.contactName)
  await field(page, createJobCopy.contactPhone, false).fill(createExample.contactPhone)
  await page.getByLabel(createJobCopy.date, { exact: true }).fill(date)
  await page.getByLabel(createJobCopy.start, { exact: true }).fill('09:00')
  await page.getByLabel(createJobCopy.end, { exact: true }).fill('11:00')
  await page
    .getByRole('combobox', { name: createJobCopy.checklistTemplate })
    .selectOption({ label: createExample.template })
}

function isCreateJob(request: Request): boolean {
  return request.method() === 'POST' && new URL(request.url()).pathname === '/api/jobs'
}

function trackCreateRequests(page: Page): Request[] {
  const requests: Request[] = []
  page.on('request', (request) => {
    if (isCreateJob(request)) {
      requests.push(request)
    }
  })
  return requests
}

async function sendNextCreateWithoutSiteAndWithEndAtStart(page: Page): Promise<void> {
  await page.route(
    '**/api/jobs',
    async (route) => {
      const body = route.request().postDataJSON() as Record<string, unknown>
      delete body.siteAddress
      body.scheduledEnd = body.scheduledStart
      await route.continue({ postData: JSON.stringify(body) })
    },
    { times: 1 },
  )
}

function chipName(label: string, count: number | undefined): RegExp {
  return new RegExp(`^${label} ?, ${String(count)}$`)
}

function summaryOf(page: Page) {
  return page.getByRole('paragraph').filter({ hasText: /^Showing \d/ })
}

function rowsOf(page: Page) {
  return page.getByRole('region', { name: jobsCopy.table }).locator('tbody tr')
}

test.describe('E2E-06 create a job', () => {
  test.use({ expectedConsole: consolePatterns.createJobRejected })

  test('the form shows field errors, creates the job with its equipment, and the job survives a reload and is listed', async ({
    adminPage: page,
    api,
    runId,
  }) => {
    const title = e2eTitle(runId, createExample.title)
    const date = tomorrowInUtc()
    const createRequests = trackCreateRequests(page)
    await page.goto('/jobs')
    await page.getByRole('link', { name: shellCopy.newJob }).click()
    await expect(page).toHaveURL('/jobs/new')
    await expect(page.getByRole('heading', { level: 1, name: createJobCopy.heading })).toBeVisible()
    await expect(
      page.getByRole('combobox', { name: createJobCopy.checklistTemplate }),
    ).toBeEnabled()
    const equipment = field(page, createJobCopy.equipment, false)
    await expect(equipment).toHaveAccessibleName(`${createJobCopy.equipment} Optional`)
    await expect(equipment).toHaveAccessibleDescription(createJobCopy.equipmentHelp)
    await expect(equipment).toHaveAttribute('maxlength', '200')
    await expect(equipment).toHaveAttribute('placeholder', createJobCopy.equipmentPlaceholder)
    await expect(equipment).toHaveAttribute('autocomplete', 'off')
    await fillCreateForm(page, title, date)

    await page.getByRole('button', { name: createJobCopy.create }).click()

    const siteAddress = field(page, createJobCopy.siteAddress)
    await expect(siteAddress).toHaveAttribute('aria-invalid', 'true')
    await expect(siteAddress).toHaveAccessibleDescription(createJobCopy.siteAddressRequired)
    await expect(siteAddress).toBeFocused()
    expect(createRequests).toHaveLength(0)

    await siteAddress.fill(createExample.siteAddress)
    await sendNextCreateWithoutSiteAndWithEndAtStart(page)
    const rejected = page.waitForResponse((response) => isCreateJob(response.request()))
    await page.getByRole('button', { name: createJobCopy.create }).click()

    expect((await rejected).status()).toBe(400)
    const end = page.getByLabel(createJobCopy.end, { exact: true })
    await expect(siteAddress).toHaveAccessibleDescription(createJobCopy.serverSiteAddressRequired)
    await expect(end).toHaveAccessibleDescription(createJobCopy.serverEndAfterStart)
    await expect(siteAddress).toBeFocused()
    const summary = page.getByRole('alert').filter({ hasText: createJobCopy.summaryTitle })
    await expect(summary).toContainText(createJobCopy.summaryForSiteAndEnd)

    const created = page.waitForResponse((response) => isCreateJob(response.request()))
    await page.getByRole('button', { name: createJobCopy.create }).click()

    const response = await created
    expect(response.status()).toBe(201)
    const job = (await response.json()) as { id: string; number: number }
    expect(response.headers()['location']).toBe(`/api/jobs/${job.id}`)
    await expect(page).toHaveURL(`/jobs/${job.id}`)
    await expect(page.getByRole('heading', { level: 1, name: title })).toBeVisible()
    await expect(page).toHaveTitle(documentTitle(`JOB-${job.number}`))
    const stored = await api.getJob(job.id)
    expect(stored).toMatchObject({
      number: job.number,
      title,
      status: 'Unassigned',
      assignedTechnician: null,
      equipment: createExample.equipment,
      siteAddress: createExample.siteAddress,
      contactName: createExample.contactName,
      contactPhone: createExample.contactPhone,
      checklistTemplate: { name: createExample.template },
    })
    expect(Date.parse(stored.scheduledStart)).toBe(Date.parse(`${date}T09:00:00Z`))
    expect(Date.parse(stored.scheduledEnd)).toBe(Date.parse(`${date}T11:00:00Z`))
    expect(stored.history.map((entry) => entry.action)).toEqual(['Created'])
    expect(job.number).toBeGreaterThan(1055)
    const details = page.getByRole('region', { name: jobDetailCopy.detailsCard })
    await expect(details).toContainText(createExample.equipment)
    await expect(details).toContainText(jobsCopy.unassigned)
    await expect(page.getByText(`JOB-${job.number}`, { exact: true }).first()).toBeVisible()
    await expect(page.getByRole('region', { name: jobDetailCopy.history })).toContainText(
      `Created by ${stored.createdBy.displayName}`,
    )

    await page.reload()

    await expect(page.getByRole('heading', { level: 1, name: title })).toBeVisible()
    await expect(page.getByRole('region', { name: jobDetailCopy.detailsCard })).toContainText(
      createExample.equipment,
    )
    await page
      .getByRole('navigation', { name: shellCopy.breadcrumb })
      .getByRole('link', { name: jobsCopy.heading })
      .click()
    const runJobs = await api.getJobs({ search: `E2E ${runId}`, pageSize: 12 })
    await page.getByRole('searchbox', { name: jobsCopy.search }).fill(`E2E ${runId}`)
    await page.getByRole('searchbox', { name: jobsCopy.search }).press('Enter')
    await expect(page).toHaveURL(`/jobs?search=${encodeURIComponent(`E2E ${runId}`)}`)
    await expect(rowsOf(page)).toHaveCount(runJobs.items.length)
    const createdRow = rowsOf(page).filter({
      has: page.getByRole('link', { name: title, exact: true }),
    })
    await expect(createdRow).toHaveCount(1)
    await expect(createdRow).toContainText(`JOB-${job.number}`)
    await expect(createdRow).toContainText(jobsCopy.unassigned)
  })
})

test.describe('W1 job list', () => {
  test('the list shows the API facet counts and filters by status, priority and search, twelve rows per page', async ({
    adminPage: page,
    api,
    runId,
  }) => {
    const all = await api.getJobs({ pageSize: 12 })
    const onHold = await api.getJobs({ status: 'OnHold', pageSize: 12 })
    const urgent = await api.getJobs({ priority: 'Urgent', pageSize: 12 })
    const walkIn = await api.findJobByNumber(1047)
    const counts = new Map(all.statusCounts.map((entry) => [entry.status, entry.count]))
    const total = all.statusCounts.reduce((sum, entry) => sum + entry.count, 0)
    const chips = page.getByRole('group', { name: jobsCopy.statusFilter })

    await page.goto('/jobs')

    await expect(page.getByRole('heading', { level: 1, name: jobsCopy.heading })).toBeVisible()
    await expect(
      chips.getByRole('button', { name: chipName(jobsCopy.allStatuses, total) }),
    ).toHaveAttribute('aria-pressed', 'true')
    for (const status of statuses) {
      await expect(
        chips.getByRole('button', { name: chipName(statusLabels[status], counts.get(status)) }),
      ).toHaveAttribute('aria-pressed', 'false')
    }
    await expect(rowsOf(page)).toHaveCount(12)
    await expect(summaryOf(page)).toHaveText(`Showing 1–12 of ${all.totalCount} jobs`)
    await expect(page.getByRole('status').filter({ hasText: /^Showing/ })).toHaveText(
      `Showing 1–12 of ${all.totalCount} jobs`,
    )
    await expect(page.getByRole('button', { name: jobsCopy.previous })).toBeDisabled()

    await page.getByRole('button', { name: jobsCopy.next }).click()

    await expect(page).toHaveURL('/jobs?page=2')
    await expect(summaryOf(page)).toHaveText(
      `Showing 13–${Math.min(24, all.totalCount)} of ${all.totalCount} jobs`,
    )
    await page.getByRole('button', { name: jobsCopy.previous }).click()
    await expect(page).toHaveURL('/jobs')
    await expect(summaryOf(page)).toHaveText(`Showing 1–12 of ${all.totalCount} jobs`)

    await chips
      .getByRole('button', { name: chipName(statusLabels.OnHold, counts.get('OnHold')) })
      .click()

    await expect(page).toHaveURL('/jobs?status=OnHold')
    await expect(rowsOf(page)).toHaveCount(onHold.items.length)
    await expect(rowsOf(page).getByText(statusLabels.OnHold, { exact: true })).toHaveCount(
      onHold.items.length,
    )
    await chips.getByRole('button', { name: chipName(jobsCopy.allStatuses, total) }).click()
    await expect(page).toHaveURL('/jobs')

    await page.getByRole('combobox', { name: jobsCopy.priority }).selectOption('Urgent')

    await expect(page).toHaveURL('/jobs?priority=Urgent')
    await expect(summaryOf(page)).toContainText(`of ${urgent.totalCount} job`)
    await expect(rowsOf(page)).toHaveCount(urgent.items.length)
    await page.getByRole('combobox', { name: jobsCopy.priority }).selectOption('')
    await expect(page).toHaveURL('/jobs')

    const search = page.getByRole('searchbox', { name: jobsCopy.search })
    await search.fill(`JOB-${walkIn.number}`)
    await search.press('Enter')

    await expect(rowsOf(page)).toHaveCount(1)
    await expect(rowsOf(page).getByRole('link', { name: walkIn.title })).toBeVisible()
    await search.fill(`no match ${runId}`)
    await search.press('Enter')
    await expect(page.getByRole('heading', { name: jobsCopy.noMatchesTitle })).toBeVisible()
    await page.getByRole('button', { name: jobsCopy.clearFilters }).click()
    await expect(page).toHaveURL('/jobs')
    await expect(search).toBeFocused()
    await expect(rowsOf(page)).toHaveCount(12)
  })

  test('a seeded job opens with its details, assignee and history from the API', async ({
    adminPage: page,
    api,
  }) => {
    const summary = await api.findJobByNumber(1042)
    const job = await api.getJob(summary.id)

    await page.goto(`/jobs/${job.id}`)

    await expect(page.getByRole('heading', { level: 1, name: job.title })).toBeVisible()
    await expect(page).toHaveTitle(documentTitle('JOB-1042'))
    const details = page.getByRole('region', { name: jobDetailCopy.detailsCard })
    await expect(details).toContainText(job.customerName)
    await expect(details).toContainText(job.siteAddress)
    await expect(details).toContainText(String(job.equipment))
    await expect(details).toContainText(String(job.contactName))
    await expect(details).toContainText(String(job.contactPhone))
    await expect(details).toContainText(String(job.assignedTechnician?.displayName))
    await expect(details).toContainText(job.checklistTemplate.name)
    await expect(
      page.getByRole('main').getByText(statusLabels[job.status], { exact: true }).first(),
    ).toBeVisible()
    await expect(
      page.getByRole('region', { name: jobDetailCopy.history }).getByRole('listitem'),
    ).toHaveCount(job.history.length)
    await expect(
      page.getByRole('navigation', { name: shellCopy.breadcrumb }).getByText('JOB-1042'),
    ).toHaveAttribute('aria-current', 'page')
  })
})

test.describe('E2E-18 deep links and not found (W1 part)', () => {
  test.use({ expectedConsole: consolePatterns.jobNotFound })

  test('an unknown job id shows the job-not-found state and an unknown path shows the not-found page', async ({
    adminPage: page,
  }) => {
    await page.goto(`/jobs/${randomUUID()}`)

    await expect(
      page.getByRole('heading', { level: 1, name: jobDetailCopy.notFoundHeading }),
    ).toBeVisible()
    await expect(
      page.getByRole('heading', { level: 2, name: jobDetailCopy.notFoundTitle }),
    ).toBeVisible()
    await expect(page).toHaveTitle(documentTitle(jobDetailCopy.notFoundHeading))
    await expect(page.getByRole('link', { name: jobDetailCopy.backToJobs })).toBeVisible()

    await openInApp(page, '/no-such-page')

    await expect(page.getByRole('heading', { level: 1, name: notFoundCopy.heading })).toBeVisible()
    await expect(page.getByRole('heading', { level: 2, name: notFoundCopy.title })).toBeVisible()
    await expect(page).toHaveTitle(documentTitle(notFoundCopy.heading))
    await page.getByRole('link', { name: notFoundCopy.goToJobs }).click()
    await expect(page).toHaveURL('/jobs')
    await expect(page.getByRole('heading', { level: 1, name: jobsCopy.heading })).toBeVisible()
  })
})
