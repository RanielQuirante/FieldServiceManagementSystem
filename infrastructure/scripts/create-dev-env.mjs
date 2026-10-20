import { spawnSync } from 'node:child_process'
import { randomBytes } from 'node:crypto'
import fs from 'node:fs'
import os from 'node:os'
import path from 'node:path'

const EXIT_OK = 0
const EXIT_FAILED = 1
const EXIT_USAGE = 2

const PROGRAM = 'create-dev-env'

const COMMAND = 'node infrastructure/scripts/create-dev-env.mjs'

const USAGE = [
  `usage: ${COMMAND} [--bootstrap] [--database local|compose]`,
  '         [--admin-user <name>] [--only compose,notifications,api,e2e] [--force]',
  `       ${COMMAND} --ci`,
  `       ${COMMAND} --show-demo-password`,
].join('\n')

const HELP = [
  USAGE,
  '',
  'Creates the local secrets once and writes every untracked configuration store.',
  'It never replaces a value without --force, and it never prints one.',
  '',
  '  --bootstrap           also run infrastructure/postgres/bootstrap.sql on the local server',
  '  --database <target>   local (127.0.0.1:5432, the default) or compose (127.0.0.1:DB_HOST_PORT)',
  '  --admin-user <name>   superuser for --bootstrap and the test admin connection',
  '  --only <outputs>      write only some of compose, notifications, api and e2e',
  '  --force               replace every secret, then print the follow-up steps',
  '  --ci                  on a GitHub runner: the compose output, masked values in GITHUB_ENV',
  '  --show-demo-password  print the demo password and nothing else',
].join('\n')

const ROOT = path.resolve(import.meta.dirname, '..', '..')

const IS_WINDOWS = process.platform === 'win32'

const OUTPUT_NAMES = Object.freeze(['compose', 'notifications', 'api', 'e2e'])

const DATABASE_TARGETS = Object.freeze(['local', 'compose'])

const COMPOSE_FILE = 'infrastructure/.env'

const NOTIFICATIONS_FILE = 'src/fieldservice-notifications/.env'

const E2E_FILE = 'src/fieldservice-web/e2e/.env'

const BOOTSTRAP_FILE = 'infrastructure/postgres/bootstrap.sql'

const LOG_FOLDERS = Object.freeze(['logs/api', 'logs/notifications'])

const API_STORE = 'fieldservice-api'

const TEST_STORE = 'fieldservice-api-tests'

const PROJECTS = Object.freeze([
  { file: 'src/FieldService.Api/FieldService.Api.csproj', id: API_STORE },
  { file: 'tests/FieldService.Api.Tests/FieldService.Api.Tests.csproj', id: TEST_STORE },
])

const SECRETS_ID_OPEN = '<UserSecretsId>'

const SECRETS_ID_CLOSE = '</UserSecretsId>'

const STORE_BEGIN = '//BEGIN'

const STORE_END = '//END'

const STORE_MAX_BUFFER = 16777216

const LOOPBACK = '127.0.0.1'

const LOCAL_PORT = 5432

const COMPOSE_DEFAULT_PORT = 5433

const DATABASE_NAME = 'fieldservice'

const ADMIN_DATABASE = 'postgres'

const COMPOSE_ADMIN = 'postgres'

const WINDOWS_ADMIN = 'postgres'

const MINIMUM_PSQL_MAJOR = 15

const APP_KEY = 'ConnectionStrings:FieldService'

const MIGRATIONS_KEY = 'ConnectionStrings:Migrations'

const ADMIN_KEY = 'FIELDSERVICE_TEST_ADMIN_CONNECTION'

const DEMO_ALPHABET = 'abcdefghjkmnpqrstuvwxyz23456789'

const DEMO_GROUPS = 4

const DEMO_GROUP_LENGTH = 4

const KEY_BYTES = 32

const DATABASE_PASSWORD_BYTES = 24

const WEBHOOK_PREFIX = 'whsec_'

const SAFE_VALUE = new RegExp('^[A-Za-z0-9+/=_-]+$')

const ADMIN_NAME = new RegExp('^[A-Za-z0-9_][A-Za-z0-9_.@-]*$')

const ENV_KEY = new RegExp('^[A-Za-z_][A-Za-z0-9_]*$')

const LINE_BREAK = new RegExp('\\r?\\n')

const INLINE_COMMENT = new RegExp('\\s#')

const WHITESPACE = new RegExp('\\s')

const PSQL_VERSION = new RegExp('PostgreSQL\\)\\s+(\\d+)')

const PORT_TEXT = new RegExp('^\\d+$')

const DOTNET_ENVIRONMENT = Object.freeze({
  DOTNET_NOLOGO: 'true',
  DOTNET_CLI_TELEMETRY_OPTOUT: 'true',
})

const SECRET_NAMES = Object.freeze([
  'POSTGRES_PASSWORD',
  'FIELDSERVICE_MIGRATOR_PASSWORD',
  'FIELDSERVICE_APP_PASSWORD',
  'AUTH_ACCESS_TOKEN_KEY',
  'AUTH_SOCKET_TICKET_KEY',
  'NOTIFICATIONS_WEBHOOK_SECRET',
  'SEED_DEMO_PASSWORD',
])

const COMPOSE_SETTINGS = Object.freeze(['DB_HOST_PORT', 'NETWORK_SUBNET', 'WEB_IPV4_ADDRESS'])

const NOTIFICATIONS_DEFAULTS = Object.freeze([
  ['HOST', '127.0.0.1'],
  ['PORT', '5090'],
  ['NODE_ENV', 'development'],
  ['LOG_LEVEL', 'debug'],
  ['LOG_DIR', '../../logs/notifications'],
  ['LOG_PRETTY', 'true'],
  ['WEBHOOK_SECRETS', null],
  ['WEBHOOK_TOLERANCE_SECONDS', ''],
  ['SOCKET_TICKET_KEY', null],
  ['SOCKET_TICKET_ISSUER', ''],
  ['SOCKET_TICKET_AUDIENCE', ''],
  ['SOCKET_ALLOWED_ORIGINS', 'http://localhost:5173 http://127.0.0.1:5173'],
  ['SOCKET_SESSION_MAX_SECONDS', ''],
])

const GENERATORS = Object.freeze({
  POSTGRES_PASSWORD: databasePassword,
  FIELDSERVICE_MIGRATOR_PASSWORD: databasePassword,
  FIELDSERVICE_APP_PASSWORD: databasePassword,
  AUTH_ACCESS_TOKEN_KEY: signingKey,
  AUTH_SOCKET_TICKET_KEY: signingKey,
  NOTIFICATIONS_WEBHOOK_SECRET: webhookSecret,
  SEED_DEMO_PASSWORD: demoPassword,
})

const SECRET_SOURCES = Object.freeze({
  POSTGRES_PASSWORD: (s) => [fromMap(s.compose, 'POSTGRES_PASSWORD', COMPOSE_FILE)],
  FIELDSERVICE_MIGRATOR_PASSWORD: (s) => [
    fromMap(s.compose, 'FIELDSERVICE_MIGRATOR_PASSWORD', COMPOSE_FILE),
    fromMap(s.tests, 'FIELDSERVICE_MIGRATOR_PASSWORD', `user-secrets ${TEST_STORE}`),
    fromConnection(s.api, MIGRATIONS_KEY, `user-secrets ${API_STORE}`),
  ],
  FIELDSERVICE_APP_PASSWORD: (s) => [
    fromMap(s.compose, 'FIELDSERVICE_APP_PASSWORD', COMPOSE_FILE),
    fromMap(s.tests, 'FIELDSERVICE_APP_PASSWORD', `user-secrets ${TEST_STORE}`),
    fromConnection(s.api, APP_KEY, `user-secrets ${API_STORE}`),
  ],
  AUTH_ACCESS_TOKEN_KEY: (s) => [
    fromMap(s.compose, 'AUTH_ACCESS_TOKEN_KEY', COMPOSE_FILE),
    fromMap(s.api, 'Auth:AccessTokenKey', `user-secrets ${API_STORE}`),
  ],
  AUTH_SOCKET_TICKET_KEY: (s) => [
    fromMap(s.compose, 'AUTH_SOCKET_TICKET_KEY', COMPOSE_FILE),
    fromMap(s.api, 'Auth:SocketTicketKey', `user-secrets ${API_STORE}`),
    fromMap(s.notifications, 'SOCKET_TICKET_KEY', NOTIFICATIONS_FILE),
  ],
  NOTIFICATIONS_WEBHOOK_SECRET: (s) => [
    fromMap(s.compose, 'NOTIFICATIONS_WEBHOOK_SECRET', COMPOSE_FILE),
    fromMap(s.api, 'Notifications:WebhookSecret', `user-secrets ${API_STORE}`),
    {
      origin: NOTIFICATIONS_FILE,
      value: firstEntry(s.notifications?.get('WEBHOOK_SECRETS')),
    },
  ],
  SEED_DEMO_PASSWORD: (s) => [
    fromMap(s.compose, 'SEED_DEMO_PASSWORD', COMPOSE_FILE),
    fromMap(s.api, 'Seed:DemoPassword', `user-secrets ${API_STORE}`),
    fromMap(s.e2e, 'E2E_DEMO_PASSWORD', E2E_FILE),
  ],
})

class UsageError extends Error {}

class Failure extends Error {}

function databasePassword() {
  return randomBytes(DATABASE_PASSWORD_BYTES).toString('base64url')
}

function isPatterned(bytes) {
  const step = (bytes[1] - bytes[0] + 256) % 256
  for (let index = 2; index < bytes.length; index += 1) {
    if ((bytes[index] - bytes[index - 1] + 256) % 256 !== step) {
      return false
    }
  }
  return true
}

function keyBytes() {
  let bytes = randomBytes(KEY_BYTES)
  while (isPatterned(bytes)) {
    bytes = randomBytes(KEY_BYTES)
  }
  return bytes
}

function signingKey() {
  return keyBytes().toString('base64')
}

function webhookSecret() {
  return `${WEBHOOK_PREFIX}${keyBytes().toString('base64')}`
}

function demoPassword() {
  const length = DEMO_GROUPS * DEMO_GROUP_LENGTH
  const limit = 256 - (256 % DEMO_ALPHABET.length)
  const characters = []
  while (characters.length < length) {
    for (const byte of randomBytes(length * 2)) {
      if (byte < limit && characters.length < length) {
        characters.push(DEMO_ALPHABET[byte % DEMO_ALPHABET.length])
      }
    }
  }
  const groups = []
  for (let index = 0; index < length; index += DEMO_GROUP_LENGTH) {
    groups.push(characters.slice(index, index + DEMO_GROUP_LENGTH).join(''))
  }
  return groups.join('-')
}

function fromMap(map, key, origin) {
  return { origin, value: map?.get(key) }
}

function fromConnection(map, key, origin) {
  const text = map?.get(key)
  if (typeof text !== 'string') {
    return { origin, value: undefined }
  }
  const pairs = parseConnectionString(text)
  return { origin, value: pairs.get('password') ?? pairs.get('pwd') }
}

function firstEntry(text) {
  if (typeof text !== 'string') {
    return undefined
  }
  return text.trim().split(WHITESPACE)[0]
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

function connectionValue(value) {
  const special = [';', '"', "'", '='].some((mark) => value.includes(mark))
  if (!special && value === value.trim()) {
    return value
  }
  return `"${value.replaceAll('"', '""')}"`
}

function connectionString(parts) {
  return parts.map(([key, value]) => `${key}=${connectionValue(value)}`).join(';')
}

function appConnection(database, password) {
  return connectionString([
    ['Host', database.host],
    ['Port', String(database.port)],
    ['Database', DATABASE_NAME],
    ['Username', 'fieldservice_app'],
    ['Password', password],
    ['Maximum Pool Size', '20'],
    ['Application Name', 'fieldservice-api'],
  ])
}

function migratorConnection(database, password) {
  return connectionString([
    ['Host', database.host],
    ['Port', String(database.port)],
    ['Database', DATABASE_NAME],
    ['Username', 'fieldservice_migrator'],
    ['Password', password],
    ['Application Name', 'fieldservice-migrator'],
  ])
}

function sameAdminEndpoint(text, database) {
  const pairs = parseConnectionString(text)
  const host = (pairs.get('host') ?? pairs.get('server') ?? '').toLowerCase()
  const port = Number(pairs.get('port') ?? LOCAL_PORT)
  const user = pairs.get('username') ?? pairs.get('user id') ?? pairs.get('user') ?? ''
  const name = pairs.get('database') ?? pairs.get('db') ?? ''
  return (
    host === database.host &&
    port === database.port &&
    user === database.admin &&
    name === ADMIN_DATABASE
  )
}

function adminConnection(database, existing) {
  const endpoint = [
    ['Host', database.host],
    ['Port', String(database.port)],
    ['Username', database.admin],
  ]
  if (database.adminPassword !== null) {
    return connectionString([
      ...endpoint,
      ['Password', database.adminPassword],
      ['Database', ADMIN_DATABASE],
    ])
  }
  if (typeof existing === 'string' && sameAdminEndpoint(existing, database)) {
    return existing
  }
  if (!database.trustAuthentication) {
    return null
  }
  return connectionString([...endpoint, ['Database', ADMIN_DATABASE]])
}

function parseEnvLine(line) {
  let body = line.trim()
  if (body === '' || body.startsWith('#')) {
    return null
  }
  if (body.startsWith('export ')) {
    body = body.slice('export '.length).trimStart()
  }
  const equals = body.indexOf('=')
  if (equals <= 0) {
    return null
  }
  const key = body.slice(0, equals).trim()
  if (!ENV_KEY.test(key)) {
    return null
  }
  let value = body.slice(equals + 1).trim()
  const quote = value[0]
  if ((quote === '"' || quote === "'") && value.length >= 2 && value.endsWith(quote)) {
    value = value.slice(1, -1)
  } else {
    const comment = value.search(INLINE_COMMENT)
    if (comment !== -1) {
      value = value.slice(0, comment).trimEnd()
    }
  }
  return { key, value }
}

function readEnvFile(rel) {
  let text = null
  try {
    text = fs.readFileSync(path.join(ROOT, rel), 'utf8')
  } catch (error) {
    if (error.code === 'ENOENT') {
      return null
    }
    throw error
  }
  const values = new Map()
  const lines = new Map()
  for (const line of text.split(LINE_BREAK)) {
    const parsed = parseEnvLine(line)
    if (parsed !== null) {
      values.set(parsed.key, parsed.value)
      lines.set(parsed.key, line.trim())
    }
  }
  return { text, values, lines }
}

function envLine(key, value) {
  return `${key}=${WHITESPACE.test(value) ? `"${value}"` : value}`
}

function renderEnv(entries, existing) {
  const known = new Set(entries.map(([key]) => key))
  const lines = entries.map(([key, value]) => envLine(key, value))
  for (const [key, line] of existing?.lines ?? []) {
    if (!known.has(key)) {
      lines.push(line)
    }
  }
  return `${lines.join('\n')}\n`
}

function fixMode(file) {
  if (IS_WINDOWS || (fs.statSync(file).mode & 0o777) === 0o600) {
    return false
  }
  fs.chmodSync(file, 0o600)
  return true
}

function writePrivateFile(rel, content, existing) {
  const file = path.join(ROOT, rel)
  if (existing !== null && existing.text === content) {
    return { written: false, modeFixed: fixMode(file) }
  }
  fs.mkdirSync(path.dirname(file), { recursive: true })
  const temporary = `${file}.${process.pid}.tmp`
  const handle = fs.openSync(temporary, 'wx', 0o600)
  try {
    try {
      fs.writeFileSync(handle, content)
    } finally {
      fs.closeSync(handle)
    }
    if (!IS_WINDOWS) {
      fs.chmodSync(temporary, 0o600)
    }
    fs.renameSync(temporary, file)
  } catch (error) {
    fs.rmSync(temporary, { force: true })
    throw error
  }
  return { written: true, modeFixed: false }
}

function writeEnvOutput(rel, existing, entries) {
  const result = writePrivateFile(rel, renderEnv(entries, existing), existing)
  const mode = IS_WINDOWS ? '' : ', mode 0600'
  if (existing === null) {
    return [`${rel}: created${mode} (${entries.map(([key]) => key).join(', ')})`, true]
  }
  if (result.written) {
    const keys = entries.filter(([key, value]) => existing.values.get(key) !== value)
    const names = keys.length > 0 ? keys.map(([key]) => key).join(', ') : 'layout only'
    return [`${rel}: updated${mode} (${names})`, true]
  }
  if (result.modeFixed) {
    return [`${rel}: mode set to 0600`, true]
  }
  return [`${rel}: unchanged`, false]
}

function dotnetEnvironment() {
  return { ...process.env, ...DOTNET_ENVIRONMENT }
}

function readStore(id) {
  const result = spawnSync('dotnet', ['user-secrets', 'list', '--id', id, '--json'], {
    cwd: ROOT,
    encoding: 'utf8',
    env: dotnetEnvironment(),
    maxBuffer: STORE_MAX_BUFFER,
    windowsHide: true,
  })
  if (result.error) {
    const missing = result.error.code === 'ENOENT'
    throw new Failure(
      missing
        ? 'dotnet was not found on PATH; install the .NET SDK (docs/development.md) or leave out the api output with --only'
        : `dotnet user-secrets list --id ${id} could not start (${result.error.code})`,
    )
  }
  if (result.status !== 0) {
    throw new Failure(`dotnet user-secrets list --id ${id} failed with exit code ${result.status}`)
  }
  const begin = result.stdout.indexOf(STORE_BEGIN)
  const end = begin === -1 ? -1 : result.stdout.indexOf(STORE_END, begin)
  if (end === -1) {
    throw new Failure(`dotnet user-secrets list --id ${id} printed no JSON block`)
  }
  let data = null
  try {
    const json = result.stdout.slice(begin + STORE_BEGIN.length, end).trim()
    data = json === '' ? {} : JSON.parse(json)
  } catch {
    throw new Failure(`the user-secrets store ${id} could not be parsed`)
  }
  const store = new Map()
  for (const [key, value] of Object.entries(data ?? {})) {
    if (typeof value === 'string') {
      store.set(key, value)
    }
  }
  return store
}

function setStore(id, entries) {
  const result = spawnSync('dotnet', ['user-secrets', 'set', '--id', id], {
    cwd: ROOT,
    input: JSON.stringify(Object.fromEntries(entries)),
    encoding: 'utf8',
    env: dotnetEnvironment(),
    maxBuffer: STORE_MAX_BUFFER,
    windowsHide: true,
  })
  if (result.error) {
    throw new Failure(`dotnet user-secrets set --id ${id} could not start (${result.error.code})`)
  }
  if (result.status !== 0) {
    throw new Failure(
      `dotnet user-secrets set --id ${id} failed with exit code ${result.status}; ` +
        'its output is not shown, because it may contain a value',
    )
  }
}

function writeStore(id, entries, current) {
  const changes = entries.filter(([key, value]) => current.get(key) !== value)
  if (changes.length === 0) {
    return [`user-secrets ${id}: unchanged`, false]
  }
  setStore(id, changes)
  return [`user-secrets ${id}: set ${changes.map(([key]) => key).join(', ')}`, true]
}

function checkProjects() {
  const warnings = []
  for (const project of PROJECTS) {
    let text = null
    try {
      text = fs.readFileSync(path.join(ROOT, project.file), 'utf8')
    } catch (error) {
      if (error.code === 'ENOENT') {
        continue
      }
      throw error
    }
    const start = text.indexOf(SECRETS_ID_OPEN)
    const end = start === -1 ? -1 : text.indexOf(SECRETS_ID_CLOSE, start)
    if (end === -1) {
      warnings.push(`${project.file} declares no UserSecretsId; it should declare ${project.id}`)
      continue
    }
    const declared = text.slice(start + SECRETS_ID_OPEN.length, end).trim()
    if (declared !== project.id) {
      throw new Failure(
        `${project.file} declares UserSecretsId ${declared}, but this script writes the store ` +
          `${project.id}, so the project would read another store; make both name ${project.id}`,
      )
    }
  }
  return warnings
}

function checkPsql() {
  const result = spawnSync('psql', ['--version'], { encoding: 'utf8', windowsHide: true })
  if (result.error || result.status !== 0) {
    throw new Failure(
      '--bootstrap needs psql on PATH: Homebrew postgresql@17 on macOS, ' +
        'or the PostgreSQL bin folder on Windows',
    )
  }
  const match = PSQL_VERSION.exec(result.stdout)
  if (match === null || Number(match[1]) < MINIMUM_PSQL_MAJOR) {
    throw new Failure(`--bootstrap needs psql ${MINIMUM_PSQL_MAJOR} or later, for \\getenv`)
  }
}

function runBootstrap(admin, values) {
  process.stdout.write(
    `${PROGRAM}: running ${BOOTSTRAP_FILE} on ${LOOPBACK}:${LOCAL_PORT} as ${admin}\n`,
  )
  const args = ['-X', '-h', LOOPBACK, '-p', String(LOCAL_PORT), '-U', admin, '-d', ADMIN_DATABASE]
  const result = spawnSync('psql', [...args, '-v', 'ON_ERROR_STOP=1', '-f', BOOTSTRAP_FILE], {
    cwd: ROOT,
    env: {
      ...process.env,
      FIELDSERVICE_MIGRATOR_PASSWORD: values.get('FIELDSERVICE_MIGRATOR_PASSWORD'),
      FIELDSERVICE_APP_PASSWORD: values.get('FIELDSERVICE_APP_PASSWORD'),
    },
    stdio: 'inherit',
    windowsHide: true,
  })
  if (result.error) {
    throw new Failure(`psql could not start (${result.error.code})`)
  }
  if (result.status !== 0) {
    throw new Failure(`psql exited with code ${result.status} while running ${BOOTSTRAP_FILE}`)
  }
  process.stdout.write(`${PROGRAM}: ${BOOTSTRAP_FILE} ran on the local server\n`)
}

function defaultAdmin() {
  if (IS_WINDOWS) {
    return WINDOWS_ADMIN
  }
  const name = os.userInfo().username
  if (!ADMIN_NAME.test(name)) {
    throw new Failure('the account name cannot serve as the admin user; pass --admin-user <name>')
  }
  return name
}

function composePort(composeFile) {
  const text = composeFile?.values.get('DB_HOST_PORT') ?? ''
  if (text === '') {
    return COMPOSE_DEFAULT_PORT
  }
  const port = Number(text)
  if (!PORT_TEXT.test(text) || port < 1024 || port > 65535) {
    throw new Failure(`DB_HOST_PORT in ${COMPOSE_FILE} must be a port from 1024 to 65535`)
  }
  return port
}

function databaseTarget(target, admin, composeFile, values) {
  if (target === 'compose') {
    return {
      target,
      host: LOOPBACK,
      port: composePort(composeFile),
      admin: COMPOSE_ADMIN,
      adminPassword: values.get('POSTGRES_PASSWORD'),
      trustAuthentication: false,
    }
  }
  const pgpassword = process.env.PGPASSWORD ?? ''
  return {
    target,
    host: LOOPBACK,
    port: LOCAL_PORT,
    admin,
    adminPassword: pgpassword === '' ? null : pgpassword,
    trustAuthentication: !IS_WINDOWS,
  }
}

function uniqueValue(create, taken) {
  let value = create()
  while ([...taken.values()].includes(value)) {
    value = create()
  }
  return value
}

function hasValue(item) {
  return typeof item.value === 'string' && item.value.trim() !== ''
}

function resolveSecrets(sources, force) {
  const values = new Map()
  const generated = []
  for (const name of SECRET_NAMES) {
    const found = force ? undefined : SECRET_SOURCES[name](sources).find(hasValue)
    if (found === undefined) {
      values.set(name, uniqueValue(GENERATORS[name], values))
      generated.push(name)
      continue
    }
    if (!SAFE_VALUE.test(found.value)) {
      throw new Failure(
        `${name} in ${found.origin} holds characters the outputs cannot carry; ` +
          'correct it there, or replace every secret with --force',
      )
    }
    values.set(name, found.value)
  }
  return { values, generated }
}

function composeEntries(values, existing) {
  return [
    ...COMPOSE_SETTINGS.map((key) => [key, existing?.values.get(key) ?? '']),
    ...SECRET_NAMES.map((name) => [name, values.get(name)]),
  ]
}

function notificationsEntries(values, existing) {
  const webhook = values.get('NOTIFICATIONS_WEBHOOK_SECRET')
  return NOTIFICATIONS_DEFAULTS.map(([key, fallback]) => {
    if (key === 'WEBHOOK_SECRETS') {
      const current = existing?.values.get(key) ?? ''
      const keep = firstEntry(current) === webhook && current.split(WHITESPACE).every(isSafe)
      return [key, keep ? current : webhook]
    }
    if (key === 'SOCKET_TICKET_KEY') {
      return [key, values.get('AUTH_SOCKET_TICKET_KEY')]
    }
    return [key, existing?.values.has(key) ? existing.values.get(key) : fallback]
  })
}

function isSafe(text) {
  return text === '' || SAFE_VALUE.test(text)
}

function e2eEntries(values) {
  return [['E2E_DEMO_PASSWORD', values.get('SEED_DEMO_PASSWORD')]]
}

function apiStoreEntries(values, database) {
  return [
    [APP_KEY, appConnection(database, values.get('FIELDSERVICE_APP_PASSWORD'))],
    [MIGRATIONS_KEY, migratorConnection(database, values.get('FIELDSERVICE_MIGRATOR_PASSWORD'))],
    ['Auth:AccessTokenKey', values.get('AUTH_ACCESS_TOKEN_KEY')],
    ['Auth:SocketTicketKey', values.get('AUTH_SOCKET_TICKET_KEY')],
    ['Notifications:WebhookSecret', values.get('NOTIFICATIONS_WEBHOOK_SECRET')],
    ['Seed:DemoPassword', values.get('SEED_DEMO_PASSWORD')],
  ]
}

function testStoreEntries(values, database, current) {
  const entries = []
  const admin = adminConnection(database, current.get(ADMIN_KEY))
  if (admin !== null) {
    entries.push([ADMIN_KEY, admin])
  }
  entries.push(
    ['FIELDSERVICE_MIGRATOR_PASSWORD', values.get('FIELDSERVICE_MIGRATOR_PASSWORD')],
    ['FIELDSERVICE_APP_PASSWORD', values.get('FIELDSERVICE_APP_PASSWORD')],
  )
  return entries
}

function ensureFolder(rel) {
  const created = fs.mkdirSync(path.join(ROOT, rel), { recursive: true })
  return created === undefined ? [`${rel}: exists`, false] : [`${rel}: created`, true]
}

function runnerEntries(values, database) {
  return [
    [ADMIN_KEY, adminConnection(database, undefined)],
    ['FIELDSERVICE_MIGRATOR_PASSWORD', values.get('FIELDSERVICE_MIGRATOR_PASSWORD')],
    ['FIELDSERVICE_APP_PASSWORD', values.get('FIELDSERVICE_APP_PASSWORD')],
    ['E2E_DEMO_PASSWORD', values.get('SEED_DEMO_PASSWORD')],
  ]
}

function maskForRunner(values, database) {
  const masked = new Set([
    ...SECRET_NAMES.map((name) => values.get(name)),
    adminConnection(database, undefined),
  ])
  for (const value of masked) {
    process.stdout.write(`::add-mask::${value}\n`)
  }
}

function appendRunnerEnvironment(values, database) {
  const entries = runnerEntries(values, database)
  const text = entries.map(([key, value]) => `${key}=${value}\n`).join('')
  fs.appendFileSync(process.env.GITHUB_ENV, text)
  return [`GITHUB_ENV: appended ${entries.map(([key]) => key).join(', ')}`, true]
}

function say(line) {
  process.stdout.write(`${line}\n`)
}

function describeTarget(database) {
  return `${database.target} (${database.host}:${database.port}, admin ${database.admin})`
}

function createEnvironment(options) {
  const outputs = options.ci ? ['compose'] : (options.only ?? [...OUTPUT_NAMES])
  const target = options.ci ? 'compose' : (options.database ?? 'local')
  if (options.ci && !process.env.GITHUB_ENV) {
    throw new Failure('GITHUB_ENV is not set; --ci runs only on a GitHub Actions runner')
  }
  const admin = options.adminUser ?? (target === 'local' ? defaultAdmin() : COMPOSE_ADMIN)
  const warnings = outputs.includes('api') ? checkProjects() : []
  if (options.bootstrap) {
    checkPsql()
  }
  const files = {
    compose: readEnvFile(COMPOSE_FILE),
    notifications: readEnvFile(NOTIFICATIONS_FILE),
    e2e: readEnvFile(E2E_FILE),
  }
  const withApi = outputs.includes('api')
  const stores = {
    api: withApi ? readStore(API_STORE) : null,
    tests: withApi ? readStore(TEST_STORE) : null,
  }
  const { values, generated } = resolveSecrets(
    {
      compose: files.compose?.values ?? null,
      notifications: files.notifications?.values ?? null,
      e2e: files.e2e?.values ?? null,
      api: stores.api,
      tests: stores.tests,
    },
    options.force,
  )
  const database = databaseTarget(target, admin, files.compose, values)
  if (options.ci) {
    maskForRunner(values, database)
  }
  say(`${PROGRAM}: outputs ${outputs.join(', ')}; database ${describeTarget(database)}`)
  const kept = SECRET_NAMES.filter((name) => !generated.includes(name))
  say(`${PROGRAM}: generated ${generated.length > 0 ? generated.join(', ') : 'nothing'}`)
  if (kept.length > 0) {
    say(`${PROGRAM}: kept ${kept.join(', ')}`)
  }
  let changed = false
  const report = ([line, didChange]) => {
    say(`  ${line}`)
    changed = changed || didChange
  }
  if (outputs.includes('compose')) {
    report(writeEnvOutput(COMPOSE_FILE, files.compose, composeEntries(values, files.compose)))
  }
  if (outputs.includes('notifications')) {
    const entries = notificationsEntries(values, files.notifications)
    report(writeEnvOutput(NOTIFICATIONS_FILE, files.notifications, entries))
  }
  if (outputs.includes('e2e')) {
    report(writeEnvOutput(E2E_FILE, files.e2e, e2eEntries(values)))
  }
  let adminMissing = false
  if (withApi) {
    report(writeStore(API_STORE, apiStoreEntries(values, database), stores.api))
    const testEntries = testStoreEntries(values, database, stores.tests)
    adminMissing = !testEntries.some(([key]) => key === ADMIN_KEY) && !stores.tests.has(ADMIN_KEY)
    report(writeStore(TEST_STORE, testEntries, stores.tests))
  }
  for (const folder of LOG_FOLDERS) {
    report(ensureFolder(folder))
  }
  if (options.ci) {
    report(appendRunnerEnvironment(values, database))
  }
  for (const warning of warnings) {
    say(`${PROGRAM}: warning: ${warning}`)
  }
  if (adminMissing) {
    say(`${PROGRAM}: ${ADMIN_KEY} was not written, because the admin password is unknown.`)
    say('  Set PGPASSWORD and run again, or run this with your postgres password:')
    say(
      `  dotnet user-secrets set ${ADMIN_KEY} "Host=${LOOPBACK};Port=${LOCAL_PORT};` +
        `Username=${admin};Password=<postgres password>;Database=${ADMIN_DATABASE}" --id ${TEST_STORE}`,
    )
  }
  if (!outputs.includes('compose') && files.compose === null) {
    say(`${PROGRAM}: note: ${COMPOSE_FILE} was not written, so --show-demo-password has no value`)
  }
  if (options.bootstrap) {
    runBootstrap(admin, values)
  }
  if (options.force) {
    say(`${PROGRAM}: every secret was replaced. Next steps:`)
    say(
      options.bootstrap
        ? '  1. the local server already has the new role passwords (--bootstrap ran)'
        : `  1. give the local server the new role passwords: ${COMMAND} --bootstrap`,
    )
    say(
      '  2. reset the Compose database: docker compose -f infrastructure/docker-compose.yml ' +
        'down -v, then up -d --wait db',
    )
    say('  3. reset the demo data, since the seeder keeps old hashes: VS Code task db: reset')
  }
  say(
    changed
      ? `${PROGRAM}: done; no value was printed`
      : `${PROGRAM}: no changes; every output already holds its values`,
  )
  return EXIT_OK
}

function showDemoPassword() {
  const value = readEnvFile(COMPOSE_FILE)?.values.get('SEED_DEMO_PASSWORD') ?? ''
  if (value === '') {
    throw new Failure(`${COMPOSE_FILE} holds no SEED_DEMO_PASSWORD; run ${COMMAND} first`)
  }
  process.stdout.write(`${value}\n`)
  return EXIT_OK
}

function optionValue(args, index, name, inline) {
  const value = inline ?? args[index]
  if (value === undefined || value === '' || (inline === null && value.startsWith('-'))) {
    throw new UsageError(`${name} needs a value`)
  }
  return value
}

function parseArguments(args) {
  const options = {
    bootstrap: false,
    database: null,
    adminUser: null,
    only: null,
    force: false,
    ci: false,
    showDemoPassword: false,
    help: false,
  }
  const flags = new Map([
    ['--bootstrap', 'bootstrap'],
    ['--force', 'force'],
    ['--ci', 'ci'],
    ['--show-demo-password', 'showDemoPassword'],
    ['--help', 'help'],
    ['-h', 'help'],
  ])
  for (let index = 0; index < args.length; index += 1) {
    const arg = args[index]
    const equals = arg.startsWith('--') ? arg.indexOf('=') : -1
    const name = equals === -1 ? arg : arg.slice(0, equals)
    const inline = equals === -1 ? null : arg.slice(equals + 1)
    if (flags.has(name)) {
      if (inline !== null) {
        throw new UsageError(`${name} takes no value`)
      }
      options[flags.get(name)] = true
      continue
    }
    if (name !== '--database' && name !== '--admin-user' && name !== '--only') {
      throw new UsageError(`unknown argument ${JSON.stringify(arg)}`)
    }
    if (inline === null) {
      index += 1
    }
    const value = optionValue(args, index, name, inline)
    if (name === '--database') {
      if (!DATABASE_TARGETS.includes(value)) {
        throw new UsageError(`--database takes ${DATABASE_TARGETS.join(' or ')}`)
      }
      options.database = value
    } else if (name === '--admin-user') {
      if (!ADMIN_NAME.test(value)) {
        throw new UsageError('--admin-user takes a role name of letters, digits, _ . @ and -')
      }
      options.adminUser = value
    } else {
      const names = value.split(',').map((item) => item.trim())
      const unknown = names.filter((item) => !OUTPUT_NAMES.includes(item)).join(', ')
      if (unknown !== '') {
        throw new UsageError(`unknown output ${unknown}; use ${OUTPUT_NAMES.join(', ')}`)
      }
      options.only = OUTPUT_NAMES.filter((item) => names.includes(item))
    }
  }
  if (options.help) {
    return options
  }
  if (options.showDemoPassword && args.length !== 1) {
    throw new UsageError('--show-demo-password takes no other argument')
  }
  const { bootstrap, force, database, adminUser, only } = options
  if (options.ci && (bootstrap || force || [database, adminUser, only].some((item) => item))) {
    throw new UsageError('--ci takes no other argument')
  }
  if (options.bootstrap && options.database === 'compose') {
    throw new UsageError(
      '--bootstrap runs on the local server; the Compose db runs bootstrap.sql on its first start',
    )
  }
  if (options.force && options.only !== null && !options.only.includes('compose')) {
    throw new UsageError(
      '--force with --only needs the compose output, which holds the values the others copy',
    )
  }
  return options
}

function describe(error) {
  if (error instanceof Failure) {
    return error.message
  }
  if (error !== null && typeof error === 'object' && typeof error.syscall === 'string') {
    const where = typeof error.path === 'string' ? ` on ${path.relative(ROOT, error.path)}` : ''
    return `${error.code ?? 'error'} during ${error.syscall}${where}`
  }
  return `unexpected ${error?.name ?? 'error'}; no value was printed`
}

function main(args) {
  let options = null
  try {
    options = parseArguments(args)
  } catch (error) {
    if (!(error instanceof UsageError)) {
      throw error
    }
    process.stderr.write(`${PROGRAM}: ${error.message}\n${USAGE}\n`)
    return EXIT_USAGE
  }
  if (options.help) {
    process.stdout.write(`${HELP}\n`)
    return EXIT_OK
  }
  try {
    return options.showDemoPassword ? showDemoPassword() : createEnvironment(options)
  } catch (error) {
    process.stderr.write(`${PROGRAM}: ${describe(error)}\n`)
    return EXIT_FAILED
  }
}

process.exitCode = main(process.argv.slice(2))
