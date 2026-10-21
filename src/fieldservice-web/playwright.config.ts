import { existsSync } from 'node:fs'
import process from 'node:process'

import { defineConfig, devices } from '@playwright/test'

const demoPasswordFile = new URL('./e2e/.env', import.meta.url)

if (process.env.E2E_DEMO_PASSWORD === undefined && existsSync(demoPasswordFile)) {
  process.loadEnvFile(demoPasswordFile)
}

const isCi = Boolean(process.env.CI)

function baseUrl(projectDefault: string): string {
  return process.env.E2E_BASE_URL || projectDefault
}

export default defineConfig({
  testDir: './e2e',
  globalSetup: './e2e/global-setup.ts',
  globalTeardown: './e2e/global-teardown.ts',
  fullyParallel: false,
  workers: 1,
  retries: isCi ? 1 : 0,
  failOnFlakyTests: isCi,
  forbidOnly: isCi,
  timeout: 30_000,
  expect: {
    timeout: 5_000,
  },
  reporter: isCi
    ? [
        ['list'],
        ['html', { outputFolder: 'playwright-report', open: 'never' }],
        ['junit', { outputFile: 'test-results/e2e-junit.xml' }],
      ]
    : [['list'], ['html', { outputFolder: 'playwright-report', open: 'never' }]],
  use: {
    ...devices['Desktop Chrome'],
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    locale: 'en-US',
    timezoneId: 'UTC',
    viewport: { width: 1280, height: 800 },
    serviceWorkers: 'block',
  },
  projects: [
    {
      name: 'compose',
      use: { baseURL: baseUrl('http://127.0.0.1:8081') },
    },
    {
      name: 'host',
      use: { baseURL: baseUrl('http://127.0.0.1:5173') },
    },
  ],
})
