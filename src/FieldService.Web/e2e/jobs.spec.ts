import { randomUUID } from 'node:crypto'

import type { Locator, Page, Request } from '@playwright/test'

import { anyOf, consolePatterns, expect, fulfilStub, problemBody, test } from './fixtures.ts'
import {
  demoTechnicians,
  readProblem,
  type AdminApi,
  type JobDetails,
  type JobStatus,
  type JobSummary,
  type Technician,
} from './support/api.ts'
import {
  assignCopy,
  cancelCopy,
  createJobCopy,
  dashboardCopy,
  documentTitle,
  historyCopy,
  jobDetailCopy,
  jobsCopy,
  notFoundCopy,
  shellCopy,
  statusLabels,
  tradeLabels,
} from './support/copy.ts'
import {
  assignRowName,
  createAssignedJob,
  createUnassignedJob,
  expectedAssignOrder,
  fireSafetyJob,
  holdNoteFor,
  notAssignableStatuses,
  rowNamed,
  technicianOptionLabel,
  titleLinkPath,
  tomorrowInUtc,
} from './support/jobs.ts'
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

function isAssignment(request: Request, jobId: string): boolean {
  return (
    request.method() === 'PUT' &&
    new URL(request.url()).pathname === `/api/jobs/${jobId}/assignment`
  )
}

function isJobRead(request: Request, jobId: string): boolean {
  return request.method() === 'GET' && new URL(request.url()).pathname === `/api/jobs/${jobId}`
}

async function captureNextBody(page: Page, url: string): Promise<() => unknown> {
  let body: unknown = null
  await page.route(
    url,
    async (route) => {
      body = route.request().postDataJSON()
      await route.continue()
    },
    { times: 1 },
  )
  return () => body
}

interface HeldRequest {
  readonly body: () => unknown
  readonly release: () => void
}

async function holdNextRequest(page: Page, url: string): Promise<HeldRequest> {
  let body: unknown = null
  let release: () => void = () => undefined
  const held = new Promise<void>((resolve) => {
    release = resolve
  })
  await page.route(
    url,
    async (route) => {
      body = route.request().postDataJSON()
      await held
      await route.continue()
    },
    { times: 1 },
  )
  return { body: () => body, release: () => release() }
}

function isTechniciansRead(request: Request): boolean {
  return request.method() === 'GET' && new URL(request.url()).pathname === '/api/technicians'
}

interface RowLook {
  readonly rowBackground: string
  readonly rowShadow: string
  readonly rowOpacity: string
  readonly radioOpacity: string
  readonly radioAccent: string
  readonly radioDisabled: boolean
}

async function rowLook(radio: Locator): Promise<RowLook> {
  return radio.evaluate((element) => {
    const row = element.closest('label') ?? element
    const rowStyle = getComputedStyle(row)
    const radioStyle = getComputedStyle(element)
    return {
      rowBackground: rowStyle.backgroundColor,
      rowShadow: rowStyle.boxShadow,
      rowOpacity: rowStyle.opacity,
      radioOpacity: radioStyle.opacity,
      radioAccent: radioStyle.accentColor,
      radioDisabled: (element as HTMLInputElement).disabled,
    }
  })
}

interface Margins {
  readonly left: number
  readonly top: number
  readonly right: number
  readonly bottom: number
}

async function marginsInViewport(page: Page, locator: Locator): Promise<Margins> {
  const box = await locator.boundingBox()
  const viewport = page.viewportSize()
  if (box === null || viewport === null) {
    throw new Error('The element or the viewport has no size.')
  }
  return {
    left: box.x,
    top: box.y,
    right: viewport.width - (box.x + box.width),
    bottom: viewport.height - (box.y + box.height),
  }
}

const serverError = {
  status: 500,
  body: problemBody(500, 'server.error', 'An unexpected error occurred.'),
} as const

interface OptionGroup {
  readonly label: string
  readonly options: readonly string[]
}

async function optionGroupsOf(select: Locator): Promise<OptionGroup[]> {
  return select.evaluate((element) =>
    [...element.querySelectorAll('optgroup')].map((group) => ({
      label: group.label,
      options: [...group.querySelectorAll('option')].map((option) =>
        (option.textContent ?? '').trim(),
      ),
    })),
  )
}

function technicianNamed(technicians: readonly Technician[], displayName: string): Technician {
  const match = technicians.find((technician) => technician.displayName === displayName)
  if (match === undefined) {
    throw new Error(`GET /api/technicians has no technician named ${displayName}.`)
  }
  return match
}

async function firstJobWithStatus(api: AdminApi, status: JobStatus): Promise<JobSummary> {
  const sample = (await api.getJobs({ status, pageSize: 1 })).items[0]
  if (sample === undefined) {
    throw new Error(`GET /api/jobs has no ${status} job to open.`)
  }
  return sample
}

function historyItems(page: Page) {
  return page.getByRole('region', { name: jobDetailCopy.history }).getByRole('listitem')
}

function trackStatusRequests(page: Page): string[] {
  const requests: string[] = []
  page.on('request', (request) => {
    if (/^\/api\/jobs\/[^/]+\/status$/.test(new URL(request.url()).pathname)) {
      requests.push(request.method())
    }
  })
  return requests
}

async function expectOnlyCancelIn(page: Page): Promise<void> {
  const main = page.getByRole('main')
  await expect(main.getByRole('button'), 'buttons in main').toHaveText([cancelCopy.trigger])
  for (const role of ['link', 'textbox', 'combobox', 'radio', 'checkbox'] as const) {
    await expect(main.getByRole(role), `${role} elements in main`).toHaveCount(0)
  }
}

async function fillFireSafetyForm(page: Page, title: string, date: string): Promise<void> {
  await field(page, createJobCopy.title).fill(title)
  await field(page, createJobCopy.description).fill(fireSafetyJob.description)
  await field(page, createJobCopy.customer).fill(fireSafetyJob.customer)
  await field(page, createJobCopy.siteAddress).fill(fireSafetyJob.siteAddress)
  await page.getByLabel(createJobCopy.date, { exact: true }).fill(date)
  await page.getByLabel(createJobCopy.start, { exact: true }).fill('09:00')
  await page.getByLabel(createJobCopy.end, { exact: true }).fill('11:00')
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
  test('the list shows the API facet counts and filters by status, priority and search, twelve rows per page, and a seeded job opens from it with its details, assignee and history', async ({
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

    const summary = await api.findJobByNumber(1042)
    const job = await api.getJob(summary.id)
    await search.fill(`JOB-${job.number}`)
    await search.press('Enter')
    await expect(rowsOf(page)).toHaveCount(1)

    await rowsOf(page).getByRole('link', { name: job.title, exact: true }).click()

    await expect(page).toHaveURL(titleLinkPath(job))
    await expect(page.getByRole('heading', { level: 1, name: job.title })).toBeVisible()
    await openInApp(page, `/jobs/${job.id}`)
    await expect(page).toHaveURL(`/jobs/${job.id}`)
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

  test('an unknown job id shows the job-not-found state and an unknown path shows the not-found page, whose link leads to the dashboard', async ({
    adminPage: page,
    adminSession,
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
    await page.getByRole('link', { name: notFoundCopy.goToDashboard }).click()
    await expect(page).toHaveURL('/')
    await expect(
      page.getByRole('heading', {
        level: 1,
        name: dashboardCopy.greeting(adminSession.user.displayName),
      }),
    ).toBeVisible()
    await expect(page).toHaveTitle(documentTitle(dashboardCopy.heading))
  })
})

test.describe('E2E-07 assign a job', () => {
  test.use({
    expectedConsole: anyOf(
      consolePatterns.assignmentRejected,
      consolePatterns.techniciansServerError,
    ),
  })

  test('the panel lists technicians in assign order with Samuel Osei off shift, assigning Lena Park makes the job hers alone, and the technician filter finds it', async ({
    adminPage: page,
    adminSession,
    api,
    technician,
    runId,
  }) => {
    const job = await createUnassignedJob(api, runId, 'assign')
    const technicians = await api.technicians()
    const ordered = expectedAssignOrder(technicians, job.checklistTemplate.trade)
    const lena = technicianNamed(technicians, demoTechnicians.assignments.name)
    const priya = technicianNamed(technicians, demoTechnicians.inspections.name)
    const samuel = technicianNamed(technicians, demoTechnicians.offShift.name)
    expect(lena.availableForAssignment, 'Lena Park can be assigned').toBe(true)
    expect(samuel.availableForAssignment, 'Samuel Osei is off shift').toBe(false)
    expect(job.affordances.canAssign).toBe(true)

    await page.goto(`/jobs/${job.id}`)

    const panel = page.getByRole('region', { name: assignCopy.heading })
    await expect(panel).toContainText(
      assignCopy.tradeFirst(tradeLabels[job.checklistTemplate.trade]),
    )
    const group = panel.getByRole('group', { name: assignCopy.group })
    const radios = group.getByRole('radio')
    await expect(radios).toHaveCount(ordered.length)
    for (const [index, member] of ordered.entries()) {
      const radio = radios.nth(index)
      await expect(radio).toHaveAccessibleName(assignRowName(member, job.checklistTemplate.trade))
      await expect(radio).not.toBeChecked()
      await expect(radio).toBeEnabled({ enabled: member.availableForAssignment })
    }
    await expect(
      group.getByText(tradeLabels[job.checklistTemplate.trade], { exact: true }),
    ).toBeVisible()
    await expect(group.getByText(assignCopy.differentTrade, { exact: true })).toBeVisible()
    const samuelRow = group.getByRole('radio', { name: rowNamed(samuel.displayName) })
    await expect(samuelRow).toBeDisabled()
    await expect(samuelRow).toHaveAccessibleName(new RegExp(assignCopy.offShift))
    await expect(radios.last()).toHaveAccessibleName(rowNamed(samuel.displayName))
    const assignButton = panel.getByRole('button', { name: assignCopy.button, exact: true })
    await expect(assignButton).toHaveAttribute('aria-disabled', 'true')
    await expect(assignButton).toHaveAccessibleDescription(assignCopy.helper)
    const search = panel.getByRole('searchbox', { name: assignCopy.search })
    await search.fill('PARK')
    await expect(radios).toHaveCount(1)
    await expect(radios.first()).toHaveAccessibleName(rowNamed(lena.displayName))
    await search.fill('')
    await expect(radios).toHaveCount(ordered.length)

    const lenaRow = group.getByRole('radio', { name: rowNamed(lena.displayName) })
    const assignToLena = panel.getByRole('button', { name: assignCopy.assignTo(lena.displayName) })
    await lenaRow.check()
    await fulfilStub(
      page,
      `**/api/jobs/${job.id}/assignment`,
      {
        status: 400,
        body: problemBody(400, 'validation.failed', 'One or more fields are invalid.', {
          errors: { technicianId: [assignCopy.chooseFromList] },
        }),
      },
      1,
    )
    const refetchAfterRejection = page.waitForRequest(isTechniciansRead)
    await assignToLena.click()

    await refetchAfterRejection
    await expect(radios.first()).toBeEnabled()
    await expect(radios.first()).toBeFocused()
    await expect(group).toHaveAccessibleDescription(assignCopy.chooseFromList)

    await lenaRow.check()
    await expect(assignToLena).toHaveAccessibleDescription(assignCopy.willSee(lena.displayName))
    await assignToLena.focus()
    const lookBefore = await rowLook(lenaRow)
    const held = await holdNextRequest(page, `**/api/jobs/${job.id}/assignment`)
    const assigned = page.waitForResponse((response) => isAssignment(response.request(), job.id))
    await assignToLena.click()

    await expect(panel.getByRole('button', { name: assignCopy.busy })).toBeVisible()
    await expect(group).toHaveAttribute('inert')
    await expect(lenaRow).toBeChecked()
    expect(await rowLook(lenaRow), 'the chosen row keeps its look while busy').toEqual(lookBefore)
    const refetchAfterSuccess = page.waitForRequest(isTechniciansRead)
    held.release()
    const response = await assigned
    expect(response.status()).toBe(200)
    expect(held.body()).toEqual({ technicianId: lena.id, version: job.version })
    await refetchAfterSuccess
    await expect(group).not.toHaveAttribute('inert')
    const body = (await response.json()) as JobDetails
    await expect(
      page.getByRole('status').filter({ hasText: assignCopy.assigned(lena.displayName) }),
    ).toBeVisible()
    const reassignPanel = page.getByRole('region', { name: assignCopy.reassignHeading })
    await expect(
      reassignPanel.getByRole('radio', { name: rowNamed(lena.displayName) }),
    ).toBeChecked()
    const reassignButton = reassignPanel.getByRole('button', {
      name: assignCopy.reassignHeading,
      exact: true,
    })
    await expect(reassignButton).toHaveAttribute('aria-disabled', 'true')
    await expect(reassignButton).toBeFocused()
    await expect(
      page.getByRole('main').getByText(statusLabels.Assigned, { exact: true }).first(),
    ).toBeVisible()
    await expect(page.getByRole('region', { name: jobDetailCopy.detailsCard })).toContainText(
      lena.displayName,
    )
    await expect(historyItems(page)).toHaveCount(2)
    await expect(historyItems(page).last()).toContainText(
      assignCopy.historyAssigned(lena.displayName),
    )
    const stored = await api.getJob(job.id)
    expect(stored).toMatchObject({
      status: 'Assigned',
      assignedTechnician: { id: lena.id, displayName: lena.displayName },
      version: body.version,
    })
    expect(stored.version).toBeGreaterThan(job.version)
    expect(stored.assignedAt).not.toBeNull()
    expect(stored.history.map((entry) => entry.action)).toEqual(['Created', 'Assigned'])
    expect(stored.history[1]).toMatchObject({
      fromStatus: 'Unassigned',
      toStatus: 'Assigned',
      technician: { id: lena.id },
      actor: { id: adminSession.user.id },
    })

    const lenaApi = await technician(demoTechnicians.assignments.email)
    expect(lenaApi.user.id).toBe(lena.id)
    const lenaActive = await lenaApi.assignedJobs('active')
    expect(lenaActive.find((item) => item.id === job.id)).toMatchObject({
      number: job.number,
      status: 'Assigned',
      version: stored.version,
    })
    const lenaRead = await lenaApi.getJob(job.id)
    expect(lenaRead.status()).toBe(200)
    expect(((await lenaRead.json()) as JobDetails).affordances.canAssign).toBe(false)
    const priyaApi = await technician(demoTechnicians.inspections.email)
    expect(priyaApi.user.id).toBe(priya.id)
    for (const view of ['active', 'completed'] as const) {
      const items = await priyaApi.assignedJobs(view)
      expect(
        items.map((item) => item.id),
        `Priya Natarajan's ${view} list`,
      ).not.toContain(job.id)
    }
    expect(await readProblem(await priyaApi.getJob(job.id))).toMatchObject({
      status: 404,
      code: 'job.not-found',
    })

    await page
      .getByRole('navigation', { name: shellCopy.breadcrumb })
      .getByRole('link', { name: jobsCopy.heading })
      .click()
    await expect(page).toHaveURL('/jobs')
    const filter = page.getByRole('combobox', { name: jobsCopy.technicianFilter })
    await expect(filter).toBeEnabled()
    await expect(filter.getByRole('option')).toHaveText([
      jobsCopy.allTechnicians,
      ...technicians.map((member) => member.displayName),
    ])
    await filter.selectOption({ label: lena.displayName })
    await expect(page).toHaveURL(`/jobs?technicianId=${lena.id}`)
    const lenaJobs = await api.getJobs({ technicianId: lena.id, pageSize: 12 })
    await expect(rowsOf(page)).toHaveCount(lenaJobs.items.length)
    await expect(summaryOf(page)).toContainText(`of ${lenaJobs.totalCount} job`)
    await expect(rowsOf(page).filter({ hasNotText: lena.displayName })).toHaveCount(0)
    await expect(
      rowsOf(page).first().getByRole('link', { name: stored.title, exact: true }),
    ).toBeVisible()

    for (const status of notAssignableStatuses) {
      const sample = await firstJobWithStatus(api, status)
      expect((await api.getJob(sample.id)).affordances.canAssign, `canAssign on ${status}`).toBe(
        false,
      )
      await openInApp(page, `/jobs/${sample.id}`)
      await expect(page.getByRole('heading', { level: 1, name: sample.title })).toBeVisible()
      await expect(page.getByRole('region', { name: jobDetailCopy.history })).toBeVisible()
      await expect(page.getByRole('region', { name: assignCopy.heading })).toHaveCount(0)
      await expect(page.getByRole('region', { name: assignCopy.reassignHeading })).toHaveCount(0)
      await expect(page.getByRole('radio')).toHaveCount(0)
    }

    await fulfilStub(page, '**/api/technicians', serverError, 1)
    await openInApp(page, `/jobs?technicianId=${lena.id}`)

    const unloadedFilter = page.getByRole('combobox', { name: jobsCopy.technicianFilter })
    await expect(rowsOf(page)).toHaveCount(lenaJobs.items.length)
    await expect(unloadedFilter).toBeDisabled()
    await expect(unloadedFilter.getByRole('option')).toHaveText([jobsCopy.selectedTechnician])
    await expect(unloadedFilter).toHaveValue(lena.id)
  })
})

test.describe('W2 technician at creation and E2E-14 stale edit conflict (W2 part)', () => {
  test.use({ expectedConsole: consolePatterns.assignmentConflict })

  test('a job created with Lena Park opens Assigned with Created and Assigned entries, a reassignment from a stale page is refused, refetched and explained, and the New job banners move focus', async ({
    adminPage: page,
    api,
    guards,
    runId,
  }) => {
    const technicians = await api.technicians()
    const lena = technicianNamed(technicians, demoTechnicians.assignments.name)
    const priya = technicianNamed(technicians, demoTechnicians.inspections.name)
    const samuel = technicianNamed(technicians, demoTechnicians.offShift.name)
    const tom = technicianNamed(technicians, demoTechnicians.conflicting.name)
    for (const member of [lena, priya, tom]) {
      expect(member.availableForAssignment, `${member.displayName} can be assigned`).toBe(true)
    }
    const title = e2eTitle(runId, `${fireSafetyJob.title} (created assigned)`)
    const date = tomorrowInUtc()

    await page.goto('/jobs/new')

    const card = page.getByRole('region', { name: createJobCopy.checklistCard })
    const select = card.getByRole('combobox', { name: createJobCopy.technician })
    await expect(select).toBeEnabled()
    await expect(select).toHaveValue('')
    await expect(select).toHaveAccessibleDescription(createJobCopy.technicianHelp)
    await expect(select.getByRole('option')).toHaveText([
      createJobCopy.leaveUnassigned,
      ...expectedAssignOrder(technicians, null).map(technicianOptionLabel),
    ])
    expect(await optionGroupsOf(select)).toEqual([])
    await expect(select.getByRole('option', { name: technicianOptionLabel(samuel) })).toBeDisabled()
    await card
      .getByRole('combobox', { name: createJobCopy.checklistTemplate })
      .selectOption({ label: fireSafetyJob.template })
    const fireSafetyOrder = expectedAssignOrder(technicians, fireSafetyJob.trade)
    await expect(select.getByRole('option')).toHaveText([
      createJobCopy.leaveUnassigned,
      ...fireSafetyOrder.map(technicianOptionLabel),
    ])
    expect(await optionGroupsOf(select)).toEqual([
      {
        label: tradeLabels[fireSafetyJob.trade],
        options: fireSafetyOrder
          .filter((member) => member.trade === fireSafetyJob.trade)
          .map(technicianOptionLabel),
      },
      {
        label: assignCopy.differentTrade,
        options: fireSafetyOrder
          .filter((member) => member.trade !== fireSafetyJob.trade)
          .map(technicianOptionLabel),
      },
    ])
    await expect(select).toHaveValue('')
    await fillFireSafetyForm(page, title, date)
    await select.selectOption(lena.id)
    const sentCreate = await captureNextBody(page, '**/api/jobs')
    const created = page.waitForResponse((response) => isCreateJob(response.request()))
    await page.getByRole('button', { name: createJobCopy.create }).click()

    const response = await created
    expect(response.status()).toBe(201)
    expect(sentCreate()).toMatchObject({ technicianId: lena.id })
    const job = (await response.json()) as JobDetails
    await expect(page).toHaveURL(`/jobs/${job.id}`)
    await expect(page.getByRole('heading', { level: 1, name: title })).toBeVisible()
    const details = page.getByRole('region', { name: jobDetailCopy.detailsCard })
    await expect(details).toContainText(lena.displayName)
    await expect(
      page.getByRole('main').getByText(statusLabels.Assigned, { exact: true }).first(),
    ).toBeVisible()
    const stored = await api.getJob(job.id)
    expect(stored).toMatchObject({
      status: 'Assigned',
      assignedTechnician: { id: lena.id },
      version: job.version,
    })
    expect(stored.assignedAt).not.toBeNull()
    expect(stored.history.map((entry) => entry.action)).toEqual(['Created', 'Assigned'])
    expect(stored.history[1]?.technician?.id).toBe(lena.id)
    expect(stored.history[1]?.occurredAt).toBe(stored.history[0]?.occurredAt)
    await expect(historyItems(page)).toHaveCount(2)
    await expect(historyItems(page).first()).toContainText(
      `Created by ${stored.createdBy.displayName}`,
    )
    await expect(historyItems(page).last()).toContainText(
      assignCopy.historyAssigned(lena.displayName),
    )
    const panel = page.getByRole('region', { name: assignCopy.reassignHeading })
    await expect(panel.getByRole('radio', { name: rowNamed(lena.displayName) })).toBeChecked()
    const reassign = panel.getByRole('button', { name: assignCopy.reassignHeading, exact: true })
    await expect(reassign).toHaveAttribute('aria-disabled', 'true')
    await expect(reassign).toHaveAccessibleDescription(assignCopy.assignedNow(lena.displayName))

    const moved = await api.assignJob(job.id, tom.id, stored.version)
    expect(moved.assignedTechnician?.id).toBe(tom.id)
    await page.setViewportSize({ width: 375, height: 667 })
    await panel.getByRole('radio', { name: rowNamed(priya.displayName) }).check()
    const reassignToPriya = panel.getByRole('button', {
      name: assignCopy.reassignTo(priya.displayName),
    })
    const sentAssignment = await captureNextBody(page, `**/api/jobs/${job.id}/assignment`)
    const refused = page.waitForResponse((answer) => isAssignment(answer.request(), job.id))
    const refetched = page.waitForResponse((answer) => isJobRead(answer.request(), job.id))
    const techniciansAgain = page.waitForRequest(isTechniciansRead)
    await reassignToPriya.click()

    const conflict = await refused
    expect(conflict.status()).toBe(409)
    expect(((await conflict.json()) as { code?: string }).code).toBe('concurrency.conflict')
    expect(sentAssignment()).toEqual({ technicianId: priya.id, version: stored.version })
    expect((await refetched).status()).toBe(200)
    await techniciansAgain
    const notice = panel
      .getByRole('alert')
      .filter({ hasText: assignCopy.changedTitle(`JOB-${job.number}`) })
    await expect(notice).toContainText(assignCopy.changedMessage)
    await expect(details).toContainText(tom.displayName)
    await expect(historyItems(page)).toHaveCount(3)
    await expect(historyItems(page).last()).toContainText(
      assignCopy.historyAssigned(tom.displayName),
    )
    await expect(panel.getByRole('radio', { name: rowNamed(priya.displayName) })).toBeChecked()
    await expect(reassignToPriya).toBeFocused()
    const margins = await marginsInViewport(page, reassignToPriya)
    expect(
      Math.min(margins.left, margins.top, margins.right, margins.bottom),
      'the button and 4 px around it lie inside the 375 × 667 viewport after the conflict',
    ).toBeGreaterThanOrEqual(4)
    await expect(notice).toBeInViewport()
    await page.setViewportSize({ width: 1280, height: 800 })
    const after = await api.getJob(job.id)
    expect(after).toMatchObject({
      status: 'Assigned',
      assignedTechnician: { id: tom.id },
      version: moved.version,
    })
    expect(
      after.history
        .filter((entry) => entry.action === 'Assigned')
        .map((entry) => entry.technician?.id),
    ).toEqual([lena.id, tom.id])

    guards.allowConsole(
      consolePatterns.techniciansServerError,
      consolePatterns.templatesServerError,
    )
    await fulfilStub(page, '**/api/technicians', serverError, 2)
    await fulfilStub(page, '**/api/checklist-templates', serverError, 2)
    await openInApp(page, '/jobs/new')

    const newCard = page.getByRole('region', { name: createJobCopy.checklistCard })
    const techniciansBanner = (role: 'status' | 'alert') =>
      newCard.getByRole(role).filter({ hasText: createJobCopy.techniciansFailed })
    const templatesBanner = (role: 'status' | 'alert') =>
      newCard.getByRole(role).filter({ hasText: createJobCopy.templatesFailed })
    const retry = (banner: Locator) => banner.getByRole('button', { name: createJobCopy.tryAgain })
    await expect(techniciansBanner('status')).toContainText(createJobCopy.techniciansFailedHelp)
    await expect(templatesBanner('status')).toBeVisible()
    await retry(techniciansBanner('status')).click()
    await expect(retry(techniciansBanner('alert'))).toBeFocused()
    await retry(techniciansBanner('alert')).click()
    await expect(newCard.getByRole('combobox', { name: createJobCopy.technician })).toBeFocused()
    await retry(templatesBanner('status')).click()
    await expect(retry(templatesBanner('alert'))).toBeFocused()
    await fulfilStub(page, '**/api/checklist-templates', { status: 200, body: { items: [] } }, 1)
    await retry(templatesBanner('alert')).click()

    const cardHeading = newCard.getByRole('heading', {
      level: 2,
      name: createJobCopy.checklistCard,
    })
    await expect(cardHeading).toBeFocused()
    await expect(cardHeading).toHaveAttribute('tabindex', '-1')
    const templateSelect = newCard.getByRole('combobox', { name: createJobCopy.checklistTemplate })
    await expect(templateSelect).toBeDisabled()
    await expect(templateSelect).toContainText(createJobCopy.noTemplates)
  })
})

test.describe('W3 status changes in the dashboard (W3-AC7)', () => {
  test('after the technician starts, holds and resumes a job, a reload of the job detail shows Started, Put on hold with the note and Resumed; the In progress and On hold chips find the job; the job page offers Cancel job as its only control and the dashboard never sends a status change', async ({
    adminPage: page,
    adminSession,
    api,
    guards,
    sentinelSink,
    technician,
    runId,
  }) => {
    const lenaApi = await technician(demoTechnicians.assignments.email)
    const lena = lenaApi.user
    const note = holdNoteFor(runId)
    sentinelSink('hold note', note)
    const job = await createAssignedJob(api, runId, 'status changes', lena.id)
    expect(
      job.affordances.allowedStatusChanges,
      'the administrator is offered no status change',
    ).toEqual([])
    const statusRequests = trackStatusRequests(page)
    const chips = page.getByRole('group', { name: jobsCopy.statusFilter })
    const ownRow = rowsOf(page).filter({
      has: page.getByRole('link', { name: job.title, exact: true }),
    })

    await page.goto(`/jobs/${job.id}`)

    await expect(page.getByRole('heading', { level: 1, name: job.title })).toBeVisible()
    await expect(
      page.getByRole('main').getByText(statusLabels.Assigned, { exact: true }).first(),
    ).toBeVisible()
    await expect(historyItems(page)).toHaveCount(2)

    const started = await lenaApi.start(job.id)
    expect(started).toMatchObject({ status: 'InProgress', version: expect.any(Number) })
    expect(started.startedAt).not.toBeNull()
    expect(started.affordances.allowedStatusChanges).toEqual([
      { status: 'OnHold', requiresNote: true },
    ])
    const inProgress = await api.getJobs({ status: 'InProgress', pageSize: 12 })
    const inProgressCount = inProgress.statusCounts.find((entry) => entry.status === 'InProgress')
    await page
      .getByRole('navigation', { name: shellCopy.breadcrumb })
      .getByRole('link', { name: jobsCopy.heading })
      .click()
    await expect(page).toHaveURL('/jobs')
    await chips.getByRole('button', { name: new RegExp(`^${statusLabels.InProgress}`) }).click()

    await expect(page).toHaveURL('/jobs?status=InProgress')
    await expect(
      chips.getByRole('button', {
        name: chipName(statusLabels.InProgress, inProgressCount?.count),
      }),
    ).toHaveAttribute('aria-pressed', 'true')
    await expect(rowsOf(page)).toHaveCount(inProgress.items.length)
    await expect(rowsOf(page).getByText(statusLabels.InProgress, { exact: true })).toHaveCount(
      inProgress.items.length,
    )
    await expect(ownRow).toHaveCount(1)
    await expect(ownRow).toContainText(statusLabels.InProgress)

    const held = await lenaApi.hold(job.id, note)
    expect(held.status).toBe('OnHold')
    expect(held.startedAt).toBe(started.startedAt)
    expect(held.affordances.allowedStatusChanges).toEqual([
      { status: 'InProgress', requiresNote: false },
    ])
    const onHold = await api.getJobs({ status: 'OnHold', pageSize: 12 })
    const onHoldCount = onHold.statusCounts.find((entry) => entry.status === 'OnHold')
    await chips.getByRole('button', { name: new RegExp(`^${statusLabels.OnHold}`) }).click()

    await expect(page).toHaveURL('/jobs?status=OnHold')
    await expect(
      chips.getByRole('button', { name: chipName(statusLabels.OnHold, onHoldCount?.count) }),
    ).toHaveAttribute('aria-pressed', 'true')
    await expect(
      chips.getByRole('button', {
        name: chipName(
          statusLabels.InProgress,
          onHold.statusCounts.find((entry) => entry.status === 'InProgress')?.count,
        ),
      }),
    ).toHaveAttribute('aria-pressed', 'false')
    await expect(rowsOf(page)).toHaveCount(onHold.items.length)
    await expect(rowsOf(page).getByText(statusLabels.OnHold, { exact: true })).toHaveCount(
      onHold.items.length,
    )
    await expect(ownRow).toContainText(statusLabels.OnHold)
    await ownRow.getByRole('link', { name: job.title, exact: true }).click()

    await expect(page).toHaveURL(`/jobs/${job.id}`)
    await expect(
      page.getByRole('main').getByText(statusLabels.OnHold, { exact: true }).first(),
    ).toBeVisible()
    await expect(historyItems(page)).toHaveCount(4)
    await expect(historyItems(page).last()).toContainText(historyCopy.putOnHold(lena.displayName))
    await expect(historyItems(page).last()).toContainText(historyCopy.note(note))
    await expectOnlyCancelIn(page)

    const resumed = await lenaApi.resume(job.id)
    expect(resumed.status).toBe('InProgress')
    expect(resumed.startedAt).toBe(started.startedAt)

    await page.reload()

    await expect(page.getByRole('heading', { level: 1, name: job.title })).toBeVisible()
    await expect(
      page.getByRole('main').getByText(statusLabels.InProgress, { exact: true }).first(),
    ).toBeVisible()
    await expect(
      page.getByRole('main').getByText(statusLabels.OnHold, { exact: true }),
    ).toHaveCount(0)
    await expect(historyItems(page)).toHaveCount(5)
    const sentences = [
      historyCopy.created(adminSession.user.displayName),
      historyCopy.assigned(lena.displayName),
      historyCopy.started(lena.displayName),
      historyCopy.putOnHold(lena.displayName),
      historyCopy.resumed(lena.displayName),
    ]
    for (const [index, sentence] of sentences.entries()) {
      await expect(historyItems(page).nth(index)).toContainText(sentence)
    }
    await expect(historyItems(page).nth(3)).toContainText(historyCopy.note(note))
    await expect(page.getByText(historyCopy.note(note), { exact: true })).toHaveCount(1)
    await expectOnlyCancelIn(page)
    const stored = await api.getJob(job.id)
    expect(stored.history.map((entry) => entry.action)).toEqual([
      'Created',
      'Assigned',
      'Started',
      'PutOnHold',
      'Resumed',
    ])
    expect(stored.history.map((entry) => entry.note)).toEqual([null, null, null, note, null])
    expect(stored.history.slice(2).map((entry) => entry.actor.id)).toEqual([
      lena.id,
      lena.id,
      lena.id,
    ])
    expect(stored).toMatchObject({
      status: 'InProgress',
      version: resumed.version,
      startedAt: started.startedAt,
    })
    expect(statusRequests, 'requests to /status sent by the dashboard').toEqual([])
    expect(guards.cspViolations).toEqual([])
  })
})
