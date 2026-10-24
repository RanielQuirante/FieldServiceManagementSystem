import { setTimeout as delay } from 'node:timers/promises'

import type { Locator, Page, TestInfo } from '@playwright/test'

import { anyOf, consolePatterns, expect, test } from './fixtures.ts'
import { demoTechnicians, type JobDetails } from './support/api.ts'
import { audit } from './support/axe.ts'
import {
  assignCopy,
  cancelCopy,
  historyCopy,
  jobDetailCopy,
  liveCopy,
  reportCopy,
  shellCopy,
  statusLabels,
} from './support/copy.ts'
import { isCancellation, trackRequests } from './support/dashboard.ts'
import { completeJob, createAssignedJob, createUnassignedJob } from './support/jobs.ts'
import { openInApp } from './support/run.ts'
import { SocketGate } from './support/websocket.ts'

const bell = String.fromCharCode(7)

interface CancelPage {
  readonly heading: (title: string) => Locator
  readonly trigger: Locator
  readonly dialog: Locator
  readonly reason: Locator
  readonly confirm: Locator
  readonly keep: Locator
  readonly history: Locator
}

function cancelPage(page: Page): CancelPage {
  const main = page.getByRole('main')
  const dialog = page.getByRole('dialog', { name: cancelCopy.dialogTitle })
  return {
    heading: (title) => page.getByRole('heading', { level: 1, name: title }),
    trigger: main.getByRole('button', { name: cancelCopy.trigger, exact: true }).first(),
    dialog,
    reason: dialog.getByRole('textbox', { name: cancelCopy.reason }),
    confirm: dialog.getByRole('button', { name: cancelCopy.confirm, exact: true }),
    keep: dialog.getByRole('button', { name: cancelCopy.keep, exact: true }),
    history: page.getByRole('region', { name: jobDetailCopy.history }).getByRole('listitem'),
  }
}

function pill(page: Page): Locator {
  return page.getByRole('banner').getByText(liveCopy.pillAnyState)
}

function pageAlert(page: Page, title: string): Locator {
  return page.getByRole('main').getByRole('alert').filter({ hasText: title })
}

async function precedes(first: Locator, second: Locator): Promise<boolean> {
  const other = await second.elementHandle()
  return first.evaluate(
    (element, target) =>
      target !== null &&
      Boolean(element.compareDocumentPosition(target) & Node.DOCUMENT_POSITION_FOLLOWING),
    other,
  )
}

async function capture(page: Page, testInfo: TestInfo, name: string): Promise<void> {
  const path = testInfo.outputPath(`${name}.png`)
  await page.screenshot({ path, fullPage: true })
  await testInfo.attach(name, { path, contentType: 'image/png' })
}

function escapeForPattern(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\/]/g, '\\$&')
}

function statusRowDescription(
  job: Pick<JobDetails, 'number' | 'title'>,
  status: string,
  technician: string | null,
): RegExp {
  const technicianPart = technician === null ? '' : `\\s*${escapeForPattern(technician)}`
  return new RegExp(
    `^JOB-${job.number}\\s*,\\s*${escapeForPattern(job.title)}\\s*${escapeForPattern(status)}${technicianPart}\\s*${escapeForPattern(cancelCopy.consequence(technician))}$`,
  )
}

function cancelledEntries(job: JobDetails): JobDetails['history'] {
  return job.history.filter((entry) => entry.action === 'Cancelled')
}

test.describe('E2E-13 cancel a job (W6-AC4)', () => {
  test.use({
    expectedConsole: anyOf(
      consolePatterns.cancellationConflict,
      consolePatterns.cancellationRejected,
    ),
  })

  test('the cancel dialog shows the status row and keeps the reason, ignores the backdrop, and answers a concurrency conflict, an already cancelled job, a completed job, a rejected reason and a cancellation, which leaves the page read-only with its notice', async ({
    adminPage: page,
    adminSession,
    api,
    guards,
    technician,
    runId,
    sentinelSink,
  }, testInfo) => {
    const admin = adminSession.user.displayName
    const lenaApi = await technician(demoTechnicians.assignments.email)
    const priyaApi = await technician(demoTechnicians.inspections.email)
    const lena = lenaApi.user
    const priya = priyaApi.user
    const reasons = {
      first: `Customer moved the visit to next month. E2E ${runId} first`,
      other: `Site closed for renovation until spring. E2E ${runId} other`,
      completed: `Duplicate of another visit. E2E ${runId} completed`,
      final: `Tenant cancelled the service contract. E2E ${runId} final`,
    }
    for (const reason of Object.values(reasons)) {
      sentinelSink('cancellation reason', reason)
    }
    const conflictJob = await createAssignedJob(api, runId, 'cancel conflict', lena.id)
    const runningJob = await createAssignedJob(api, runId, 'cancel completed', lena.id)
    await lenaApi.start(runningJob.id)
    const unassignedJob = await createUnassignedJob(api, runId, 'cancel unassigned')
    const ui = cancelPage(page)
    const gate = new SocketGate()
    await gate.install(page, guards)

    await page.goto(`/jobs/${conflictJob.id}`)

    await expect(ui.heading(conflictJob.title)).toBeVisible()
    await expect(pill(page)).toHaveText(liveCopy.pillLive, { timeout: 10_000 })
    await ui.trigger.click()
    await expect(ui.dialog).toBeVisible()
    await expect(ui.reason).toBeFocused()
    await expect(ui.dialog).toHaveAccessibleDescription(
      statusRowDescription(conflictJob, statusLabels.Assigned, lena.displayName),
    )
    await expect(
      ui.dialog.getByText(cancelCopy.consequence(lena.displayName), { exact: true }),
    ).toBeVisible()
    expect(await gate.drop()).toBe(1)
    const moved = await api.assignJob(conflictJob.id, priya.id, conflictJob.version)
    await ui.reason.fill(reasons.first)
    const refused = page.waitForResponse((response) =>
      isCancellation(response.request(), conflictJob.id),
    )
    await ui.confirm.click()

    const conflict = await refused
    expect(conflict.status()).toBe(409)
    expect(((await conflict.json()) as { code?: string }).code).toBe('concurrency.conflict')
    const changed = ui.dialog
      .getByRole('alert')
      .filter({ hasText: cancelCopy.changed(`JOB-${conflictJob.number}`) })
    await expect(changed).toContainText(cancelCopy.changedMessage)
    await expect(ui.dialog).toHaveAccessibleDescription(
      statusRowDescription(conflictJob, statusLabels.Assigned, priya.displayName),
    )
    await expect(
      ui.dialog.getByText(cancelCopy.consequence(priya.displayName), { exact: true }),
    ).toBeVisible()
    await expect(ui.reason).toHaveValue(reasons.first)
    await expect(ui.confirm).toBeFocused()
    await audit(page, testInfo, runId, 'cancel dialog after a concurrency conflict 1280')
    await capture(page, testInfo, 'w6-cancel-conflict-1280')

    const elsewhere = await api.cancelJob(conflictJob.id, reasons.other, moved.version)
    expect(elsewhere.status).toBe('Cancelled')
    await page.setViewportSize({ width: 375, height: 800 })
    const answered = page.waitForResponse((response) =>
      isCancellation(response.request(), conflictJob.id),
    )
    await ui.confirm.click()

    expect((await answered).status()).toBe(200)
    await expect(ui.dialog).toHaveCount(0)
    const already = pageAlert(page, cancelCopy.alreadyCancelled(`JOB-${conflictJob.number}`))
    await expect(already).toContainText(cancelCopy.alreadyCancelledMessage)
    const notice = page.getByRole('main').getByText(cancelCopy.notice(admin))
    await expect(notice).toBeVisible()
    await expect(already).toBeInViewport()
    expect(await precedes(already, notice), 'the conflict banner comes before the notice').toBe(
      true,
    )
    await expect(
      page.getByRole('main').getByText(historyCopy.note(reasons.other), { exact: true }).first(),
    ).toBeVisible()
    await expect(page.getByRole('main').getByText(reasons.first)).toHaveCount(0)
    await expect(ui.heading(conflictJob.title)).toBeFocused()
    await expect(ui.trigger).toHaveCount(0)
    await audit(page, testInfo, runId, 'job already cancelled elsewhere 375')
    await capture(page, testInfo, 'w6-cancel-already-cancelled-375')
    await page.setViewportSize({ width: 1280, height: 800 })
    const storedConflict = await api.getJob(conflictJob.id)
    expect(storedConflict.status).toBe('Cancelled')
    expect(cancelledEntries(storedConflict).map((entry) => entry.note)).toEqual([reasons.other])
    const priyaView = await priyaApi.readJob(conflictJob.id)
    expect(priyaView.status).toBe('Cancelled')
    expect(priyaView.affordances.allowedStatusChanges).toEqual([])
    expect(priyaView.affordances.canCancel).toBe(false)
    const refusedStart = await priyaApi.updateStatus(conflictJob.id, {
      status: 'InProgress',
      note: null,
      version: priyaView.version,
    })
    expect(refusedStart.status()).toBe(409)
    expect(((await refusedStart.json()) as { code?: string }).code).toBe('job.invalid-transition')

    await openInApp(page, `/jobs/${runningJob.id}`)
    await expect(ui.heading(runningJob.title)).toBeVisible()
    await expect(page.locator('.page-header__meta')).toContainText(statusLabels.InProgress)
    const completed = await completeJob(lenaApi, runningJob.id)
    expect(completed.status).toBe('Completed')
    await ui.trigger.click()
    await ui.reason.fill(reasons.completed)
    const refusedCompleted = page.waitForResponse((response) =>
      isCancellation(response.request(), runningJob.id),
    )
    await ui.confirm.click()

    const invalid = await refusedCompleted
    expect(invalid.status()).toBe(409)
    expect(((await invalid.json()) as { code?: string }).code).toBe('job.invalid-transition')
    await expect(ui.dialog).toHaveCount(0)
    await expect(
      pageAlert(page, cancelCopy.notCancellable(`JOB-${runningJob.number}`)),
    ).toContainText(cancelCopy.notCancellableMessage)
    await expect(page.locator('.page-header__meta')).toContainText(statusLabels.Completed)
    await expect(page.getByRole('link', { name: reportCopy.reportLink })).toBeVisible()
    await expect(ui.trigger).toHaveCount(0)
    await expect(ui.heading(runningJob.title)).toBeFocused()
    const storedCompleted = await api.getJob(runningJob.id)
    expect(storedCompleted.status).toBe('Completed')
    expect(cancelledEntries(storedCompleted)).toEqual([])
    gate.allow()
    await expect(pill(page)).toHaveText(liveCopy.pillLive, { timeout: 12_000 })

    const sent = trackRequests(page, (request) => isCancellation(request, unassignedJob.id))
    await openInApp(page, `/jobs/${unassignedJob.id}`)
    await expect(ui.heading(unassignedJob.title)).toBeVisible()
    await expect(page.getByRole('region', { name: assignCopy.heading })).toBeVisible()
    await ui.trigger.click()
    await expect(ui.reason).toBeFocused()
    await expect(ui.reason).toHaveAccessibleDescription(cancelCopy.reasonHelp)
    await expect(ui.dialog).toHaveAccessibleDescription(
      statusRowDescription(unassignedJob, statusLabels.Unassigned, null),
    )
    await expect(ui.dialog.getByText(cancelCopy.rule, { exact: true })).toBeVisible()
    await audit(page, testInfo, runId, 'cancel dialog 1280')
    await capture(page, testInfo, 'w6-cancel-dialog-1280')
    await ui.confirm.click()
    await expect(ui.reason).toHaveAccessibleDescription(
      new RegExp(escapeForPattern(cancelCopy.blankReason)),
    )
    await expect(ui.reason).toHaveAttribute('aria-invalid', 'true')
    await expect(ui.reason).toBeFocused()
    expect(sent, 'requests sent with a blank reason').toHaveLength(0)
    await audit(page, testInfo, runId, 'cancel dialog with a blank reason 1280')

    await ui.reason.fill(reasons.final)
    const box = await ui.dialog.boundingBox()
    expect(box, 'the dialog box').not.toBeNull()
    await page.mouse.click(Math.max((box?.x ?? 0) / 2, 4), 8)
    await expect(ui.dialog).toBeVisible()
    await expect(ui.reason).toHaveValue(reasons.final)
    await ui.keep.click()
    await expect(ui.dialog).toHaveCount(0)
    await expect(ui.trigger).toBeFocused()
    await ui.trigger.click()
    await expect(ui.reason).toHaveValue(reasons.final)
    await page.keyboard.press('Escape')
    await expect(ui.dialog).toHaveCount(0)
    await expect(ui.trigger).toBeFocused()
    await ui.trigger.click()
    await expect(ui.reason).toHaveValue(reasons.final)
    await page.setViewportSize({ width: 375, height: 812 })
    await audit(page, testInfo, runId, 'cancel dialog with a reason 375')
    await capture(page, testInfo, 'w6-cancel-dialog-375')
    await page.setViewportSize({ width: 1280, height: 800 })
    expect(sent, 'requests sent before a confirmation').toHaveLength(0)

    await ui.reason.fill(`${reasons.final}${bell}`)
    const rejected = page.waitForResponse((response) =>
      isCancellation(response.request(), unassignedJob.id),
    )
    await ui.confirm.click()

    const badRequest = await rejected
    expect(badRequest.status()).toBe(400)
    expect(await badRequest.json()).toMatchObject({
      code: 'request.validation-failed',
      errors: { reason: [cancelCopy.controlCharacters] },
    })
    await expect(ui.reason).toHaveAccessibleDescription(
      new RegExp(escapeForPattern(cancelCopy.controlCharacters)),
    )
    await expect(ui.reason).toBeFocused()
    await expect(ui.dialog).toBeVisible()

    await ui.reason.fill(reasons.final)
    let sentBody: unknown = null
    await page.route(
      `**/api/jobs/${unassignedJob.id}/cancellation`,
      async (route) => {
        sentBody = route.request().postDataJSON()
        await route.continue()
      },
      { times: 1 },
    )
    const accepted = page.waitForResponse((response) =>
      isCancellation(response.request(), unassignedJob.id),
    )
    await ui.confirm.click()

    const done = await accepted
    expect(done.status()).toBe(200)
    expect(sentBody).toEqual({ reason: reasons.final, version: unassignedJob.version })
    await expect(ui.dialog).toHaveCount(0)
    await expect(page.getByRole('main').getByText(cancelCopy.notice(admin))).toBeVisible()
    await expect(ui.heading(unassignedJob.title)).toBeFocused()
    await expect(
      page
        .getByRole('main')
        .getByRole('status')
        .filter({ hasText: cancelCopy.announcement(`JOB-${unassignedJob.number}`) }),
    ).toBeAttached()
    await expect(page.locator('.page-header__meta')).toContainText(statusLabels.Cancelled)
    await expect(ui.trigger).toHaveCount(0)
    await expect(page.getByRole('region', { name: assignCopy.heading })).toHaveCount(0)
    await expect(page.getByText(cancelCopy.liveLine)).toHaveCount(0)
    await expect(ui.history.last()).toContainText(cancelCopy.history(admin))
    await expect(ui.history.last()).toContainText(historyCopy.note(reasons.final))
    await delay(1_500)
    await expect(
      page
        .getByRole('region', { name: shellCopy.notifications })
        .getByText(`JOB-${unassignedJob.number}`),
    ).toHaveCount(0)
    await audit(page, testInfo, runId, 'cancelled job page 1280')
    await capture(page, testInfo, 'w6-cancelled-job-1280')
    const stored = await api.getJob(unassignedJob.id)
    expect(stored).toMatchObject({ status: 'Cancelled' })
    expect(stored.cancelledAt).not.toBeNull()
    expect(stored.affordances).toMatchObject({
      allowedStatusChanges: [],
      canAssign: false,
      canCancel: false,
    })
    expect(cancelledEntries(stored)).toEqual([
      expect.objectContaining({
        fromStatus: 'Unassigned',
        toStatus: 'Cancelled',
        note: reasons.final,
        actor: expect.objectContaining({ id: adminSession.user.id }),
      }),
    ])
    expect(sent, 'cancellation requests for the unassigned job').toHaveLength(2)
  })
})
