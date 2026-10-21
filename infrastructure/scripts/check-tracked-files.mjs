import { spawnSync } from 'node:child_process'
import { createHmac, timingSafeEqual } from 'node:crypto'
import path from 'node:path'

const EXIT_CLEAN = 0
const EXIT_FOUND = 1
const EXIT_ERROR = 2

const PROGRAM = 'check-tracked-files'

const USAGE = 'usage: node infrastructure/scripts/check-tracked-files.mjs'

const HELP = [
  USAGE,
  '',
  'Fails when a tracked path must never be committed, or when a tracked file holds a',
  'webhook secret, a JWT or a private-key header other than the published test values of',
  'docs/contracts/events/signature-test-vector.json and socket-ticket-test-vector.json.',
  'Prints one line per finding: "path kind" for paths, "path:line kind" for content.',
  'Exit codes: 0 clean, 1 findings, 2 usage or git error.',
].join('\n')

const GIT_MAX_BUFFER = 1073741824

const BLOB_BATCH = 64

const GITLINK_MODE = '160000'

const GIT_ENVIRONMENT = Object.freeze({ GIT_OPTIONAL_LOCKS: '0', GIT_NO_REPLACE_OBJECTS: '1' })

const SIGNATURE_VECTOR = 'docs/contracts/events/signature-test-vector.json'

const SOCKET_TICKET_VECTOR = 'docs/contracts/events/socket-ticket-test-vector.json'

const WEBHOOK_PREFIX = 'whsec_'

const GENERATED_CLIENT = 'src/FieldService.Web/src/shared/api/schema.gen.ts'

const LOG_FOLDER = 'logs'

const LOG_KEEP = 'logs/.gitkeep'

const ENV_BASE = '.env'

const ENV_EXAMPLE = '.env.example'

const BUILD_SEGMENTS = new Set([
  'bin',
  'obj',
  'node_modules',
  'dist',
  'testresults',
  'coverage',
  'test-results',
  'playwright-report',
  'artifacts',
])

const KEY_EXTENSIONS = new Set(['.pfx', '.p12', '.pem', '.key', '.keystore', '.jks'])

const SHELL_EXTENSIONS = new Set(['.sh', '.bash', '.zsh', '.ps1', '.psm1', '.bat', '.cmd'])

const BINARY_EXTENSIONS = new Set(['.apk', '.aab', '.msix', '.exe', '.dll'])

const VSCODE_FOLDER = '.vscode'

const VSCODE_SHARED = new Set(['extensions.json', 'launch.json', 'tasks.json'])

const CONTENT_RULES = Object.freeze([
  { kind: 'webhook-secret', source: 'whsec_[A-Za-z0-9+/]{20,}={0,2}' },
  { kind: 'jwt', source: 'eyJ[A-Za-z0-9_-]{5,}\\.[A-Za-z0-9_-]{5,}\\.[A-Za-z0-9_-]*' },
  { kind: 'private-key', source: '-----BEGIN [A-Z ]*PRIVATE KEY-----' },
])

class UsageError extends Error {}

class GitError extends Error {}

function runGit(args, cwd, input) {
  const result = spawnSync('git', args, {
    cwd,
    input,
    env: { ...process.env, ...GIT_ENVIRONMENT },
    maxBuffer: GIT_MAX_BUFFER,
    windowsHide: true,
  })
  const command = `git ${args.join(' ')}`
  if (result.error) {
    throw new GitError(`${command}: ${result.error.code ?? result.error.message}`)
  }
  if (result.signal) {
    throw new GitError(`${command}: terminated by ${result.signal}`)
  }
  if (result.status !== 0) {
    const detail = result.stderr.toString('utf8').split('\n')[0].trim()
    throw new GitError(`${command}: ${detail === '' ? `exit ${result.status}` : detail}`)
  }
  return result.stdout
}

function repositoryTop() {
  const top = runGit(['rev-parse', '--show-toplevel'], process.cwd()).toString('utf8').trim()
  if (top === '') {
    throw new GitError('the current folder is not inside a git work tree')
  }
  return top
}

function trackedEntries(top) {
  const output = runGit(['ls-files', '-z', '--stage'], top).toString('utf8')
  const entries = new Map()
  for (const record of output.split('\0')) {
    if (record === '') {
      continue
    }
    const tab = record.indexOf('\t')
    const [mode, object, stage] = record.slice(0, tab).split(' ')
    const file = record.slice(tab + 1)
    if (!entries.has(file) || stage === '0') {
      entries.set(file, { file, mode, object })
    }
  }
  return [...entries.values()].sort((left, right) => compareText(left.file, right.file))
}

function compareText(left, right) {
  if (left < right) {
    return -1
  }
  return left > right ? 1 : 0
}

function readBlobs(top, objects) {
  const blobs = new Map()
  const unique = [...new Set(objects)]
  for (let start = 0; start < unique.length; start += BLOB_BATCH) {
    const batch = unique.slice(start, start + BLOB_BATCH)
    const output = runGit(['cat-file', '--batch'], top, `${batch.join('\n')}\n`)
    let offset = 0
    for (const object of batch) {
      const newline = output.indexOf(0x0a, offset)
      const header = output.subarray(offset, newline).toString('utf8').split(' ')
      if (header[1] === 'missing' || header.length < 3) {
        throw new GitError(`git cat-file --batch: object ${object} is missing`)
      }
      const size = Number(header[2])
      const body = output.subarray(newline + 1, newline + 1 + size)
      blobs.set(object, body)
      offset = newline + 1 + size + 1
    }
  }
  return blobs
}

function pathKinds(file) {
  const segments = file.split('/')
  const folders = segments.slice(0, -1).map((segment) => segment.toLowerCase())
  const base = segments[segments.length - 1]
  const lowerBase = base.toLowerCase()
  const extension = path.posix.extname(lowerBase)
  const kinds = []
  const underLogs = segments.length > 1 && folders[0] === LOG_FOLDER && file !== LOG_KEEP
  if (extension === '.log' || underLogs) {
    kinds.push('log-file')
  }
  const envFile = lowerBase === ENV_BASE || lowerBase.startsWith(`${ENV_BASE}.`)
  if (envFile && lowerBase !== ENV_EXAMPLE) {
    kinds.push('env-file')
  }
  if (folders.some((segment) => BUILD_SEGMENTS.has(segment))) {
    kinds.push('build-output')
  }
  if (file === GENERATED_CLIENT) {
    kinds.push('generated-client')
  }
  if (KEY_EXTENSIONS.has(extension) || lowerBase === 'secrets.json') {
    kinds.push('key-material')
  }
  const vscodeIndex = folders.indexOf(VSCODE_FOLDER)
  const sharedVscode = segments.length === 2 && vscodeIndex === 0 && VSCODE_SHARED.has(base)
  const ideState =
    extension === '.user' ||
    folders.includes('.vs') ||
    lowerBase === '.ds_store' ||
    (vscodeIndex !== -1 && !sharedVscode)
  if (ideState) {
    kinds.push('ide-state')
  }
  if (SHELL_EXTENSIONS.has(extension)) {
    kinds.push('shell-script')
  }
  if (BINARY_EXTENSIONS.has(extension)) {
    kinds.push('packaged-binary')
  }
  if (extension === '.txt' && !base.startsWith('LICENSE') && base !== 'OFL.txt') {
    kinds.push('plain-text')
  }
  return kinds
}

function parseVector(file, blob) {
  if (blob === undefined) {
    return null
  }
  try {
    const value = JSON.parse(blob.toString('utf8'))
    if (value === null || typeof value !== 'object' || Array.isArray(value)) {
      throw new TypeError('not an object')
    }
    return value
  } catch {
    process.stderr.write(`${PROGRAM}: ${file} is not a valid test vector; it allows nothing\n`)
    return null
  }
}

function decodedKey(text) {
  if (typeof text !== 'string' || text === '') {
    return null
  }
  const bytes = Buffer.from(text, 'base64')
  return bytes.length === 0 ? null : bytes
}

function publishedValues(entries, blobs) {
  const byFile = new Map(entries.map((entry) => [entry.file, entry]))
  const blobOf = (file) => {
    const entry = byFile.get(file)
    return entry === undefined ? undefined : blobs.get(entry.object)
  }
  const secrets = new Set()
  const keys = []
  const signature = parseVector(SIGNATURE_VECTOR, blobOf(SIGNATURE_VECTOR))
  if (signature !== null && typeof signature.secret === 'string') {
    if (signature.secret.startsWith(WEBHOOK_PREFIX)) {
      secrets.add(signature.secret)
      const key = decodedKey(signature.secret.slice(WEBHOOK_PREFIX.length))
      if (key !== null) {
        keys.push(key)
      }
    }
  }
  const ticket = parseVector(SOCKET_TICKET_VECTOR, blobOf(SOCKET_TICKET_VECTOR))
  if (ticket !== null) {
    const key = decodedKey(ticket.key)
    if (key !== null) {
      keys.push(key)
    }
  }
  return { secrets, keys }
}

function equalText(left, right) {
  const a = Buffer.from(left, 'utf8')
  const b = Buffer.from(right, 'utf8')
  return a.length === b.length && timingSafeEqual(a, b)
}

function isPublishedToken(token, keys) {
  const parts = token.split('.')
  if (parts.length !== 3 || parts[2] === '') {
    return false
  }
  let header = null
  try {
    header = JSON.parse(Buffer.from(parts[0], 'base64url').toString('utf8'))
  } catch {
    return false
  }
  if (header === null || typeof header !== 'object' || header.alg !== 'HS256') {
    return false
  }
  const signingInput = `${parts[0]}.${parts[1]}`
  let verified = false
  for (const key of keys) {
    const expected = createHmac('sha256', key).update(signingInput).digest('base64url')
    if (equalText(parts[2], expected)) {
      verified = true
    }
  }
  return verified
}

function isAllowed(kind, text, published) {
  if (kind === 'webhook-secret') {
    return [...published.secrets].some((secret) => equalText(text, secret))
  }
  if (kind === 'jwt') {
    return isPublishedToken(text, published.keys)
  }
  return false
}

function contentFindings(file, blob, published) {
  if (blob.includes(0)) {
    return []
  }
  const findings = []
  const lines = blob.toString('utf8').split('\n')
  for (let index = 0; index < lines.length; index += 1) {
    for (const rule of CONTENT_RULES) {
      for (const match of lines[index].matchAll(new RegExp(rule.source, 'g'))) {
        if (!isAllowed(rule.kind, match[0], published)) {
          findings.push(`${file}:${index + 1} ${rule.kind}`)
        }
      }
    }
  }
  return findings
}

function scan() {
  const top = repositoryTop()
  const entries = trackedEntries(top)
  const pathLines = []
  for (const entry of entries) {
    for (const kind of pathKinds(entry.file)) {
      pathLines.push(`${entry.file} ${kind}`)
    }
  }
  const readable = entries.filter((entry) => entry.mode !== GITLINK_MODE)
  const blobs = readBlobs(
    top,
    readable.map((entry) => entry.object),
  )
  const published = publishedValues(readable, blobs)
  const contentLines = []
  for (const entry of readable) {
    contentLines.push(...contentFindings(entry.file, blobs.get(entry.object), published))
  }
  return [...pathLines, ...contentLines]
}

function parseArguments(args) {
  if (args.length === 0) {
    return { help: false }
  }
  if (args.length === 1 && (args[0] === '--help' || args[0] === '-h')) {
    return { help: true }
  }
  throw new UsageError(`unexpected argument ${JSON.stringify(args[0])}`)
}

function main(args) {
  try {
    if (parseArguments(args).help) {
      process.stdout.write(`${HELP}\n`)
      return EXIT_CLEAN
    }
    const lines = scan()
    if (lines.length === 0) {
      return EXIT_CLEAN
    }
    process.stdout.write(`${lines.join('\n')}\n`)
    return EXIT_FOUND
  } catch (error) {
    const usage = error instanceof UsageError ? `\n${USAGE}` : ''
    const known = error instanceof UsageError || error instanceof GitError
    const message = known ? error.message : `${error.name} ${error.code ?? ''}`.trim()
    process.stderr.write(`${PROGRAM}: ${message}${usage}\n`)
    return EXIT_ERROR
  }
}

process.stdout.on('error', (error) => {
  if (error.code !== 'EPIPE') {
    process.exitCode = EXIT_ERROR
  }
})

process.exitCode = main(process.argv.slice(2))
