import { setTimeout as delay } from 'node:timers/promises'

import type { Locator, Page } from '@playwright/test'

import { anyOf, consolePatterns, expect, stubSignedOutRestore, test } from './fixtures.ts'
import { audit } from './support/axe.ts'
import { dashboardCopy, shellCopy, signInCopy, toastCopy } from './support/copy.ts'
import { isClientErrorReport, trackRequests } from './support/dashboard.ts'
import { eventName, waitForApiLogRecords } from './support/logs.ts'

const errorName = '__e2eClientError'

const settleMilliseconds = 1_000

const referencePattern = /^[A-Za-z0-9._-]{1,64}$/

function notifications(page: Page): Locator {
  return page.getByRole('region', { name: shellCopy.notifications })
}

function errorToasts(page: Page): Locator {
  return notifications(page).getByRole('alert').filter({ hasText: toastCopy.errorTitle })
}

async function rejectNewError(page: Page, message: string): Promise<void> {
  await page.evaluate(
    ([name, text]) => {
      const error = new Error(text)
      ;(window as unknown as Record<string, Error>)[name] = error
      void Promise.reject(error)
    },
    [errorName, message] as const,
  )
}

async function rejectSameError(page: Page): Promise<void> {
  await page.evaluate((name) => {
    void Promise.reject((window as unknown as Record<string, Error>)[name])
  }, errorName)
}

test.describe('E2E-17 client error reporting (W6-AC5)', () => {
  test('an error raised in the signed-in page shows a toast whose reference is the CorrelationId of the client-error.reported Warning in logs/api and never its text, a repeat adds no toast and no report, and signed out the toast has no reference and nothing is sent', async ({
    adminPage: page,
    adminSession,
    anonymousPage,
    guards,
    runId,
    sentinelSink,
  }, testInfo) => {
    const marker = `E2E ${runId} client error`
    const email = `client.error.${runId}@fieldservice.example`
    sentinelSink('email in a client error', email)
    guards.allowPageErrors(consolePatterns.reportedError(marker))
    guards.allowConsole(
      anyOf(consolePatterns.reportedError(marker), consolePatterns.anonymousRefresh),
    )
    const reports = trackRequests(page, isClientErrorReport)
    await page.goto('/')
    await expect(
      page.getByRole('heading', {
        level: 1,
        name: dashboardCopy.greeting(adminSession.user.displayName),
      }),
    ).toBeVisible()
    let sent: Record<string, unknown> | null = null
    await page.route(
      '**/api/client-errors',
      async (route) => {
        sent = route.request().postDataJSON() as Record<string, unknown>
        await route.continue()
      },
      { times: 1 },
    )
    const accepted = page.waitForResponse((response) => isClientErrorReport(response.request()))

    await rejectNewError(page, `${marker} for ${email}`)

    const response = await accepted
    expect(response.status()).toBe(202)
    expect(response.headers()['content-length'] ?? '0', 'the 202 response has no body').toBe('0')
    const reference = response.headers()['x-correlation-id'] ?? ''
    expect(reference).toMatch(referencePattern)
    await expect(errorToasts(page)).toHaveCount(1)
    await expect(errorToasts(page)).toContainText(toastCopy.errorBody)
    const referenceLine = notifications(page).getByText(`${toastCopy.reference} ${reference}`, {
      exact: true,
    })
    await expect(referenceLine).toBeVisible()
    await expect(errorToasts(page).getByText(toastCopy.reference)).toHaveCount(0)
    expect(
      (await notifications(page).textContent())?.includes(marker),
      'the error message is shown in the toast',
    ).toBe(false)
    expect(sent, 'the report body').toMatchObject({
      source: 'window.unhandledrejection',
      route: '/',
      correlationId: null,
    })
    expect(JSON.stringify(sent).includes(email), 'the email left the browser').toBe(false)
    await audit(page, testInfo, runId, 'error toast with its reference 1280')

    const records = await waitForApiLogRecords(
      reference,
      (record) =>
        eventName(record) === 'client-error.reported' && record.CorrelationId === reference,
    )
    expect(
      records.map(eventName),
      'client-error.reported events with the toast reference, by event name only',
    ).toHaveLength(1)
    const logged = records[0] ?? {}
    expect(logged['@l']).toBe('Warning')
    expect(logged.Source).toBe('web')
    expect(logged.UserId).toBe(adminSession.user.id)
    expect(logged.ErrorSource).toBe('window.unhandledrejection')
    expect(logged.Route).toBe('/')
    expect(logged.ReportedCorrelationId).toBeNull()
    expect(
      JSON.stringify(logged).includes(email),
      'the email reached the client-error.reported event',
    ).toBe(false)
    testInfo.annotations.push({
      type: 'client error reference',
      description: `toast ${reference}; Warning CorrelationId ${String(logged.CorrelationId)}; Source ${String(logged.Source)}`,
    })

    await rejectSameError(page)
    await delay(settleMilliseconds)
    await expect(errorToasts(page)).toHaveCount(1)
    expect(reports, 'reports after the repeat').toHaveLength(1)
    await errorToasts(page).getByRole('button', { name: toastCopy.dismiss }).click()
    await expect(errorToasts(page)).toHaveCount(0)
    await rejectSameError(page)
    await delay(settleMilliseconds)
    await expect(errorToasts(page)).toHaveCount(0)
    expect(reports, 'reports after a repeat of a dismissed error').toHaveLength(1)

    await stubSignedOutRestore(anonymousPage)
    const anonymousReports = trackRequests(anonymousPage, isClientErrorReport)
    await anonymousPage.goto('/sign-in')
    await expect(
      anonymousPage.getByRole('heading', { level: 1, name: signInCopy.heading }),
    ).toBeVisible()

    await rejectNewError(anonymousPage, `${marker} signed out`)

    await expect(errorToasts(anonymousPage)).toHaveCount(1)
    await delay(settleMilliseconds)
    await expect(notifications(anonymousPage).getByText(toastCopy.reference)).toHaveCount(0)
    expect(anonymousReports, 'reports sent while signed out').toHaveLength(0)
    await audit(anonymousPage, testInfo, runId, 'error toast while signed out 1280')
  })
})
