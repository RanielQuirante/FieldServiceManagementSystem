import process from 'node:process'
import { setTimeout as delay } from 'node:timers/promises'

import type { FullConfig } from '@playwright/test'

import { demoPassword } from './support/api.ts'
import { newRunId, selectedBaseUrl } from './support/run.ts'

const readinessMilliseconds = 60_000

const pollMilliseconds = 500

interface Readiness {
  readonly ready: boolean
  readonly detail: string
}

async function dashboardServed(baseUrl: string): Promise<Readiness> {
  const response = await fetch(new URL('/', baseUrl), { headers: { Accept: 'text/html' } })
  const html = await response.text()
  const ready = response.status === 200 && html.includes('<div id="app">')
  return { ready, detail: `GET / returned ${response.status}` }
}

async function apiReachedThroughProxy(baseUrl: string): Promise<Readiness> {
  const response = await fetch(new URL('/api/checklist-templates', baseUrl))
  const mediaType = response.headers.get('content-type') ?? ''
  if (response.status !== 401 || !mediaType.startsWith('application/problem+json')) {
    return {
      ready: false,
      detail: `GET /api/checklist-templates without a token returned ${response.status} ${mediaType}`,
    }
  }
  const problem = (await response.json()) as { code?: string }
  return {
    ready: problem.code === 'auth.unauthenticated',
    detail: `GET /api/checklist-templates without a token returned 401 ${problem.code ?? ''}`,
  }
}

function engineErrorCode(body: string): unknown {
  try {
    return (JSON.parse(body) as { code?: unknown }).code
  } catch {
    return null
  }
}

async function realtimeReachedThroughProxy(baseUrl: string): Promise<Readiness> {
  const response = await fetch(new URL('/socket.io/?EIO=4&transport=polling', baseUrl))
  const code = engineErrorCode(await response.text())
  return {
    ready: response.status === 400 && code === 0,
    detail: `GET /socket.io/ with the polling transport returned ${response.status}; the notification service answers 400 with code 0 when it refuses polling`,
  }
}

async function waitFor(
  check: (baseUrl: string) => Promise<Readiness>,
  baseUrl: string,
  deadline: number,
): Promise<void> {
  let last = 'no answer yet'
  while (Date.now() < deadline) {
    const result = await check(baseUrl).catch((error: unknown): Readiness => ({
      ready: false,
      detail: error instanceof Error ? error.message : 'request failed',
    }))
    if (result.ready) {
      return
    }
    last = result.detail
    await delay(pollMilliseconds)
  }
  throw new Error(`The stack at ${baseUrl} was not ready within 60 seconds: ${last}.`)
}

export default async function globalSetup(config: FullConfig): Promise<void> {
  demoPassword()
  const configured = process.env.E2E_RUN_ID
  const runId = configured !== undefined && configured.length > 0 ? configured : newRunId()
  process.env.E2E_RUN_ID = runId
  const baseUrl = selectedBaseUrl(config)
  const deadline = Date.now() + readinessMilliseconds
  await waitFor(dashboardServed, baseUrl, deadline)
  await waitFor(apiReachedThroughProxy, baseUrl, deadline)
  await waitFor(realtimeReachedThroughProxy, baseUrl, deadline)
  process.stdout.write(`e2e run ${runId} against ${baseUrl}\n`)
}
