import { randomUUID } from 'node:crypto'

import type { APIResponse, Locator, Page, TestInfo } from '@playwright/test'

import { consolePatterns, expect, test } from './fixtures.ts'
import {
  demoTechnicians,
  readProblem,
  type AdminApi,
  type Inspection,
  type InspectionAnswerInput,
  type InspectionResult,
  type JobDetails,
} from './support/api.ts'
import {
  historyCopy,
  jobDetailCopy,
  jobsCopy,
  liveCopy,
  reportCopy,
  shellCopy,
  statusLabels,
} from './support/copy.ts'
import { jobWithDraftInspection, seededJobInStatus, tomorrowInUtc } from './support/jobs.ts'
import { e2eTitle } from './support/run.ts'

const hvacTemplate = 'Rooftop HVAC service'

const notApplicableNote = 'Direct-drive unit'

const liveBudgetMilliseconds = 5_000

const markerName = '__e2eReportMarker'

const resultLabels: Readonly<Record<InspectionResult, string>> = {
  Pass: 'Pass',
  Fail: 'Fail',
  NotApplicable: 'N/A',
}

interface PlannedAnswer {
  readonly result: InspectionResult
  readonly note?: string
  readonly values?: readonly number[]
}

function plannedAnswers(failNote: string): ReadonlyMap<number, PlannedAnswer> {
  return new Map<number, PlannedAnswer>([
    [1, { result: 'Pass' }],
    [2, { result: 'Pass' }],
    [3, { result: 'Pass', values: [118, 285] }],
    [4, { result: 'Pass' }],
    [5, { result: 'Fail', note: failNote }],
    [6, { result: 'NotApplicable', note: notApplicableNote }],
    [7, { result: 'Pass' }],
    [8, { result: 'Pass', values: [54] }],
    [9, { result: 'Pass' }],
    [10, { result: 'Pass' }],
  ])
}

function answersFor(
  inspection: Inspection,
  plan: ReadonlyMap<number, PlannedAnswer>,
  positions: readonly number[],
): InspectionAnswerInput[] {
  return positions.map((position) => {
    const item = inspection.items.find((entry) => entry.position === position)
    const planned = plan.get(position)
    if (item === undefined || planned === undefined) {
      throw new Error(`Item ${position} is not in the checklist.`)
    }
    const fields = [...item.readingFields].sort((first, second) => first.position - second.position)
    const values = planned.values ?? []
    return {
      checklistItemId: item.checklistItemId,
      result: planned.result,
      note: planned.note ?? null,
      readings:
        values.length === 0
          ? null
          : values.map((value, index) => {
              const field = fields[index]
              if (field === undefined) {
                throw new Error(`Item ${position} has no reading field ${index + 1}.`)
              }
              return { readingFieldId: field.readingFieldId, value }
            }),
    }
  })
}

async function readJson<T>(response: APIResponse, what: string): Promise<T> {
  if (!response.ok()) {
    const problem = await readProblem(response)
    throw new Error(`${what} failed with ${problem.status} ${problem.code}.`)
  }
  return (await response.json()) as T
}

async function expectProblem(response: APIResponse, status: number, code: string): Promise<void> {
  expect(response.status()).toBe(status)
  expect((await readProblem(response)).code).toBe(code)
}

async function createHvacJob(
  api: AdminApi,
  runId: string,
  technicianId: string,
): Promise<JobDetails> {
  const template = (await api.checklistTemplates()).find((item) => item.name === hvacTemplate)
  if (template === undefined) {
    throw new Error(`The checklist template "${hvacTemplate}" is missing.`)
  }
  const sample = await api.getJob((await api.findJobByNumber(1042)).id)
  const date = tomorrowInUtc()
  const job = await api.createJob({
    title: e2eTitle(runId, `${hvacTemplate} (inspection report)`),
    description: sample.description,
    priority: 'Normal',
    customerName: sample.customerName,
    siteAddress: sample.siteAddress,
    scheduledStart: `${date}T09:00:00Z`,
    scheduledEnd: `${date}T11:00:00Z`,
    checklistTemplateId: template.id,
    technicianId,
  })
  if (job.status !== 'Assigned' || job.assignedTechnician?.id !== technicianId) {
    throw new Error(`The inspection job is ${job.status}, not Assigned to the technician.`)
  }
  return job
}

function tile(page: Page, key: string): Locator {
  return page.locator(`.kpi-tile[data-tile="${key}"]`)
}

function resultChips(page: Page): Locator {
  return tile(page, 'result').locator('.kpi-tile__content .status-chip')
}

function notice(page: Page): Locator {
  return page.locator('main .page-header + .banner')
}

function resultRows(page: Page): Locator {
  return page.getByRole('region', { name: reportCopy.results }).locator('tbody tr')
}

function rowAt(page: Page, position: number): Locator {
  return resultRows(page).nth(position - 1)
}

function readingsAt(page: Page, position: number): Locator {
  return rowAt(page, position).locator('.inspection-results__reading')
}

function editableControls(page: Page): Locator {
  return page.locator('main').locator('input, select, textarea, button')
}

function pill(page: Page): Locator {
  return page.getByRole('banner').getByText(liveCopy.pillAnyState)
}

function notifications(page: Page): Locator {
  return page.getByRole('region', { name: shellCopy.notifications })
}

function wholeMinutes(moment: string | null): number {
  return Math.floor(Date.parse(moment ?? '') / 60_000)
}

function expectedTimeOnSite(job: JobDetails): string | RegExp {
  const minutes = wholeMinutes(job.completedAt) - wholeMinutes(job.startedAt)
  if (minutes === 0) {
    return reportCopy.lessThanAMinute
  }
  return minutes < 60 ? `${minutes} min` : /^\d+ h( \d+ min)?$/
}

interface ReadingLayout {
  readonly valueBelowLabel: number
  readonly gapToNext: number | null
}

async function readingLayout(readings: Locator): Promise<ReadingLayout[]> {
  return readings.evaluateAll((elements) =>
    elements.map((element, index) => {
      const label = element.querySelector('.inspection-results__reading-label')
      const value = element.querySelector('.inspection-results__reading-value')
      const next = elements[index + 1]
      const box = element.getBoundingClientRect()
      return {
        valueBelowLabel:
          label === null || value === null
            ? Number.NaN
            : value.getBoundingClientRect().top - label.getBoundingClientRect().bottom,
        gapToNext: next === undefined ? null : next.getBoundingClientRect().top - box.bottom,
      }
    }),
  )
}

interface ExpectedReadings {
  readonly position: number
  readonly texts: readonly string[]
}

function expectedReadings(inspection: Inspection): ExpectedReadings[] {
  return inspection.items.flatMap((item) => {
    const readings = item.answer?.readings ?? []
    const texts = [...item.readingFields]
      .sort((first, second) => first.position - second.position)
      .flatMap((field) => {
        const reading = readings.find((entry) => entry.readingFieldId === field.readingFieldId)
        return reading === undefined
          ? []
          : [reportCopy.reading(field.label, String(Number(reading.value)), field.unit)]
      })
    return texts.length === 0 ? [] : [{ position: item.position, texts }]
  })
}

function countsOf(inspection: Inspection): string[] {
  const { passCount, failCount, notApplicableCount } = inspection.progress
  return [`${passCount} Pass`, `${failCount} Fail`, `${notApplicableCount} N/A`]
}

async function setMarker(page: Page, value: string): Promise<void> {
  await page.evaluate(
    ([name, marker]) => {
      ;(window as unknown as Record<string, string>)[name] = marker
    },
    [markerName, value] as const,
  )
}

async function markerOf(page: Page): Promise<string | undefined> {
  return page.evaluate(
    (name) => (window as unknown as Record<string, string | undefined>)[name],
    markerName,
  )
}

async function capture(page: Page, testInfo: TestInfo, name: string): Promise<void> {
  const path = testInfo.outputPath(`${name}.png`)
  await page.screenshot({ path, fullPage: true })
  await testInfo.attach(name, { path, contentType: 'image/png' })
}

async function expectReadOnlyReport(page: Page, job: JobDetails): Promise<void> {
  await expect(page).toHaveURL(`/jobs/${job.id}/inspection`)
  await expect(page).toHaveTitle(reportCopy.documentTitle(`JOB-${job.number}`))
  await expect(page.getByRole('heading', { level: 1, name: job.title })).toBeVisible()
  await expect(page.locator('.page-header__eyebrow')).toContainText(reportCopy.eyebrowSuffix)
  await expect(page.getByRole('link', { name: reportCopy.jobDetailsLink })).toBeVisible()
  await expect(resultRows(page)).not.toHaveCount(0)
  await expect(editableControls(page)).toHaveCount(0)
}

test.describe('E2E-12 inspection to read-only report (W5-AC9, W5-AC10)', () => {
  test('a submission through the API completes the job live on its page and its report, and the report shows the results, readings, notes, technician notes, time on site and history with no editable control', async ({
    adminPage: page,
    api,
    guards,
    technician,
    runId,
    sentinelSink,
  }, testInfo) => {
    const priyaApi = await technician(demoTechnicians.inspections.email)
    const priya = priyaApi.user
    const failNote = `Bearing noise at start-up. Recommend motor replacement within 30 days. E2E ${runId}`
    const notesFirstLine = `Replaced the return air filters and cleared the condensate drain. E2E ${runId}`
    const notesSecondLine = 'The condenser fan motor needs a replacement within 30 days.'
    sentinelSink('inspection fail note', failNote)
    sentinelSink('technician notes', notesFirstLine)
    const plan = plannedAnswers(failNote)
    const job = await createHvacJob(api, runId, priya.id)
    const started = await priyaApi.start(job.id)
    expect(started.status).toBe('InProgress')

    const opened = await readJson<Inspection>(await priyaApi.getInspection(job.id), 'GetInspection')
    expect(opened.progress).toEqual({
      answered: 0,
      total: 10,
      passCount: 0,
      failCount: 0,
      notApplicableCount: 0,
    })
    const draft = await readJson<Inspection>(
      await priyaApi.saveInspection(job.id, {
        answers: answersFor(opened, plan, [1, 2, 3, 4, 5, 6]),
        version: opened.version,
      }),
      'SaveInspection',
    )
    expect(draft.affordances.canSubmit).toEqual({
      allowed: false,
      blockingCode: 'inspection.incomplete',
      answered: 6,
      total: 10,
    })
    const adminDraft = await api.getInspection(job.id)
    expect(adminDraft.progress.answered).toBe(6)
    expect(adminDraft.progress.total).toBe(10)
    expect(adminDraft.affordances).toEqual({
      canEdit: false,
      canSubmit: { allowed: false, blockingCode: 'auth.forbidden', answered: 6, total: 10 },
    })
    await expectProblem(
      await priyaApi.submitJob(job.id, {
        technicianNotes: null,
        siteSafetyConfirmed: true,
        version: draft.version,
      }),
      409,
      'inspection.incomplete',
    )

    const reportPage = await page.context().newPage()
    try {
      await guards.watch(reportPage)
      await reportPage.goto(`/jobs/${job.id}/inspection`)
      await expectReadOnlyReport(reportPage, job)
      await expect(pill(reportPage)).toHaveText(liveCopy.pillLive, { timeout: 10_000 })
      await expect(notice(reportPage)).toContainText(reportCopy.notSubmittedTitle)
      await expect(notice(reportPage)).toContainText(reportCopy.notSubmittedMessage)
      await expect(resultChips(reportPage)).toHaveText(['4 Pass', '1 Fail', '1 N/A'])
      await expect(tile(reportPage, 'result')).toContainText(reportCopy.answered(6, 10))
      await expect(readingsAt(reportPage, 3)).toHaveText([
        reportCopy.reading('Suction', '118', 'psi'),
        reportCopy.reading('Discharge', '285', 'psi'),
      ])
      for (const position of [7, 8, 9, 10]) {
        await expect(rowAt(reportPage, position)).toContainText(reportCopy.notAnswered)
      }
      await expect(reportPage.locator('.inspection-report__notes')).toHaveText(
        reportCopy.notesPending,
      )
      await setMarker(reportPage, runId)
      await capture(reportPage, testInfo, 'w5-report-not-submitted-1280')

      await page.goto(`/jobs/${job.id}`)
      await expect(page.getByRole('heading', { level: 1, name: job.title })).toBeVisible()
      await expect(pill(page)).toHaveText(liveCopy.pillLive, { timeout: 10_000 })
      await expect(page.locator('.page-header__meta')).toContainText(statusLabels.InProgress)
      await expect(page.getByRole('link', { name: reportCopy.reportLink })).toHaveCount(0)
      await setMarker(page, runId)
      expect(guards.handshakesChecked(), 'the live handshake URLs were checked').toBeGreaterThan(1)

      const complete = await readJson<Inspection>(
        await priyaApi.saveInspection(job.id, {
          answers: answersFor(opened, plan, [7, 8, 9, 10]),
          version: draft.version,
        }),
        'SaveInspection',
      )
      expect(complete.progress.answered).toBe(10)
      expect(complete.affordances.canSubmit.allowed).toBe(true)
      const submitted = await readJson<JobDetails>(
        await priyaApi.submitJob(job.id, {
          technicianNotes: `${notesFirstLine}\n${notesSecondLine}`,
          siteSafetyConfirmed: true,
          version: complete.version,
        }),
        'SubmitJob',
      )
      const committedAt = Date.now()
      expect(submitted.status).toBe('Completed')

      await expect(page.locator('.page-header__meta')).toContainText(statusLabels.Completed, {
        timeout: liveBudgetMilliseconds,
      })
      await expect(
        notifications(page).getByText(liveCopy.completed(priya.displayName), { exact: true }),
      ).toBeVisible({ timeout: Math.max(liveBudgetMilliseconds - (Date.now() - committedAt), 1) })
      const pageLatency = Date.now() - committedAt
      await expect(
        notifications(page)
          .getByText(liveCopy.body(job.title, statusLabels.Completed), { exact: true })
          .last(),
      ).toBeVisible()
      await expect(page.getByRole('link', { name: reportCopy.reportLink })).toBeVisible()
      expect(await markerOf(page), 'the job page did not reload').toBe(runId)
      await capture(page, testInfo, 'w5-job-completed-live-1280')

      await expect(notice(reportPage)).toHaveCount(0, {
        timeout: Math.max(liveBudgetMilliseconds - (Date.now() - committedAt), 1),
      })
      await expect(resultChips(reportPage)).toHaveText(['8 Pass', '1 Fail', '1 N/A'])
      const reportLatency = Date.now() - committedAt
      expect(await markerOf(reportPage), 'the report did not reload').toBe(runId)
      expect(Math.max(pageLatency, reportLatency)).toBeLessThanOrEqual(liveBudgetMilliseconds)
      testInfo.annotations.push({
        type: 'completion latency',
        description: `job page ${pageLatency} ms, report ${reportLatency} ms`,
      })

      const stored = await api.getJob(job.id)
      expect(Date.parse(stored.completedAt ?? '')).toBeGreaterThanOrEqual(
        Date.parse(stored.startedAt ?? ''),
      )
      await expect(tile(reportPage, 'time-on-site').locator('.kpi-tile__value')).toHaveText(
        expectedTimeOnSite(stored),
      )
      await expect(tile(reportPage, 'result')).toContainText(hvacTemplate)
      await expect(tile(reportPage, 'result')).toContainText('10 items')
      await expect(tile(reportPage, 'completed')).toContainText(`by ${priya.displayName}`)
      await expect(reportPage.locator('.page-header__meta')).toContainText(
        `by ${priya.displayName}`,
      )
      await expect(resultRows(reportPage)).toHaveCount(10)
      for (const [position, planned] of plan) {
        await expect(rowAt(reportPage, position)).toContainText(resultLabels[planned.result])
      }
      await expect(readingsAt(reportPage, 3)).toHaveText([
        reportCopy.reading('Suction', '118', 'psi'),
        reportCopy.reading('Discharge', '285', 'psi'),
      ])
      await expect(readingsAt(reportPage, 8)).toHaveText([
        reportCopy.reading('Supply air temperature', '54', '°F'),
      ])
      const pressures = await readingLayout(readingsAt(reportPage, 3))
      expect(pressures, 'each reading sets its value under its label, 6 px apart').toEqual([
        { valueBelowLabel: expect.closeTo(0, 0), gapToNext: expect.closeTo(6, 0) },
        { valueBelowLabel: expect.closeTo(0, 0), gapToNext: null },
      ])
      await expect(rowAt(reportPage, 5).locator('.inspection-results__note')).toHaveText(failNote)
      await expect(rowAt(reportPage, 6).locator('.inspection-results__note')).toHaveText(
        notApplicableNote,
      )
      const notes = reportPage.locator('.inspection-report__notes')
      await expect(notes).toContainText(notesFirstLine)
      await expect(notes).toContainText(notesSecondLine)
      const history = reportPage.getByRole('region', { name: reportCopy.statusHistory })
      await expect(history).toContainText(historyCopy.started(priya.displayName))
      await expect(history).toContainText(historyCopy.completed(priya.displayName))
      await expect(editableControls(reportPage)).toHaveCount(0)
      await capture(reportPage, testInfo, 'w5-report-submitted-live-1280')

      const report = await api.getInspection(job.id)
      expect(report.submittedAt).not.toBeNull()
      expect(report.technicianNotes).toBe(`${notesFirstLine}\n${notesSecondLine}`)
      expect(report.affordances.canSubmit.blockingCode).toBe('auth.forbidden')
      const readOnly = await readJson<Inspection>(
        await priyaApi.getInspection(job.id),
        'GetInspection',
      )
      expect(readOnly.affordances).toEqual({
        canEdit: false,
        canSubmit: {
          allowed: false,
          blockingCode: 'job.invalid-transition',
          answered: 10,
          total: 10,
        },
      })
      await expectProblem(
        await priyaApi.saveInspection(job.id, {
          answers: answersFor(
            opened,
            plannedAnswers(`Changed after submission. E2E ${runId}`),
            [5],
          ),
          version: stored.version,
        }),
        409,
        'inspection.locked',
      )

      await reportPage.reload()
      await expectReadOnlyReport(reportPage, job)
      await expect(resultChips(reportPage)).toHaveText(['8 Pass', '1 Fail', '1 N/A'])

      await page.goto(`/jobs?search=${encodeURIComponent(`JOB-${job.number}`)}`)
      const titleLink = page
        .getByRole('region', { name: jobsCopy.table })
        .getByRole('link', { name: job.title, exact: true })
      await expect(titleLink).toHaveAttribute('href', `/jobs/${job.id}/inspection`)
      await titleLink.click()
      await expectReadOnlyReport(page, job)
      await page.getByRole('link', { name: reportCopy.jobDetailsLink }).click()
      await expect(page).toHaveURL(`/jobs/${job.id}`)
      await page.getByRole('link', { name: reportCopy.reportLink }).click()
      await expectReadOnlyReport(page, job)
    } finally {
      await reportPage.close()
    }
  })
})

test.describe('W5 seeded inspection reports and E2E-18 (report part)', () => {
  test.use({ expectedConsole: consolePatterns.jobNotFound })

  test('the submitted, not submitted and not started reports read from the API, a completed title opens its report, and an unknown job shows not found', async ({
    adminPage: page,
    api,
  }) => {
    const submittedJob = await api.getJob((await api.findJobByNumber(1039)).id)
    const submitted = await api.getInspection(submittedJob.id)
    expect(submittedJob.status).toBe('Completed')
    expect(submitted.submittedAt).not.toBeNull()
    await page.goto(`/jobs?search=${encodeURIComponent('JOB-1039')}`)
    await page
      .getByRole('region', { name: jobsCopy.table })
      .getByRole('link', { name: submittedJob.title, exact: true })
      .click()
    await expectReadOnlyReport(page, submittedJob)
    await expect(notice(page)).toHaveCount(0)
    await expect(resultChips(page)).toHaveText(countsOf(submitted))
    await expect(resultRows(page)).toHaveCount(submitted.progress.total)
    const technicianName = submittedJob.assignedTechnician?.displayName ?? ''
    await expect(page.locator('.page-header__meta')).toContainText(`by ${technicianName}`)
    const seededReadings = expectedReadings(submitted)
    expect(seededReadings.length).toBeGreaterThan(0)
    for (const expected of seededReadings) {
      await expect(readingsAt(page, expected.position)).toHaveText([...expected.texts])
    }

    const draftJob = await jobWithDraftInspection(api)
    const draft = await api.getInspection(draftJob.id)
    expect(draftJob.status).toBe('InProgress')
    await page.goto(`/jobs/${draftJob.id}`)
    await expect(page.getByRole('link', { name: reportCopy.reportLink })).toHaveCount(0)
    await page.goto(`/jobs/${draftJob.id}/inspection`)
    await expectReadOnlyReport(page, draftJob)
    await expect(notice(page)).toContainText(reportCopy.notSubmittedTitle)
    await expect(tile(page, 'result')).toContainText(
      reportCopy.answered(draft.progress.answered, draft.progress.total),
    )
    await expect(resultChips(page)).toHaveText(countsOf(draft))
    await expect(resultRows(page).filter({ hasText: reportCopy.notAnswered })).toHaveCount(
      draft.progress.total - draft.progress.answered,
    )
    await expect(page.locator('.inspection-report__notes')).toHaveText(reportCopy.notesPending)

    const notStartedJob = await seededJobInStatus(api, 'Assigned')
    await page.goto(`/jobs/${notStartedJob.id}/inspection`)
    await expectReadOnlyReport(page, notStartedJob)
    await expect(notice(page)).toContainText(reportCopy.notStartedTitle)
    await expect(notice(page)).toContainText(reportCopy.notStartedMessage)
    await expect(tile(page, 'result').locator('.kpi-tile__value')).toHaveClass(
      /kpi-tile__value--unavailable/,
    )
    await expect(tile(page, 'time-on-site')).toContainText('Not started')
    await expect(resultRows(page).filter({ hasText: reportCopy.notAnswered })).toHaveCount(
      await resultRows(page).count(),
    )

    await page.goto(`/jobs/${randomUUID()}/inspection`)
    await expect(
      page.getByRole('heading', { level: 1, name: jobDetailCopy.notFoundHeading }),
    ).toBeVisible()
    await expect(page.getByText(jobDetailCopy.notFoundTitle)).toBeVisible()
  })
})
