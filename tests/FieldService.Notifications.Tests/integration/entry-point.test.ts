import assert from 'node:assert/strict'
import { spawn, type ChildProcess } from 'node:child_process'
import { randomUUID } from 'node:crypto'
import { mkdtemp, readdir, readFile, rmdir, unlink } from 'node:fs/promises'
import { createServer, type Server } from 'node:net'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'
import {
  createSocketTicketKey,
  createTestEnvironment,
  createWebhookSecret,
  type Environment,
} from '../support/fixtures.ts'
import { boundPort, findFreePort } from '../support/ports.ts'

const serviceDirectory = fileURLToPath(
  new URL('../../../src/FieldService.Notifications/', import.meta.url),
)
const mainModule = join(serviceDirectory, 'src', 'main.ts')
const faultEntry = fileURLToPath(new URL('../support/fault-entry.ts', import.meta.url))
const logFilePattern = /^notifications\.(\d{8})\.1\.log$/
const fatalLevel = 60
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

function startService(
  environment: Environment,
  entryArguments: readonly string[] = [mainModule],
): RunningService {
  const child = spawn(process.execPath, entryArguments, {
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

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function requiredValue(environment: Environment, name: string): string {
  const value = environment[name]
  assert.ok(value !== undefined && value.length >= 16, `the environment holds ${name}`)
  return value
}

function secretValuesOf(environment: Environment): string[] {
  const webhookSecrets = requiredValue(environment, 'WEBHOOK_SECRETS')
  return [
    webhookSecrets,
    ...webhookSecrets.split(' ').map((entry) => entry.slice('whsec_'.length)),
    requiredValue(environment, 'SOCKET_TICKET_KEY'),
  ]
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

  test(
    'exits with code 1 when a WEBHOOK_SECRETS entry equals SOCKET_TICKET_KEY, printing neither value',
    { timeout: 20_000 },
    async () => {
      const ticketKey = createSocketTicketKey()
      const environment = createTestEnvironment({
        WEBHOOK_SECRETS: `${createWebhookSecret()} whsec_${ticketKey}`,
        SOCKET_TICKET_KEY: ticketKey,
      })
      const service = startService(environment)

      try {
        const exit = await withTimeout('the process to exit', stopTimeoutMs, service.exited)

        assert.equal(exit.code, 1)
        assert.match(service.stderr(), /WEBHOOK_SECRETS entries must differ from SOCKET_TICKET_KEY/)
        for (const value of secretValuesOf(environment)) {
          assert.ok(!service.stderr().includes(value), 'stderr never holds a secret or the key')
        }
        assert.equal(service.stdout(), '')
      } finally {
        stopIfRunning(service)
      }
    },
  )
})

interface FatalRun {
  readonly entryArguments: readonly string[]
  readonly message: string
  readonly listens: boolean
  readonly port?: number
}

interface FatalOutcome {
  readonly fatal: Record<string, unknown>
  readonly stdout: string
  readonly stderr: string
  readonly fileText: string
}

async function runToOneFatalLine(run: FatalRun): Promise<FatalOutcome> {
  const logDirectory = await createLogDirectory()
  const port = run.port ?? (await findFreePort())
  const environment = createTestEnvironment({
    PORT: String(port),
    LOG_DIR: logDirectory,
    LOG_LEVEL: 'info',
  })
  const service = startService(environment, run.entryArguments)

  try {
    const exit = await withTimeout('the process to exit', stopTimeoutMs, service.exited)
    assert.equal(exit.code, 1)

    const printed = logLines(service.stdout())
    assert.equal(
      printed.some((entry) => entry.msg === 'server.listening'),
      run.listens,
      run.listens ? 'the failure came after the service started' : 'the service never listened',
    )
    const fatalLines = printed.filter((entry) => entry.level === fatalLevel)
    assert.equal(fatalLines.length, 1, 'exactly one line at level 60')
    const [fatal] = fatalLines
    assert.ok(fatal !== undefined)
    assert.equal(fatal.msg, run.message)

    const logFile = (await readdir(logDirectory)).find((name) => logFilePattern.test(name))
    assert.ok(logFile !== undefined, 'the daily log file exists')
    const fileText = await readFile(join(logDirectory, logFile), 'utf8')
    assert.deepEqual(
      logLines(fileText).filter((entry) => entry.level === fatalLevel),
      [fatal],
      'the same fatal line was flushed to the file before the exit',
    )

    for (const value of secretValuesOf(environment)) {
      for (const output of [service.stdout(), service.stderr(), fileText]) {
        assert.ok(!output.includes(value), 'no output holds a secret or the key')
      }
    }
    return { fatal, stdout: service.stdout(), stderr: service.stderr(), fileText }
  } finally {
    stopIfRunning(service)
    await service.exited
    await removeLogDirectory(logDirectory)
  }
}

function listenOnFreePort(): Promise<Server> {
  return new Promise<Server>((resolve, reject) => {
    const server = createServer()
    server.once('error', reject)
    server.listen(0, '127.0.0.1', () => {
      resolve(server)
    })
  })
}

function closeServer(server: Server): Promise<void> {
  return new Promise<void>((resolve) => {
    server.close(() => {
      resolve()
    })
  })
}

describe('src/main.ts process failure handlers (LOG-12, SEC-W4-07)', () => {
  const faults = [
    { fault: 'uncaught-exception', message: 'process.uncaught-exception' },
    { fault: 'unhandled-rejection', message: 'process.unhandled-rejection' },
  ] as const

  for (const { fault, message } of faults) {
    const label = fault.replace('-', ' ')

    test(
      `an ${label} with an Error writes one ${message} line at fatal with its stack and exits with code 1`,
      { timeout: 20_000 },
      async () => {
        const failureMessage = `${fault} raised by the test entry ${randomUUID()}`
        const outcome = await runToOneFatalLine({
          entryArguments: [faultEntry, fault, 'error', failureMessage],
          message,
          listens: true,
        })

        assert.deepEqual(Object.keys(outcome.fatal).sort(), ['err', 'level', 'msg', 'time'])
        const err = outcome.fatal.err
        assert.ok(isRecord(err), 'the line carries the error')
        assert.deepEqual(Object.keys(err).sort(), ['message', 'stack', 'type'])
        assert.equal(err.type, 'Error')
        assert.equal(err.message, failureMessage)
        assert.equal(typeof err.stack, 'string')
        const stack = String(err.stack)
        assert.ok(stack.startsWith(`Error: ${failureMessage}\n`), 'the stack starts with the error')
        assert.match(stack, /\n {4}at .*fault-entry\.ts:\d+:\d+/)
        assert.ok(
          !outcome.stderr.includes(failureMessage),
          'the handler, not the default Node printer, reported the failure',
        )
      },
    )

    test(
      `an ${label} with a non-Error value writes one ${message} line at fatal with its type only and exits with code 1`,
      { timeout: 20_000 },
      async () => {
        const sentinel = `value-sentinel-${randomUUID()}`
        const outcome = await runToOneFatalLine({
          entryArguments: [faultEntry, fault, 'nested-object', sentinel],
          message,
          listens: true,
        })

        assert.deepEqual(Object.keys(outcome.fatal).sort(), ['errType', 'level', 'msg', 'time'])
        assert.equal(outcome.fatal.errType, 'object')
        for (const output of [outcome.stdout, outcome.stderr, outcome.fileText]) {
          assert.ok(!output.includes(sentinel), 'no output holds the failure value')
        }
      },
    )
  }

  test(
    'an unhandled rejection with an Error carrying a nested ticket writes only its type, message and stack',
    { timeout: 20_000 },
    async () => {
      const failureMessage = `unhandled-rejection raised by the test entry ${randomUUID()}`
      const nestedTicket = `nested-ticket-sentinel-${randomUUID()}`
      const outcome = await runToOneFatalLine({
        entryArguments: [
          faultEntry,
          'unhandled-rejection',
          'error-with-properties',
          failureMessage,
          nestedTicket,
        ],
        message: 'process.unhandled-rejection',
        listens: true,
      })

      const err = outcome.fatal.err
      assert.ok(isRecord(err), 'the line carries the error')
      assert.deepEqual(Object.keys(err).sort(), ['message', 'stack', 'type'])
      assert.equal(err.message, failureMessage)
      assert.ok(String(err.stack).startsWith(`Error: ${failureMessage}\n`))
      for (const output of [outcome.stdout, outcome.stderr, outcome.fileText]) {
        assert.ok(!output.includes(nestedTicket), 'no output holds the nested ticket')
      }
    },
  )

  test(
    'a port already in use writes one server.listen-failed line at fatal with the error and exits with code 1',
    { timeout: 20_000 },
    async () => {
      const occupant = await listenOnFreePort()

      try {
        const outcome = await runToOneFatalLine({
          entryArguments: [mainModule],
          message: 'server.listen-failed',
          listens: false,
          port: boundPort(occupant),
        })

        assert.deepEqual(Object.keys(outcome.fatal).sort(), ['err', 'level', 'msg', 'time'])
        const err = outcome.fatal.err
        assert.ok(isRecord(err), 'the line carries the error')
        assert.deepEqual(Object.keys(err).sort(), ['code', 'message', 'stack', 'type'])
        assert.equal(err.type, 'Error')
        assert.equal(err.code, 'EADDRINUSE')
        assert.equal(typeof err.stack, 'string')
      } finally {
        await closeServer(occupant)
      }
    },
  )
})
