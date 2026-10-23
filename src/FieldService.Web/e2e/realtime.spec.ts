import { randomUUID } from 'node:crypto'
import { setTimeout as delay } from 'node:timers/promises'

import type { Locator, Page, Request, TestInfo } from '@playwright/test'

import { expect, nothingExpected, PageGuards, test } from './fixtures.ts'
import { demoTechnicians, type JobDetails, type JobStatus } from './support/api.ts'
import { jobsCopy, liveCopy, shellCopy, statusLabels } from './support/copy.ts'
import { createAssignedJob, holdNoteFor } from './support/jobs.ts'
import { handshakeUrlProblem, SocketGate } from './support/websocket.ts'

const liveBudgetMilliseconds = 5_000

const bannerDelayMilliseconds = 3_000

const dropHoldMilliseconds = 5_000

const backOnlineMilliseconds = 8_000

const pausedAfterMilliseconds = 30_000

const reconnectWithinMilliseconds = 12_000

const markerName = '__e2eLiveMarker'

const seenName = '__e2eSeenTexts'

const reconnectingLead = 'Reconnecting to live updates.'

const backOnlineLead = 'Back online.'

function pill(page: Page): Locator {
  return page.getByRole('banner').getByText(liveCopy.pillAnyState)
}

function connectionNotice(page: Page): Locator {
  return page.getByRole('main').getByRole('status').first()
}

function notifications(page: Page): Locator {
  return page.getByRole('region', { name: shellCopy.notifications })
}

function rowOf(page: Page, job: JobDetails): Locator {
  return page
    .getByRole('region', { name: jobsCopy.table })
    .locator('tbody tr')
    .filter({ has: page.getByRole('link', { name: job.title, exact: true }) })
}

function jobNumberOf(job: JobDetails): string {
  return `JOB-${job.number}`
}

function isJobListRequest(request: Request): boolean {
  return request.method() === 'GET' && new URL(request.url()).pathname === '/api/jobs'
}

function trackJobListRequests(page: Page): number[] {
  const times: number[] = []
  page.on('request', (request) => {
    if (isJobListRequest(request)) {
      times.push(Date.now())
    }
  })
  return times
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

async function recordTextAppearances(page: Page, texts: readonly string[]): Promise<void> {
  await page.evaluate(
    ([name, watched]) => {
      const seen: Record<string, number> = {}
      ;(window as unknown as Record<string, Record<string, number>>)[name] = seen
      const check = (): void => {
        const text = document.querySelector('main')?.textContent ?? ''
        for (const candidate of watched) {
          if (!(candidate in seen) && text.includes(candidate)) {
            seen[candidate] = Date.now()
          }
        }
      }
      new MutationObserver(check).observe(document.body, {
        subtree: true,
        childList: true,
        characterData: true,
      })
      check()
    },
    [seenName, texts] as const,
  )
}

async function firstSeen(page: Page, text: string): Promise<number | undefined> {
  return page.evaluate(
    ([name, candidate]) =>
      (window as unknown as Record<string, Record<string, number> | undefined>)[name]?.[candidate],
    [seenName, text] as const,
  )
}

function announcementOf(sentences: string, toastNote: string | null): string {
  return toastNote === null ? sentences : `${sentences} ${toastNote}`
}

async function expectToastNote(page: Page, toastNote: string | null): Promise<void> {
  if (toastNote === null) {
    return
  }
  await expect(notifications(page).getByText(toastNote, { exact: true })).toBeVisible()
}

async function capture(page: Page, testInfo: TestInfo, name: string): Promise<void> {
  const path = testInfo.outputPath(`${name}.png`)
  await page.screenshot({ path })
  await testInfo.attach(name, { path, contentType: 'image/png' })
}

function note(testInfo: TestInfo, type: string, description: string): void {
  testInfo.annotations.push({ type, description })
}

async function openLiveJobsPage(page: Page, job: JobDetails, status: JobStatus): Promise<void> {
  await page.goto('/jobs')
  await expect(page.getByRole('heading', { level: 1, name: jobsCopy.heading })).toBeVisible()
  await expect(pill(page)).toHaveText(liveCopy.pillLive, { timeout: 10_000 })
  await expect(rowOf(page, job)).toContainText(statusLabels[status])
}

test.describe('E2E-08 live status updates (W4-AC4)', () => {
  test('with the jobs page open, the technician starting, holding and resuming a job reaches its row within 5 seconds with a toast, a row flash and no reload', async ({
    adminPage: page,
    api,
    guards,
    technician,
    runId,
    sentinelSink,
  }, testInfo) => {
    const lenaApi = await technician(demoTechnicians.assignments.email)
    const lena = lenaApi.user
    const holdNote = holdNoteFor(runId)
    sentinelSink('hold note', holdNote)
    const job = await createAssignedJob(api, runId, 'live status', lena.id)
    const row = rowOf(page, job)
    const jobNumber = jobNumberOf(job)

    await openLiveJobsPage(page, job, 'Assigned')
    await expect(pill(page)).toHaveText(/^Live\b.*\bupdated \d{1,2}:\d{2} [AP]M$/)
    await expect(connectionNotice(page)).toHaveText('')
    expect(guards.handshakesChecked(), 'the live handshake URL was checked').toBeGreaterThan(0)
    await setMarker(page, runId)
    await capture(page, testInfo, 'w4-live-pill-jobs-1280')

    const steps = [
      {
        name: 'start',
        change: () => lenaApi.start(job.id),
        status: 'InProgress',
        title: liveCopy.started(lena.displayName),
        note: null,
      },
      {
        name: 'hold',
        change: () => lenaApi.hold(job.id, holdNote),
        status: 'OnHold',
        title: liveCopy.putOnHold(lena.displayName),
        note: `“${holdNote}”`,
      },
      {
        name: 'resume',
        change: () => lenaApi.resume(job.id),
        status: 'InProgress',
        title: liveCopy.resumed(lena.displayName),
        note: null,
      },
    ] as const

    for (const step of steps) {
      const changed = await step.change()
      const committedAt = Date.now()
      expect(changed.status).toBe(step.status)
      const label = statusLabels[step.status]

      await expect(row, `${step.name}: the row shows ${label}`).toContainText(label, {
        timeout: liveBudgetMilliseconds,
      })
      const rowLatency = Date.now() - committedAt
      await expect(row, `${step.name}: the row flashes`).toHaveClass(/data-table__row--updated/, {
        timeout: 1_000,
      })
      await expect(row).toContainText(liveCopy.justNow)
      const toastTitle = notifications(page).getByText(step.title, { exact: true })
      await expect(toastTitle, `${step.name}: the toast shows`).toBeVisible({
        timeout: Math.max(liveBudgetMilliseconds - (Date.now() - committedAt), 1),
      })
      const toastLatency = Date.now() - committedAt
      const body = liveCopy.body(job.title, label)
      await expect(notifications(page).getByText(body, { exact: true }).last()).toBeVisible()
      await expect(
        notifications(page)
          .getByText(new RegExp(`^${jobNumber}\\b`))
          .last(),
      ).toBeVisible()
      await expectToastNote(page, step.note)
      await expect(notifications(page).getByRole('status')).toContainText(
        announcementOf(liveCopy.announcement(jobNumber, step.title, body), step.note),
      )
      expect(Math.max(rowLatency, toastLatency)).toBeLessThanOrEqual(liveBudgetMilliseconds)
      note(testInfo, `${step.name} latency`, `row ${rowLatency} ms, toast ${toastLatency} ms`)
      expect(await markerOf(page), `${step.name}: no reload`).toBe(runId)
      await capture(page, testInfo, `w4-${step.name}-toast-and-row-flash-1280`)
    }

    await expect(page.getByText(jobsCopy.loadFailed)).toHaveCount(0)
    const stored = await api.getJob(job.id)
    expect(stored.history.map((entry) => entry.action)).toEqual([
      'Created',
      'Assigned',
      'Started',
      'PutOnHold',
      'Resumed',
    ])
  })
})

test.describe('E2E-09 socket drop, reconnect and refetch (W4-AC5)', () => {
  test('a socket drop held for at least 5 seconds shows Reconnecting after 3 seconds; a hold missed while down is refetched on reconnect with Back online and without a reload', async ({
    adminPage: page,
    api,
    guards,
    technician,
    runId,
    sentinelSink,
  }, testInfo) => {
    const lenaApi = await technician(demoTechnicians.assignments.email)
    const lena = lenaApi.user
    const holdNote = holdNoteFor(runId)
    sentinelSink('hold note', holdNote)
    const job = await createAssignedJob(api, runId, 'socket drop', lena.id)
    await lenaApi.start(job.id)
    const gate = new SocketGate()
    await gate.install(page, guards)
    const listRequests = trackJobListRequests(page)
    const row = rowOf(page, job)

    await openLiveJobsPage(page, job, 'InProgress')
    await setMarker(page, runId)
    await recordTextAppearances(page, [reconnectingLead, backOnlineLead])
    expect(gate.counts().passed).toBe(1)

    const droppedAt = Date.now()
    expect(await gate.drop()).toBe(1)

    await expect(pill(page)).toHaveText(liveCopy.pillReconnecting, { timeout: 1_000 })
    await expect(connectionNotice(page)).toHaveText('')
    const held = await lenaApi.hold(job.id, holdNote)
    expect(held.status).toBe('OnHold')
    await expect(connectionNotice(page)).toHaveText(liveCopy.reconnectingBanner, {
      timeout: bannerDelayMilliseconds + 2_000,
    })
    const bannerAt = await firstSeen(page, reconnectingLead)
    expect(bannerAt).toBeDefined()
    expect((bannerAt ?? 0) - droppedAt).toBeGreaterThanOrEqual(bannerDelayMilliseconds)
    await expect(row).toContainText(statusLabels.InProgress)
    await delay(Math.max(dropHoldMilliseconds - (Date.now() - droppedAt), 0))
    await expect(connectionNotice(page)).toHaveText(liveCopy.reconnectingBanner)
    await expect(row).toContainText(statusLabels.InProgress)
    await capture(page, testInfo, 'w4-reconnecting-banner-1280')

    const restoredAt = Date.now()
    gate.allow()

    await expect(connectionNotice(page)).toHaveText(liveCopy.backOnlineBanner, {
      timeout: reconnectWithinMilliseconds,
    })
    const backOnlineAt = await firstSeen(page, backOnlineLead)
    await expect(pill(page)).toHaveText(liveCopy.pillLive)
    await expect(row).toContainText(statusLabels.OnHold)
    expect(listRequests.some((at) => at >= restoredAt)).toBe(true)
    expect(await markerOf(page), 'no reload').toBe(runId)
    await expect(
      notifications(page).getByText(liveCopy.putOnHold(lena.displayName)),
      'the missed event shows no toast',
    ).toHaveCount(0)
    await capture(page, testInfo, 'w4-back-online-1280')
    const counts = gate.counts()
    expect(counts.passed).toBeGreaterThanOrEqual(2)
    note(
      testInfo,
      'drop',
      `banner after ${(bannerAt ?? 0) - droppedAt} ms; drop held ${restoredAt - droppedAt} ms; back online ${(backOnlineAt ?? 0) - restoredAt} ms after the gate opened; ${counts.refused} refused attempts`,
    )

    await expect(connectionNotice(page)).toHaveText('', {
      timeout: backOnlineMilliseconds + 2_000,
    })
    expect(Date.now() - (backOnlineAt ?? 0)).toBeGreaterThanOrEqual(backOnlineMilliseconds - 500)
  })
})

test.describe('E2E-10 live updates paused (W4-AC5)', () => {
  test('with every socket refused, the backoff leads to Live updates paused after 30 seconds with Refresh now, and Refresh now reconnects and refetches once connections are allowed', async ({
    adminPage: page,
    guards,
  }, testInfo) => {
    test.setTimeout(testInfo.timeout + pausedAfterMilliseconds + 15_000)
    const gate = new SocketGate()
    gate.refuse()
    await gate.install(page, guards)
    const listRequests = trackJobListRequests(page)

    const openedAt = Date.now()
    await page.goto('/jobs')

    await expect(page.getByRole('heading', { level: 1, name: jobsCopy.heading })).toBeVisible()
    await expect(pill(page)).toHaveText(
      new RegExp(`^(?:${liveCopy.pillConnecting}|${liveCopy.pillReconnecting})$`),
    )
    await expect(connectionNotice(page)).toHaveText(liveCopy.reconnectingBanner, {
      timeout: bannerDelayMilliseconds + 3_000,
    })
    await expect(pill(page)).toHaveText(liveCopy.pillPaused, {
      timeout: pausedAfterMilliseconds + 5_000,
    })
    const pausedAfter = Date.now() - openedAt
    expect(pausedAfter).toBeGreaterThanOrEqual(pausedAfterMilliseconds)
    await expect(connectionNotice(page).getByRole('paragraph')).toHaveText(liveCopy.pausedBanner)
    const refreshNow = connectionNotice(page).getByRole('button', { name: liveCopy.refreshNow })
    await expect(refreshNow).toBeVisible()
    expect(gate.counts().passed).toBe(0)
    expect(gate.counts().refused).toBeGreaterThan(1)
    await capture(page, testInfo, 'w4-paused-banner-1280')

    const clickedAt = Date.now()
    gate.allowFrom(clickedAt + 150)
    await refreshNow.click()

    await expect(pill(page)).toHaveText(liveCopy.pillLive, { timeout: 10_000 })
    expect(gate.counts().passed).toBe(1)
    expect(listRequests.some((at) => at >= clickedAt)).toBe(true)
    await expect(connectionNotice(page)).not.toHaveText(new RegExp(`^${liveCopy.pausedLead}`))
    note(
      testInfo,
      'paused',
      `paused ${pausedAfter} ms after the page opened; ${gate.counts().refused} refused attempts before Refresh now`,
    )
  })
})

test.describe('SEC-W4-05 handshake URLs carry no credential', () => {
  test('a Socket.IO handshake URL may hold only the EIO and transport query keys, and a breach fails the guards naming keys, never values', () => {
    const origin = 'http://127.0.0.1:5173'
    const handshake = `${origin}/socket.io/?EIO=4&transport=websocket`
    const value = `sentinel-${randomUUID()}`
    const checker = new PageGuards(origin, nothingExpected, nothingExpected)

    const named = handshakeUrlProblem(`${handshake}&ticket=${value}`)
    const bare = handshakeUrlProblem(`${handshake}&${value}`)
    const repeated = handshakeUrlProblem(`${handshake}&transport=polling`)
    const fragment = handshakeUrlProblem(`${handshake}#${value}`)
    checker.checkSocketUrl(handshake)
    checker.checkSocketUrl(`${origin}/api/jobs?search=${value}`)
    checker.checkSocketUrl(`${handshake}&auth=${value}`)

    expect(handshakeUrlProblem(handshake)).toBeNull()
    expect(handshakeUrlProblem(`${origin}/api/jobs?ticket=${value}`)).toBeNull()
    expect(named).toContain('EIO, transport, ticket')
    expect(bare).toContain(`a key of ${value.length} characters`)
    expect(repeated).toContain('EIO, transport, transport')
    expect(fragment).toContain('a fragment')
    for (const problem of [named, bare, repeated, fragment]) {
      expect(problem).not.toBeNull()
      expect(problem).not.toContain(value)
    }
    expect(checker.handshakesChecked()).toBe(2)
    expect(checker.failures()).toHaveLength(1)
    expect(checker.failures()[0]).toContain('EIO, transport, auth')
    expect(checker.failures().join('\n')).not.toContain(value)
  })
})
