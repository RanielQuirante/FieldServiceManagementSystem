import { randomUUID } from 'node:crypto'

import type { APIResponse, Page, Response } from '@playwright/test'

import { expect, test } from './fixtures.ts'
import { demoTechnicians, readProblem } from './support/api.ts'
import { consoleSince } from './support/containers.ts'
import { dashboardCopy } from './support/copy.ts'
import {
  doubleEncodedPaths,
  exposedPaths,
  isNginxErrorPage,
  nginxEntries,
  nginxEntryOf,
  probeRequest,
  rootEscapePath,
  serviceLines,
  sightings,
  sourcesHolding,
} from './support/exposure.ts'
import { apiCacheControl, serverSecurityHeaders } from './support/headers.ts'
import {
  directApi,
  directNotifications,
  hostPortOf,
  probeMarker,
  sendRaw,
  upgradeStatus,
} from './support/probes.ts'

const composeOnly = { tag: '@compose-only' } as const

const handshakePath = '/socket.io/?EIO=4&transport=websocket'

const foreignOrigin = 'http://evil.example'

const htmlCacheControl = 'no-cache'

const assetCacheControl = 'public, max-age=31536000, immutable'

const logWaitMilliseconds = 15_000

function collectApiResponses(page: Page): Response[] {
  const responses: Response[] = []
  page.on('response', (response) => {
    if (new URL(response.url()).pathname.startsWith('/api/')) {
      responses.push(response)
    }
  })
  return responses
}

async function loadedFontFamilies(page: Page): Promise<string[]> {
  return page.evaluate(async () => {
    await document.fonts.ready
    return [...document.fonts]
      .filter((face) => face.status === 'loaded')
      .map((face) => face.family.replaceAll('"', '').replaceAll("'", ''))
  })
}

async function fontResources(page: Page): Promise<string[]> {
  return page.evaluate(() =>
    performance
      .getEntriesByType('resource')
      .map((entry) => entry.name)
      .filter((name) => new URL(name).pathname.endsWith('.woff2')),
  )
}

function securityHeadersOf(response: APIResponse): Record<string, string | undefined> {
  const headers = response.headers()
  return Object.fromEntries(Object.keys(serverSecurityHeaders).map((name) => [name, headers[name]]))
}

function hashedScriptOf(html: string): string {
  const match = /<script[^>]+src="(\/assets\/[^"]+\.js)"/.exec(html)
  return match?.[1] ?? 'no hashed script in index.html'
}

test.describe('E2E-16 CSP and headers', () => {
  test('the dashboard is served with the production security headers, self-hosted fonts and no-store API responses', async ({
    adminPage: page,
    baseURL,
  }) => {
    const apiResponses = collectApiResponses(page)
    await page.route(
      (url) => url.pathname === '/',
      (route) => route.continue(),
    )

    const served = await page.goto('/')

    expect(served?.status()).toBe(200)
    const headers = (await served?.allHeaders()) ?? {}
    for (const [name, value] of Object.entries(serverSecurityHeaders)) {
      expect(headers[name], `${name} on /`).toBe(value)
    }
    expect(String(headers.server), 'Server header on /').not.toMatch(/\d/)
    await expect(page).toHaveURL('/')
    await expect(
      page
        .getByRole('region', { name: dashboardCopy.jobsByStatus })
        .getByRole('link', { name: /, \d+(?:, .+)?$/ }),
    ).toHaveCount(6)
    const workloadRows = page.getByRole('table', { name: dashboardCopy.workload }).getByRole('row')
    await expect.poll(() => workloadRows.count()).toBeGreaterThan(1)
    expect(
      apiResponses.map((response) => new URL(response.url()).pathname),
      'the dashboard read its summary and the technicians',
    ).toEqual(expect.arrayContaining(['/api/dashboard/summary', '/api/technicians']))
    const families = await loadedFontFamilies(page)
    expect(families).toEqual(expect.arrayContaining(['Geist', 'Geist Mono']))
    const fonts = await fontResources(page)
    expect(fonts.length).toBeGreaterThan(0)
    const origin = new URL(String(baseURL)).origin
    expect(fonts.filter((url) => new URL(url).origin !== origin)).toEqual([])
    expect(apiResponses.length).toBeGreaterThan(1)
    for (const response of apiResponses) {
      const apiHeaders = await response.allHeaders()
      expect(
        apiHeaders['cache-control'],
        `Cache-Control on ${new URL(response.url()).pathname}`,
      ).toContain(apiCacheControl)
      expect(String(apiHeaders.server), 'Server header on /api').not.toMatch(/\d/)
    }
  })
})

test.describe('E2E-16 nginx headers, caching and access log', composeOnly, () => {
  test('nginx sends the security headers on 200, 304 and 404 answers, caches only hashed assets, and logs the technicians search without its query', async ({
    request,
    baseURL,
    runId,
    sentinelSink,
  }) => {
    const since = consoleSince(new Date().toISOString())
    const index = await request.get('/')
    const html = await index.text()
    const etag = index.headers().etag ?? 'no ETag on /'
    const script = hashedScriptOf(html)
    const deepLink = await request.get(`/jobs/${randomUUID()}`)
    const revalidated = await request.get('/', { headers: { 'If-None-Match': etag } })
    const asset = await request.get(script)
    const assetEtag = asset.headers().etag ?? 'no ETag on the asset'
    const assetRevalidated = await request.get(script, { headers: { 'If-None-Match': assetEtag } })
    const missing = await request.get(`/assets/e2e-${runId}-missing.js`)
    const searchMarker = probeMarker(`qs${runId}`)
    sentinelSink('query sentinel', searchMarker)
    const searchId = `e2e-${runId}-technicians-search`
    const search = await sendRaw(hostPortOf(String(baseURL)), {
      method: 'GET',
      path: `/technicians?search=${searchMarker}`,
      headers: { 'X-Correlation-ID': searchId },
    })

    const answers = [
      { name: '/', response: index, status: 200, cache: htmlCacheControl },
      { name: 'a deep link', response: deepLink, status: 200, cache: htmlCacheControl },
      { name: '/ revalidated', response: revalidated, status: 304, cache: htmlCacheControl },
      { name: 'a hashed script', response: asset, status: 200, cache: assetCacheControl },
      {
        name: 'a hashed script revalidated',
        response: assetRevalidated,
        status: 304,
        cache: assetCacheControl,
      },
      { name: 'a missing asset', response: missing, status: 404, cache: htmlCacheControl },
    ] as const
    for (const answer of answers) {
      expect(answer.response.status(), `${answer.name} status`).toBe(answer.status)
      expect(securityHeadersOf(answer.response), `${answer.name} security headers`).toEqual(
        serverSecurityHeaders,
      )
      expect(answer.response.headers()['cache-control'], `${answer.name} Cache-Control`).toBe(
        answer.cache,
      )
      expect(answer.response.headers().server, `${answer.name} Server`).toBe('nginx')
    }
    expect(html).toContain('<div id="app">')
    expect(await deepLink.text()).toBe(html)
    expect(search.status).toBe(200)
    await expect
      .poll(() => nginxEntryOf(nginxEntries(since), searchId), { timeout: logWaitMilliseconds })
      .toBe('GET /index.html 200')
    expect(
      nginxEntries(since).filter((entry) => JSON.stringify(entry).includes(searchMarker)),
      'nginx lines holding the search text',
    ).toEqual([])
  })
})

test.describe('E2E-11 socket and internal exposure', composeOnly, () => {
  test('a technician is refused a socket ticket with 403', async ({ technician }) => {
    const lena = await technician(demoTechnicians.assignments.email)

    const refused = await lena.requestSocketTicket()

    expect(await readProblem(refused)).toMatchObject({ status: 403, code: 'auth.forbidden' })
    expect(refused.headers()['cache-control']).toContain(apiCacheControl)
  })

  test('every plain, encoded and dot-segment form of /health and /internal gets a 404 page from nginx itself and reaches no service, while double-encoded forms reach an upstream that does not resolve them', async ({
    baseURL,
    runId,
  }) => {
    const since = consoleSince(new Date().toISOString())
    const stack = hostPortOf(String(baseURL))
    const apiControl = `e2e-${runId}-exposure-api-control`
    const notificationsControl = `e2e-${runId}-exposure-notifications-control`
    const probes = exposedPaths.map((path, index) => ({
      ...path,
      id: `e2e-${runId}-exposure-${index + 1}`,
    }))
    const forwarded = doubleEncodedPaths.map((path, index) => ({
      ...path,
      id: `e2e-${runId}-forwarded-${index + 1}`,
    }))
    const escapeId = `e2e-${runId}-exposure-escape`

    for (const probe of probes) {
      const response = await sendRaw(stack, probeRequest(probe.method, probe.raw, probe.id))
      expect(response.status, `${probe.method} ${probe.raw} status`).toBe(404)
      expect(isNginxErrorPage(response), `${probe.method} ${probe.raw} is nginx's own page`).toBe(
        true,
      )
    }
    for (const probe of forwarded) {
      const response = await sendRaw(stack, probeRequest(probe.method, probe.raw, probe.id))
      expect(response.status, `${probe.method} ${probe.raw} status`).toBe(probe.status)
      expect(String(response.headers['content-type'])).toContain(probe.mediaType)
      expect(response.body).not.toContain('Healthy')
    }
    const escape = await sendRaw(stack, probeRequest('POST', rootEscapePath, escapeId))
    expect(escape.status, `${rootEscapePath} status`).toBe(400)
    expect(isNginxErrorPage(escape), `${rootEscapePath} is nginx's own page`).toBe(true)
    const apiReached = await sendRaw(directApi, {
      method: 'GET',
      path: `/api/e2e-${runId}-exposure-control`,
      headers: { 'X-Correlation-ID': apiControl },
    })
    const notificationsReached = await sendRaw(directNotifications, {
      method: 'POST',
      path: '/internal/job-events',
      headers: { 'X-Correlation-ID': notificationsControl, 'Content-Type': 'text/plain' },
      body: 'e2e',
    })
    expect(apiReached.status).toBe(404)
    expect(notificationsReached.status).toBe(415)

    await expect
      .poll(() => sourcesHolding(serviceLines(since), apiControl), { timeout: logWaitMilliseconds })
      .toEqual(['console api', 'logs/api'])
    await expect
      .poll(() => sourcesHolding(serviceLines(since), notificationsControl), {
        timeout: logWaitMilliseconds,
      })
      .toEqual(['console notifications', 'logs/notifications'])
    const entries = nginxEntries(since)
    for (const probe of probes) {
      expect(nginxEntryOf(entries, probe.id), `nginx's line for ${probe.raw}`).toBe(
        `${probe.method} ${probe.logged} 404`,
      )
    }
    const lines = serviceLines(since)
    expect(
      sightings(
        lines,
        probes.map((probe) => probe.id),
      ),
      'service log lines for the nginx-only probes',
    ).toEqual([])
    expect(sightings(lines, [escapeId]), 'service log lines for the root escape').toEqual([])
    expect(
      lines['console notifications'].filter(
        (line) => line.includes('job-event.') && forwarded.some((probe) => line.includes(probe.id)),
      ),
      'job-event lines for the double-encoded probes',
    ).toEqual([])
  })

  test('a Socket.IO handshake through nginx with a foreign Origin is refused, and one with the dashboard origin is upgraded', async ({
    baseURL,
  }) => {
    const since = consoleSince(new Date().toISOString())
    const stack = hostPortOf(String(baseURL))

    const foreign = await upgradeStatus(stack, handshakePath, foreignOrigin)
    const own = await upgradeStatus(stack, handshakePath, new URL(String(baseURL)).origin)

    expect(foreign, 'a foreign Origin').not.toBe(101)
    expect([400, 403]).toContain(foreign)
    expect(own, 'the dashboard origin').toBe(101)
    await expect
      .poll(
        () =>
          serviceLines(since)['console notifications'].filter(
            (line) => line.includes('socket.rejected') && line.includes('origin-not-allowed'),
          ).length,
        { timeout: logWaitMilliseconds },
      )
      .toBe(1)
  })
})
