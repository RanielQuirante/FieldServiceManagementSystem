import assert from 'node:assert/strict'
import { spawn, type ChildProcess } from 'node:child_process'
import { mkdtemp, readdir, readFile, rmdir, unlink } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'
import {
  createTestEnvironment,
  createWebhookSecret,
  type Environment,
} from '../support/fixtures.ts'
import { findFreePort } from '../support/ports.ts'

const serviceDirectory = fileURLToPath(
  new URL('../../../src/fieldservice-notifications/', import.meta.url),
)
const mainModule = join(serviceDirectory, 'src', 'main.ts')
const logFilePattern = /^notifications\.(\d{8})\.1\.log$/
const startupTimeoutMs = 5_000
const stopTimeoutMs = 5_000

interface ExitResult {
  readonly code: number | null
  readonly signal: NodeJS.Signals | null
}

interface RunningService {
  readonly child: ChildProcess
  readonly exited: Promise<ExitResult>
  stdout(): string
  stderr(): string
}

function startService(environment: Environment): RunningService {
  const child = spawn(process.execPath, [mainModule], {
    cwd: serviceDirectory,
    env: { ...environment, TZ: process.env.TZ },
    stdio: ['ignore', 'pipe', 'pipe'],
  })
  let stdout = ''
  let stderr = ''
  child.stdout?.setEncoding('utf8')
  child.stderr?.setEncoding('utf8')
  child.stdout?.on('data', (chunk: string) => {
    stdout += chunk
  })
  child.stderr?.on('data', (chunk: string) => {
    stderr += chunk
  })
  const exited = new Promise<ExitResult>((resolve) => {
    child.once('exit', (code, signal) => {
      resolve({ code, signal })
    })
  })
  return { child, exited, stdout: () => stdout, stderr: () => stderr }
}

function stopIfRunning(service: RunningService): void {
  if (service.child.exitCode === null && service.child.signalCode === null) {
    service.child.kill('SIGKILL')
  }
}

async function waitFor<T>(
  description: string,
  timeoutMs: number,
  probe: () => T | undefined | Promise<T | undefined>,
): Promise<T> {
  const deadline = Date.now() + timeoutMs
  for (;;) {
    const value = await probe()
    if (value !== undefined) {
      return value
    }
    if (Date.now() > deadline) {
      throw new Error(`timed out waiting for ${description}`)
    }
    await new Promise((resolve) => setTimeout(resolve, 25))
  }
}

function withTimeout<T>(description: string, timeoutMs: number, promise: Promise<T>): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const timer = setTimeout(() => {
      reject(new Error(`timed out waiting for ${description}`))
    }, timeoutMs)
    promise.then(
      (value) => {
        clearTimeout(timer)
        resolve(value)
      },
      (error: unknown) => {
        clearTimeout(timer)
        reject(error instanceof Error ? error : new Error(String(error)))
      },
    )
  })
}

function logLines(text: string): Record<string, unknown>[] {
  return text
    .split('\n')
    .filter((line) => line.trim().startsWith('{'))
    .map((line) => JSON.parse(line) as Record<string, unknown>)
}

function localDateStamp(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${date.getFullYear()}${month}${day}`
}

async function createLogDirectory(): Promise<string> {
  return mkdtemp(join(tmpdir(), 'notifications-entry-point-'))
}

async function removeLogDirectory(directory: string): Promise<void> {
  for (const name of await readdir(directory)) {
    await unlink(join(directory, name))
  }
  await rmdir(directory)
}

describe('src/main.ts entry point', () => {
  test(
    'listens, writes the daily log file and exits with code 0 on SIGTERM',
    { timeout: 20_000 },
    async () => {
      const logDirectory = await createLogDirectory()
      const port = await findFreePort()
      const startedOn = localDateStamp(new Date())
      const service = startService(
        createTestEnvironment({ PORT: String(port), LOG_DIR: logDirectory, LOG_LEVEL: 'info' }),
      )

      try {
        const listening = await waitFor('server.listening on stdout', startupTimeoutMs, () =>
          logLines(service.stdout()).find((entry) => entry.msg === 'server.listening'),
        )
        assert.equal(listening.host, '127.0.0.1')
        assert.equal(listening.port, port)

        const health = await fetch(`http://127.0.0.1:${port}/health/live`)
        assert.equal(health.status, 200)
        assert.equal(await health.text(), 'Healthy')

        const logFile = await waitFor('the log file', startupTimeoutMs, async () =>
          (await readdir(logDirectory)).find((name) => logFilePattern.test(name)),
        )
        const stamp = logFilePattern.exec(logFile)?.[1]
        assert.ok(
          stamp === startedOn || stamp === localDateStamp(new Date()),
          'the file name carries the local date',
        )

        service.child.kill('SIGTERM')
        const exit = await withTimeout('the process to exit', stopTimeoutMs, service.exited)
        assert.equal(exit.code, 0)

        const written = logLines(await readFile(join(logDirectory, logFile), 'utf8'))
        const messages = written.map((entry) => entry.msg)
        assert.ok(messages.includes('server.listening'), 'the file holds server.listening')
        assert.ok(messages.includes('server.stopping'), 'the file holds server.stopping')
        assert.ok(messages.includes('server.stopped'), 'the log was flushed before the exit')
      } finally {
        stopIfRunning(service)
        await service.exited
        await removeLogDirectory(logDirectory)
      }
    },
  )

  test(
    'exits with code 1 and names a missing WEBHOOK_SECRETS without printing a secret',
    { timeout: 20_000 },
    async () => {
      const environment = createTestEnvironment({ WEBHOOK_SECRETS: undefined })
      const service = startService(environment)

      try {
        const exit = await withTimeout('the process to exit', stopTimeoutMs, service.exited)

        assert.equal(exit.code, 1)
        assert.match(service.stderr(), /WEBHOOK_SECRETS is required/)
        assert.ok(
          !service.stderr().includes(environment.SOCKET_TICKET_KEY ?? ''),
          'stderr never holds the socket ticket key',
        )
        assert.equal(service.stdout(), '')
      } finally {
        stopIfRunning(service)
      }
    },
  )

  test(
    'exits with code 1 and names an invalid WEBHOOK_SECRETS without printing its value',
    { timeout: 20_000 },
    async () => {
      const invalidSecret = createWebhookSecret(16)
      const service = startService(createTestEnvironment({ WEBHOOK_SECRETS: invalidSecret }))

      try {
        const exit = await withTimeout('the process to exit', stopTimeoutMs, service.exited)

        assert.equal(exit.code, 1)
        assert.match(service.stderr(), /WEBHOOK_SECRETS/)
        assert.ok(!service.stderr().includes(invalidSecret), 'stderr never holds the value')
        assert.equal(service.stdout(), '')
      } finally {
        stopIfRunning(service)
      }
    },
  )
})
