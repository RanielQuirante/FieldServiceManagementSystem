import { anyOf, consolePatterns, expect, test } from './fixtures.ts'
import {
  demoAccounts,
  demoPassword,
  readProblem,
  readSession,
  refreshCookieName,
  refreshWithCookie,
  signIn,
  type SessionResponse,
} from './support/api.ts'
import { dashboardCopy, documentTitle, jobsCopy, shellCopy, signInCopy } from './support/copy.ts'
import { openInApp } from './support/run.ts'

test.describe('E2E-01 administrator sign-in and route protection', () => {
  test.use({ expectedConsole: consolePatterns.anonymousRefresh })

  test('signed out, each protected route redirects to sign-in with its path', async ({
    anonymousPage: page,
    api,
  }) => {
    const job = await api.findJobByNumber(1042)

    await page.goto('/jobs')

    await expect(page).toHaveURL('/sign-in?redirect=%2Fjobs')
    await expect(page.getByRole('heading', { level: 1, name: signInCopy.heading })).toBeVisible()
    await expect(page).toHaveTitle(documentTitle(signInCopy.heading))
    await openInApp(page, '/jobs/new')
    await expect(page).toHaveURL('/sign-in?redirect=%2Fjobs%2Fnew')
    await openInApp(page, `/jobs/${job.id}`)
    await expect(page).toHaveURL(`/sign-in?redirect=%2Fjobs%2F${job.id}`)
    await openInApp(page, '/')
    await expect(page).toHaveURL('/sign-in?redirect=%2F')
    await openInApp(page, '/technicians?shift=off-shift')
    await expect(page).toHaveURL('/sign-in?redirect=%2Ftechnicians%3Fshift%3Doff-shift')
  })

  test('signing in through the form without a redirect lands on the dashboard at / and a reload keeps the session through the cookie', async ({
    anonymousPage: page,
    sentinelSink,
  }) => {
    await page.goto('/sign-in')
    await expect(page).toHaveURL('/sign-in')
    await page.getByLabel(signInCopy.email, { exact: true }).fill(demoAccounts.administrator)
    await page.getByLabel(signInCopy.password, { exact: true }).fill(demoPassword())
    const signInResponse = page.waitForResponse('**/api/auth/sign-in')

    await page.getByRole('button', { name: signInCopy.submit, exact: true }).click()

    const session = (await (await signInResponse).json()) as SessionResponse
    await expect(page).toHaveURL('/')
    const greeting = page.getByRole('heading', {
      level: 1,
      name: dashboardCopy.greeting(session.user.displayName),
    })
    await expect(greeting).toBeVisible()
    await expect(greeting).toBeFocused()
    await expect(page).toHaveTitle(documentTitle(dashboardCopy.heading))
    await expect(
      page.getByRole('navigation', { name: shellCopy.mainNavigation }).getByRole('link', {
        name: dashboardCopy.heading,
      }),
    ).toHaveAttribute('aria-current', 'page')
    const cookie = (await page.context().cookies()).find((item) => item.name === refreshCookieName)
    expect(cookie?.httpOnly).toBe(true)
    expect(cookie?.sameSite).toBe('Strict')
    expect(cookie?.path).toBe('/api/auth')
    const storage = await page.evaluate(() => ({
      entries: window.localStorage.length + window.sessionStorage.length,
      text: JSON.stringify({ ...window.localStorage, ...window.sessionStorage }),
      cookies: document.cookie,
    }))
    expect(storage.entries).toBe(0)
    expect(storage.text.includes(session.accessToken), 'web storage holds the access token').toBe(
      false,
    )
    expect(storage.cookies.includes(refreshCookieName), 'script can read the refresh cookie').toBe(
      false,
    )

    const restored = page.waitForResponse('**/api/auth/refresh')
    await page.reload()
    const refresh = await restored

    expect(refresh.status()).toBe(200)
    expect(refresh.request().headers()['x-fieldservice-csrf']).toBe('1')
    await expect(page).toHaveURL('/')
    await expect(
      page.getByRole('heading', {
        level: 1,
        name: dashboardCopy.greeting(session.user.displayName),
      }),
    ).toBeVisible()
    await expect(page.getByRole('navigation', { name: shellCopy.mainNavigation })).toContainText(
      session.user.displayName,
    )
    const withoutHeader = await refreshWithCookie(page.request, sentinelSink, {})
    expect(await readProblem(withoutHeader)).toMatchObject({
      status: 403,
      code: 'auth.csrf-required',
    })
  })
})

test.describe('W1-AC1 redirect values after sign-in', () => {
  test('a redirect whose dot segments normalize to another host stays on this origin', async ({
    adminPage: page,
  }) => {
    await page.goto('/sign-in?redirect=%2F..%2F%2Fevil.example')

    await expect(page).toHaveURL('/')
    await openInApp(page, '/sign-in?redirect=%2F.%2F%5Cevil.example')
    await expect(page).toHaveURL('/')
    await openInApp(page, '/sign-in?redirect=%2Fjobs%2F..%2F%2Fevil.example%2Fx')
    await expect(page).toHaveURL('/')
    await openInApp(page, '/sign-in?redirect=%2F%2Fevil.example')
    await expect(page).toHaveURL('/')
    await openInApp(page, '/sign-in?redirect=%2Fjobs')
    await expect(page).toHaveURL('/jobs')
    await openInApp(page, '/sign-in?redirect=%2Fjobs%2Fnew')
    await expect(page).toHaveURL('/jobs/new')
  })
})

test.describe('E2E-02 wrong application refused', () => {
  test.use({
    expectedConsole: anyOf(consolePatterns.anonymousRefresh, consolePatterns.signInRefused),
  })

  test('a technician account is refused by the dashboard and the administrator by the mobile client', async ({
    anonymousPage: page,
    playwright,
    baseURL,
    sentinelSink,
  }) => {
    await page.goto('/sign-in')
    await page.getByLabel(signInCopy.email, { exact: true }).fill(demoAccounts.technician)
    await page.getByLabel(signInCopy.password, { exact: true }).fill(demoPassword())
    const refusal = page.waitForResponse('**/api/auth/sign-in')

    await page.getByRole('button', { name: signInCopy.submit, exact: true }).click()

    const response = await refusal
    expect(response.status()).toBe(403)
    expect(((await response.json()) as { code: string }).code).toBe('auth.wrong-application')
    const alert = page.getByRole('alert')
    await expect(alert).toContainText(signInCopy.technicianAccount)
    await expect(alert).toContainText(signInCopy.technicianApp)
    await expect(page).toHaveURL('/sign-in')
    const request = await playwright.request.newContext({ baseURL })
    const mobile = await signIn(request, demoAccounts.administrator, 'Mobile', sentinelSink)
    expect(await readProblem(mobile)).toMatchObject({ status: 403, code: 'auth.wrong-application' })
    await request.dispose()
  })
})

test.describe('E2E-03 sign-out across tabs', () => {
  test('signing out in one tab returns the other tab to sign-in and the old cookie is refused', async ({
    openContext,
    guards,
    playwright,
    baseURL,
    sentinelSink,
  }) => {
    const context = await openContext()
    await readSession(
      await signIn(context.request, demoAccounts.administrator, 'Web', sentinelSink),
      'The sign-in of the two-tab context',
    )
    const first = await context.newPage()
    const second = await context.newPage()
    await guards.watch(first)
    await guards.watch(second)
    await first.goto('/jobs')
    await expect(first.getByRole('heading', { level: 1, name: jobsCopy.heading })).toBeVisible()
    await second.goto('/jobs')
    await expect(second.getByRole('heading', { level: 1, name: jobsCopy.heading })).toBeVisible()
    const cookieBeforeSignOut = (await context.cookies()).find(
      (item) => item.name === refreshCookieName,
    )?.value
    expect(cookieBeforeSignOut).toBeDefined()

    await first.getByRole('button', { name: shellCopy.signOut }).click()

    await expect(first).toHaveURL('/sign-in')
    await expect(second).toHaveURL('/sign-in?redirect=%2Fjobs')
    await expect(second.getByText(signInCopy.signedOutElsewhere)).toBeVisible()
    const remaining = (await context.cookies()).filter((item) => item.name === refreshCookieName)
    expect(remaining).toHaveLength(0)
    const stale = await playwright.request.newContext({
      baseURL,
      extraHTTPHeaders: { Cookie: `${refreshCookieName}=${String(cookieBeforeSignOut)}` },
    })
    const refused = await refreshWithCookie(stale, sentinelSink)
    expect(await readProblem(refused)).toMatchObject({ status: 401, code: 'auth.session-expired' })
    await stale.dispose()
  })
})
