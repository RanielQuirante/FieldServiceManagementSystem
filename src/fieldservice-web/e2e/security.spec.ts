import type { Page, Response } from '@playwright/test'

import { expect, test } from './fixtures.ts'
import { jobsCopy } from './support/copy.ts'
import { apiCacheControl, serverSecurityHeaders } from './support/headers.ts'

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

test.describe('E2E-16 CSP and headers', () => {
  test('the dashboard is served with the production security headers, self-hosted fonts and no-store API responses', async ({
    adminPage: page,
    baseURL,
  }) => {
    const apiResponses = collectApiResponses(page)

    const served = await page.goto('/')

    expect(served?.status()).toBe(200)
    const headers = (await served?.allHeaders()) ?? {}
    for (const [name, value] of Object.entries(serverSecurityHeaders)) {
      expect(headers[name], `${name} on /`).toBe(value)
    }
    expect(String(headers.server), 'Server header on /').not.toMatch(/\d/)
    await expect(page).toHaveURL('/jobs')
    await expect(
      page.getByRole('region', { name: jobsCopy.table }).locator('tbody tr'),
    ).toHaveCount(12)
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
