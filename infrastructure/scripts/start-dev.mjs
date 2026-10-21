import { spawn, spawnSync } from 'node:child_process'
import fs from 'node:fs'
import net from 'node:net'
import path from 'node:path'
import readline from 'node:readline'

const EXIT_OK = 0
const EXIT_FAILED = 1
const EXIT_USAGE = 2

const PROGRAM = 'start-dev'

const USAGE =
  'usage: node infrastructure/scripts/start-dev.mjs [--check] [--only api,notifications,web]'

const HELP = [
  USAGE,
  '',
  'Checks the environment, then starts the API (dotnet watch), the notification service and',
  'the web dev server (npm run dev) in this terminal. Ctrl+C stops all of them.',
  '',
  '  --check   run the checks only; exit 1 with each problem and its fix',
  '  --only    start or check a subset, for example --only web',
].join('\n')

const ROOT = path.resolve(import.meta.dirname, '..', '..')

const IS_WINDOWS = process.platform === 'win32'

const SERVICE_NAMES = Object.freeze(['api', 'notifications', 'web'])

const SERVICES = Object.freeze({
  api: {
    port: 5080,
    hosts: ['127.0.0.1', '::1'],
    folder: '.',
    command: 'dotnet',
    args: ['watch', '--project', 'src/FieldService.Api'],
    env: { DOTNET_WATCH_RESTART_ON_RUDE_EDIT: 'true' },
    watchHost: true,
  },
  notifications: {
    port: 5090,
    hosts: ['127.0.0.1'],
    folder: 'src/FieldService.Notifications',
    command: 'npm',
    args: ['run', 'dev'],
    env: {},
  },
  web: {
    port: 5173,
    hosts: ['127.0.0.1'],
    folder: 'src/FieldService.Web',
    command: 'npm',
    args: ['run', 'dev'],
    env: {},
  },
})

const NODE_MODULES = Object.freeze({
  api: [],
  notifications: ['src/FieldService.Notifications', 'tests/FieldService.Notifications.Tests'],
  web: ['src/FieldService.Web'],
})

const LOG_FOLDERS = Object.freeze({
  api: ['logs/api'],
  notifications: ['logs/notifications'],
  web: [],
})

const NOTIFICATIONS_ENV_FILE = 'src/FieldService.Notifications/.env'

const NOTIFICATIONS_KEYS = Object.freeze([
  'HOST',
  'PORT',
  'NODE_ENV',
  'LOG_LEVEL',
  'LOG_DIR',
  'LOG_PRETTY',
  'WEBHOOK_SECRETS',
  'WEBHOOK_TOLERANCE_SECONDS',
  'SOCKET_TICKET_KEY',
  'SOCKET_TICKET_ISSUER',
  'SOCKET_TICKET_AUDIENCE',
  'SOCKET_ALLOWED_ORIGINS',
  'SOCKET_SESSION_MAX_SECONDS',
])

const NOTIFICATIONS_REQUIRED = Object.freeze([
  'LOG_DIR',
  'WEBHOOK_SECRETS',
  'SOCKET_TICKET_KEY',
  'SOCKET_ALLOWED_ORIGINS',
])

const STORES = Object.freeze([
  {
    id: 'fieldservice-api',
    keys: [
      'ConnectionStrings:FieldService',
      'ConnectionStrings:Migrations',
      'Auth:AccessTokenKey',
      'Auth:SocketTicketKey',
      'Notifications:WebhookSecret',
      'Seed:DemoPassword',
    ],
  },
  {
    id: 'fieldservice-api-tests',
    keys: [
      'FIELDSERVICE_TEST_ADMIN_CONNECTION',
      'FIELDSERVICE_MIGRATOR_PASSWORD',
      'FIELDSERVICE_APP_PASSWORD',
    ],
  },
])

const APP_CONNECTION_KEY = 'ConnectionStrings:FieldService'

const DEFAULT_DATABASE_PORT = 5432

const SDK_BAND = Object.freeze({ major: 10, minor: 0, lowest: 402, highest: 499 })

const CONNECT_TIMEOUT_MS = 2000

const STOP_GRACE_MS = 10000

const HOST_POLL_MS = 200

const CREATE_ENV = 'node infrastructure/scripts/create-dev-env.mjs'

const DOTNET_ENVIRONMENT = Object.freeze({
  DOTNET_NOLOGO: 'true',
  DOTNET_CLI_TELEMETRY_OPTOUT: 'true',
})

class UsageError extends Error {}

function parseArguments(args) {
  const options = { check: false, help: false, only: null }
  for (let index = 0; index < args.length; index += 1) {
    const arg = args[index]
    const equals = arg.startsWith('--') ? arg.indexOf('=') : -1
    const name = equals === -1 ? arg : arg.slice(0, equals)
    const inline = equals === -1 ? null : arg.slice(equals + 1)
    if ((name === '--check' || name === '--help' || name === '-h') && inline !== null) {
      throw new UsageError(`${name} takes no value`)
    }
    if (name === '--check') {
      options.check = true
    } else if (name === '--help' || name === '-h') {
      options.help = true
    } else if (name === '--only') {
      const value = inline ?? args[(index += 1)]
      if (value === undefined || value === '') {
        throw new UsageError('--only needs a list such as api,web')
      }
      const names = value.split(',').map((item) => item.trim())
      const unknown = names.filter((item) => !SERVICE_NAMES.includes(item))
      if (unknown.length > 0) {
        throw new UsageError(
          `unknown service ${unknown.join(', ')}; use ${SERVICE_NAMES.join(', ')}`,
        )
      }
      options.only = SERVICE_NAMES.filter((item) => names.includes(item))
    } else {
      throw new UsageError(`unknown argument ${JSON.stringify(arg)}`)
    }
  }
  return options
}

function problem(text, fix) {
  return { text, fix }
}

function readEnvKeys(file) {
  let text = ''
  try {
    text = fs.readFileSync(file, 'utf8')
  } catch (error) {
    if (error.code === 'ENOENT') {
      return null
    }
    throw error
  }
  const keys = new Map()
  for (const raw of text.split('\n')) {
    const line = raw.trim()
    if (line === '' || line.startsWith('#')) {
      continue
    }
    const body = line.startsWith('export ') ? line.slice('export '.length).trimStart() : line
    const equals = body.indexOf('=')
    if (equals <= 0) {
      continue
    }
    let value = body.slice(equals + 1).trim()
    const quote = value[0]
    if (value.length >= 2 && (quote === '"' || quote === "'") && value.endsWith(quote)) {
      value = value.slice(1, -1)
    }
    keys.set(body.slice(0, equals).trim(), value.trim() !== '')
  }
  return keys
}

function parseConnectionString(text) {
  const pairs = new Map()
  let index = 0
  while (index < text.length) {
    const equals = text.indexOf('=', index)
    if (equals === -1) {
      break
    }
    const key = text.slice(index, equals).trim().toLowerCase()
    let cursor = equals + 1
    let value = ''
    const quote = text[cursor]
    if (quote === '"' || quote === "'") {
      cursor += 1
      while (cursor < text.length) {
        if (text[cursor] === quote && text[cursor + 1] === quote) {
          value += quote
          cursor += 2
        } else if (text[cursor] === quote) {
          cursor += 1
          break
        } else {
          value += text[cursor]
          cursor += 1
        }
      }
      const semicolon = text.indexOf(';', cursor)
      index = semicolon === -1 ? text.length : semicolon + 1
    } else {
      const semicolon = text.indexOf(';', cursor)
      const end = semicolon === -1 ? text.length : semicolon
      value = text.slice(cursor, end).trim()
      index = end + 1
    }
    if (key !== '') {
      pairs.set(key, value)
    }
  }
  return pairs
}

function databaseEndpoint(connectionString) {
  const pairs = parseConnectionString(connectionString)
  const hostList = pairs.get('host') ?? pairs.get('server') ?? ''
  let host = hostList.split(',')[0].trim()
  let port = Number(pairs.get('port') ?? DEFAULT_DATABASE_PORT)
  const colon = host.lastIndexOf(':')
  if (colon > 0 && host.indexOf(':') === colon && /^\d+$/.test(host.slice(colon + 1))) {
    port = Number(host.slice(colon + 1))
    host = host.slice(0, colon)
  }
  if (host === '' || !Number.isInteger(port) || port < 1 || port > 65535) {
    return null
  }
  return { host, port }
}

function readStore(id) {
  const result = spawnSync('dotnet', ['user-secrets', 'list', '--id', id, '--json'], {
    cwd: ROOT,
    encoding: 'utf8',
    env: { ...process.env, ...DOTNET_ENVIRONMENT },
    maxBuffer: 16777216,
    windowsHide: true,
  })
  if (result.error || result.status !== 0) {
    return { ok: false }
  }
  const begin = result.stdout.indexOf('//BEGIN')
  const end = result.stdout.indexOf('//END', begin)
  if (begin === -1 || end === -1) {
    return { ok: false }
  }
  let data = null
  try {
    const json = result.stdout.slice(begin + '//BEGIN'.length, end).trim()
    data = json === '' ? {} : JSON.parse(json)
  } catch {
    return { ok: false }
  }
  const keys = new Map()
  let database
  for (const [key, value] of Object.entries(data ?? {})) {
    keys.set(key, typeof value === 'string' && value.trim() !== '')
    if (key === APP_CONNECTION_KEY && typeof value === 'string' && value.trim() !== '') {
      database = databaseEndpoint(value)
    }
  }
  return { ok: true, keys, database }
}

function checkNode() {
  let wanted = ''
  try {
    wanted = fs.readFileSync(path.join(ROOT, '.nvmrc'), 'utf8').trim().replace(/^v/, '')
  } catch {
    return [problem('.nvmrc is missing at the repository root', 'Restore .nvmrc from git')]
  }
  const wantedMajor = wanted.split('.')[0]
  const runningMajor = process.versions.node.split('.')[0]
  if (wantedMajor === runningMajor) {
    return []
  }
  return [
    problem(
      `Node ${process.versions.node} is running; .nvmrc asks for ${wanted}`,
      `Install Node ${wanted} and run this script with it (docs/development.md)`,
    ),
  ]
}

function checkDotnet() {
  const result = spawnSync('dotnet', ['--version'], {
    cwd: ROOT,
    encoding: 'utf8',
    env: { ...process.env, ...DOTNET_ENVIRONMENT },
    windowsHide: true,
  })
  const fix = `Install .NET SDK 10.0.${SDK_BAND.lowest} or a later 10.0.4xx patch (docs/development.md)`
  if (result.error) {
    return { ok: false, problems: [problem('dotnet was not found on PATH', fix)] }
  }
  const version = result.stdout.trim()
  const match = /^(\d+)\.(\d+)\.(\d+)$/.exec(version)
  const patch = match === null ? -1 : Number(match[3])
  const inBand =
    match !== null &&
    Number(match[1]) === SDK_BAND.major &&
    Number(match[2]) === SDK_BAND.minor &&
    patch >= SDK_BAND.lowest &&
    patch <= SDK_BAND.highest
  if (result.status === 0 && inBand) {
    return { ok: true, problems: [] }
  }
  const found = result.status === 0 ? `SDK ${version} is active` : 'no SDK matches global.json'
  return { ok: false, problems: [problem(`${found} at the repository root`, fix)] }
}

function checkNodeModules(selected) {
  const problems = []
  for (const name of selected) {
    for (const folder of NODE_MODULES[name]) {
      if (!fs.existsSync(path.join(ROOT, folder, 'node_modules'))) {
        problems.push(
          problem(
            `${folder}/node_modules is missing`,
            `npm ci --ignore-scripts --prefix ${folder}`,
          ),
        )
      }
    }
  }
  return problems
}

function checkNotificationsEnv() {
  const keys = readEnvKeys(path.join(ROOT, NOTIFICATIONS_ENV_FILE))
  if (keys === null) {
    return [problem(`${NOTIFICATIONS_ENV_FILE} is missing`, CREATE_ENV)]
  }
  const missing = NOTIFICATIONS_KEYS.filter((key) => !keys.has(key))
  const empty = NOTIFICATIONS_REQUIRED.filter((key) => keys.has(key) && !keys.get(key))
  const problems = []
  if (missing.length > 0) {
    problems.push(problem(`${NOTIFICATIONS_ENV_FILE} lacks ${missing.join(', ')}`, CREATE_ENV))
  }
  if (empty.length > 0) {
    problems.push(
      problem(`${NOTIFICATIONS_ENV_FILE} has no value for ${empty.join(', ')}`, CREATE_ENV),
    )
  }
  return problems
}

function checkStores() {
  const problems = []
  let database
  for (const store of STORES) {
    const found = readStore(store.id)
    if (!found.ok) {
      problems.push(
        problem(
          `the user-secrets store ${store.id} could not be read`,
          `Check that dotnet works at the repository root, then run ${CREATE_ENV}`,
        ),
      )
      continue
    }
    const missing = store.keys.filter((key) => found.keys.get(key) !== true)
    if (missing.length > 0) {
      problems.push(
        problem(`the user-secrets store ${store.id} lacks ${missing.join(', ')}`, CREATE_ENV),
      )
    }
    if (found.database !== undefined) {
      database = found.database
    }
  }
  return { problems, database }
}

function probeConnect(host, port) {
  return new Promise((resolve) => {
    const socket = net.connect({ host, port })
    socket.setTimeout(CONNECT_TIMEOUT_MS)
    socket.once('connect', () => {
      socket.destroy()
      resolve(true)
    })
    socket.once('timeout', () => {
      socket.destroy()
      resolve(false)
    })
    socket.once('error', () => resolve(false))
  })
}

function probeListen(host, port) {
  return new Promise((resolve) => {
    const server = net.createServer()
    server.once('error', (error) => resolve(['EADDRINUSE', 'EACCES'].includes(error.code)))
    server.listen({ host, port, exclusive: true }, () => server.close(() => resolve(false)))
  })
}

async function portBusy(service) {
  for (const host of service.hosts) {
    if (await probeConnect(host, service.port)) {
      return true
    }
  }
  return probeListen(service.hosts[0], service.port)
}

async function checkPorts(selected) {
  const problems = []
  for (const name of selected) {
    const service = SERVICES[name]
    if (await portBusy(service)) {
      problems.push(
        problem(
          `port ${service.port} (${name}) is in use`,
          `Stop the process that holds it: lsof -nP -iTCP:${service.port} -sTCP:LISTEN on macOS, ` +
            `Get-NetTCPConnection -LocalPort ${service.port} on Windows; or stop the Compose backend profile`,
        ),
      )
    }
  }
  return problems
}

function checkLogFolders(selected) {
  const problems = []
  for (const name of selected) {
    for (const folder of LOG_FOLDERS[name]) {
      if (!fs.existsSync(path.join(ROOT, folder))) {
        problems.push(problem(`${folder} is missing`, CREATE_ENV))
      }
    }
  }
  return problems
}

async function runChecks(selected) {
  const problems = [...checkNode()]
  if (selected.includes('api')) {
    const dotnet = checkDotnet()
    problems.push(...dotnet.problems)
    if (dotnet.ok) {
      const stores = checkStores()
      problems.push(...stores.problems)
      if (stores.database === null) {
        problems.push(problem(`${APP_CONNECTION_KEY} names no database host and port`, CREATE_ENV))
      } else if (
        stores.database !== undefined &&
        !(await probeConnect(stores.database.host, stores.database.port))
      ) {
        problems.push(
          problem(
            `nothing accepts TCP connections at ${stores.database.host}:${stores.database.port}, ` +
              `the database in ${APP_CONNECTION_KEY}`,
            'Start PostgreSQL (brew services start postgresql@17), or the Compose database: ' +
              'docker compose -f infrastructure/docker-compose.yml up -d --wait db',
          ),
        )
      }
    }
  }
  problems.push(...checkNodeModules(selected))
  if (selected.includes('notifications')) {
    problems.push(...checkNotificationsEnv())
  }
  problems.push(...(await checkPorts(selected)))
  problems.push(...checkLogFolders(selected))
  return problems
}

function report(problems) {
  const noun = problems.length === 1 ? 'problem' : 'problems'
  const lines = [`${PROGRAM}: ${problems.length} ${noun} found`]
  for (const item of problems) {
    lines.push(`- ${item.text}`, `  fix: ${item.fix}`)
  }
  process.stderr.write(`${lines.join('\n')}\n`)
}

function pipeLines(stream, name, target) {
  const lines = readline.createInterface({ input: stream, crlfDelay: Infinity })
  lines.on('line', (line) => target.write(`[${name}] ${line}\n`))
}

function startService(name) {
  const service = SERVICES[name]
  const options = {
    cwd: path.join(ROOT, service.folder),
    env: { ...process.env, ...service.env },
    stdio: ['ignore', 'pipe', 'pipe'],
    detached: !IS_WINDOWS,
    windowsHide: true,
  }
  const child =
    IS_WINDOWS && service.command === 'npm'
      ? spawn(`npm ${service.args.join(' ')}`, { ...options, shell: true })
      : spawn(service.command, service.args, options)
  pipeLines(child.stdout, name, process.stdout)
  pipeLines(child.stderr, name, process.stderr)
  return { name, child, exited: false, hostEnded: false }
}

function signalGroup(pid, signal) {
  try {
    process.kill(-pid, signal)
    return true
  } catch {
    return false
  }
}

function signalTree(running, signal) {
  for (const entry of running) {
    if (entry.exited || entry.child.pid === undefined) {
      continue
    }
    if (IS_WINDOWS) {
      spawnSync('taskkill', ['/PID', String(entry.child.pid), '/T', '/F'], {
        stdio: 'ignore',
        windowsHide: true,
      })
    } else {
      signalGroup(entry.child.pid, signal)
    }
  }
}

function groupMembers(pgid) {
  const result = spawnSync('ps', ['-A', '-o', 'pid=,ppid=,pgid='], {
    encoding: 'utf8',
    maxBuffer: 16777216,
  })
  if (result.error || result.status !== 0) {
    return null
  }
  const members = []
  for (const line of result.stdout.split('\n')) {
    const fields = line.trim().split(/\s+/).map(Number)
    if (fields.length === 3 && fields.every(Number.isInteger) && fields[2] === pgid) {
      members.push({ pid: fields[0], ppid: fields[1] })
    }
  }
  return members
}

function onlyWatchHostLeft(pgid) {
  const members = groupMembers(pgid)
  return members !== null && members.every((item) => item.pid === pgid || item.ppid === pgid)
}

function endWatchHostAfterApp(entry) {
  const timer = setInterval(() => {
    if (entry.exited) {
      clearInterval(timer)
    } else if (onlyWatchHostLeft(entry.child.pid)) {
      clearInterval(timer)
      entry.hostEnded = true
      signalGroup(entry.child.pid, 'SIGKILL')
    }
  }, HOST_POLL_MS)
  timer.unref()
}

function describeExit(entry, code, signal) {
  if (entry.hostEnded && signal === 'SIGKILL') {
    return 'stopped (the app shut down, then its dotnet watch host was ended)'
  }
  return signal ? `stopped by ${signal}` : `exited with code ${code}`
}

function runServices(selected) {
  return new Promise((resolve) => {
    const running = selected.map((name) => startService(name))
    let stopping = false
    let failed = false
    const stopAll = () => {
      if (stopping) {
        return
      }
      stopping = true
      signalTree(running, 'SIGINT')
      if (!IS_WINDOWS) {
        for (const entry of running) {
          if (SERVICES[entry.name].watchHost && !entry.exited) {
            endWatchHostAfterApp(entry)
          }
        }
        setTimeout(() => signalTree(running, 'SIGKILL'), STOP_GRACE_MS).unref()
      }
    }
    const settle = (entry, description) => {
      if (entry.exited) {
        return
      }
      entry.exited = true
      process.stdout.write(`${PROGRAM}: ${entry.name} ${description}\n`)
      if (!stopping) {
        failed = true
        stopAll()
      }
      if (running.every((item) => item.exited)) {
        resolve(failed ? EXIT_FAILED : EXIT_OK)
      }
    }
    for (const entry of running) {
      entry.child.once('error', (error) => settle(entry, `could not start (${error.code})`))
      entry.child.once('exit', (code, signal) => settle(entry, describeExit(entry, code, signal)))
    }
    for (const signal of ['SIGINT', 'SIGTERM', 'SIGHUP']) {
      process.on(signal, stopAll)
    }
  })
}

async function main(args) {
  let options = null
  try {
    options = parseArguments(args)
  } catch (error) {
    process.stderr.write(`${PROGRAM}: ${error.message}\n${USAGE}\n`)
    return EXIT_USAGE
  }
  if (options.help) {
    process.stdout.write(`${HELP}\n`)
    return EXIT_OK
  }
  const selected = options.only ?? [...SERVICE_NAMES]
  const problems = await runChecks(selected)
  if (problems.length > 0) {
    report(problems)
    return EXIT_FAILED
  }
  if (options.check) {
    process.stdout.write(`${PROGRAM}: all checks passed for ${selected.join(', ')}\n`)
    return EXIT_OK
  }
  process.stdout.write(
    `${PROGRAM}: starting ${selected.join(', ')}; press Ctrl+C to stop ${selected.length === 1 ? 'it' : 'all of them'}\n`,
  )
  return runServices(selected)
}

main(process.argv.slice(2)).then(
  (code) => {
    process.exitCode = code
  },
  (error) => {
    process.stderr.write(`${PROGRAM}: ${error.name} ${error.code ?? ''}\n`)
    process.exitCode = EXIT_FAILED
  },
)
