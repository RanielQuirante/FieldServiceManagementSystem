import { randomInt } from 'node:crypto'
import { existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import process from 'node:process'
import { fileURLToPath } from 'node:url'

import type { FullConfig, Page } from '@playwright/test'

export const webRoot = fileURLToPath(new URL('../../', import.meta.url))

export const resultsRoot = join(webRoot, 'test-results')

const runIdAlphabet = '0123456789abcdefghijklmnopqrstuvwxyz'

export function newRunId(now: number = Date.now()): string {
  let suffix = ''
  for (let index = 0; index < 4; index += 1) {
    suffix += runIdAlphabet.charAt(randomInt(runIdAlphabet.length))
  }
  return `${now.toString(36)}${suffix}`
}

export function currentRunId(): string {
  const value = process.env.E2E_RUN_ID
  if (value === undefined || value.trim().length === 0) {
    throw new Error('E2E_RUN_ID is not set. The global setup sets it before the first test runs.')
  }
  return value
}

function requestedProjects(argv: readonly string[]): string[] {
  const names: string[] = []
  argv.forEach((argument, index) => {
    if (argument.startsWith('--project=')) {
      names.push(argument.slice('--project='.length))
    } else if (argument === '--project') {
      const next = argv[index + 1]
      if (next !== undefined) {
        names.push(next)
      }
    }
  })
  return names
}

export function selectedBaseUrl(
  config: FullConfig,
  argv: readonly string[] = process.argv,
): string {
  const override = process.env.E2E_BASE_URL
  if (override !== undefined && override.length > 0) {
    return override
  }
  const names = requestedProjects(argv)
  const urls = config.projects
    .filter((project) => names.includes(project.name))
    .map((project) => project.use.baseURL)
    .filter((url): url is string => typeof url === 'string')
  const unique = [...new Set(urls)]
  if (unique.length !== 1 || unique[0] === undefined) {
    throw new Error(
      'Choose one stack: run with --project=host or --project=compose, or set E2E_BASE_URL.',
    )
  }
  return unique[0]
}

export function logsRoot(): string {
  const configured = process.env.E2E_LOGS_DIR
  return resolve(
    webRoot,
    configured !== undefined && configured.length > 0 ? configured : '../../logs',
  )
}

export function resultsFile(folder: string, name: string): string {
  return join(resultsRoot, folder, name)
}

export function writeJsonFile(path: string, value: unknown): void {
  mkdirSync(dirname(path), { recursive: true })
  writeFileSync(path, `${JSON.stringify(value, null, 2)}\n`, { encoding: 'utf8', mode: 0o600 })
}

export function readJsonFile<T>(path: string, fallback: T): T {
  if (!existsSync(path)) {
    return fallback
  }
  return JSON.parse(readFileSync(path, 'utf8')) as T
}

export function readRunFiles<T>(folder: string, runId: string): T[] {
  const directory = join(resultsRoot, folder)
  if (!existsSync(directory)) {
    return []
  }
  return readdirSync(directory)
    .filter((name) => name.startsWith(`${runId}-`) && name.endsWith('.json'))
    .sort()
    .map((name) => readJsonFile<T>(join(directory, name), [] as unknown as T))
}

export function e2eTitle(runId: string, text: string): string {
  return `E2E ${runId} ${text}`
}

export async function openInApp(page: Page, path: string): Promise<void> {
  await page.evaluate((target) => {
    window.history.pushState(null, '', target)
    window.dispatchEvent(new PopStateEvent('popstate', { state: null }))
  }, path)
}
