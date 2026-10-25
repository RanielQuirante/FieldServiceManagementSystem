import { existsSync, readdirSync, readFileSync, rmSync, statSync } from 'node:fs'
import { basename, dirname, extname, isAbsolute, join, relative, resolve, sep } from 'node:path'
import process from 'node:process'
import { inflateRawSync } from 'node:zlib'

import type { FullConfig, FullResult, Reporter } from '@playwright/test/reporter'

import { demoPassword } from './api.ts'
import { takeReportSecrets, withArticle, type Sentinel } from './sentinels.ts'

interface ScannedEntry {
  readonly name: string
  readonly text: string
}

interface SecretFile {
  readonly path: string
  readonly file: string
  readonly kind: string
  readonly found: string
}

interface FolderReport {
  readonly folder: string
  readonly files: number
  readonly entries: number
  readonly hits: readonly SecretFile[]
  readonly unreadable: readonly string[]
  readonly control: number
}

interface Removal {
  readonly deleted: number
  readonly problems: readonly string[]
}

const centralDirectorySignature = 0x02014b50

const endOfCentralDirectorySignature = 0x06054b50

const embeddedReport =
  /<template id="playwrightReportBase64">data:application\/zip;base64,([A-Za-z0-9+/=]+)<\/template>/

const markupEntities: ReadonlyArray<readonly [RegExp, string]> = [
  [/&lt;/g, '<'],
  [/&gt;/g, '>'],
  [/&quot;/g, '"'],
  [/&#39;|&apos;/g, "'"],
  [/&amp;/g, '&'],
]

const skippedFolders: ReadonlySet<string> = new Set(['sentinels'])

const screenshotExtensions: ReadonlySet<string> = new Set(['.png', '.jpg', '.jpeg'])

function endOfCentralDirectory(buffer: Buffer): number {
  for (
    let offset = buffer.length - 22;
    offset >= Math.max(0, buffer.length - 65_557);
    offset -= 1
  ) {
    if (buffer.readUInt32LE(offset) === endOfCentralDirectorySignature) {
      return offset
    }
  }
  return -1
}

export function zipEntries(buffer: Buffer): ScannedEntry[] {
  const end = endOfCentralDirectory(buffer)
  if (end < 0) {
    throw new Error('no end of central directory')
  }
  const count = buffer.readUInt16LE(end + 10)
  let offset = buffer.readUInt32LE(end + 16)
  const entries: ScannedEntry[] = []
  for (let index = 0; index < count; index += 1) {
    if (buffer.readUInt32LE(offset) !== centralDirectorySignature) {
      throw new Error('a broken central directory')
    }
    const method = buffer.readUInt16LE(offset + 10)
    const compressedSize = buffer.readUInt32LE(offset + 20)
    const nameLength = buffer.readUInt16LE(offset + 28)
    const extraLength = buffer.readUInt16LE(offset + 30)
    const commentLength = buffer.readUInt16LE(offset + 32)
    const localOffset = buffer.readUInt32LE(offset + 42)
    const name = buffer.toString('utf8', offset + 46, offset + 46 + nameLength)
    const start =
      localOffset +
      30 +
      buffer.readUInt16LE(localOffset + 26) +
      buffer.readUInt16LE(localOffset + 28)
    const stored = buffer.subarray(start, start + compressedSize)
    if (method !== 0 && method !== 8) {
      throw new Error(`the compression method ${method} of ${name}`)
    }
    const data = method === 0 ? stored : inflateRawSync(stored)
    entries.push(...textsOf(name, data))
    offset += 46 + nameLength + extraLength + commentLength
  }
  return entries
}

function jsonStrings(value: unknown, strings: string[]): void {
  if (typeof value === 'string') {
    strings.push(value)
  } else if (Array.isArray(value)) {
    value.forEach((item) => jsonStrings(item, strings))
  } else if (value !== null && typeof value === 'object') {
    Object.values(value).forEach((item) => jsonStrings(item, strings))
  }
}

function decodedJson(text: string): string {
  const strings: string[] = []
  const parse = (candidate: string): void => {
    try {
      jsonStrings(JSON.parse(candidate), strings)
    } catch {
      return
    }
  }
  const trimmed = text.trimStart()
  if (trimmed.startsWith('{') || trimmed.startsWith('[')) {
    parse(text)
  }
  if (strings.length === 0) {
    for (const line of text.split('\n')) {
      if (line.startsWith('{')) {
        parse(line)
      }
    }
  }
  return strings.join('\n')
}

function withoutEntities(text: string): string {
  return markupEntities.reduce((current, [pattern, value]) => current.replace(pattern, value), text)
}

function isZip(data: Buffer): boolean {
  return data.length >= 4 && data.readUInt32LE(0) === 0x04034b50
}

function textsOf(name: string, data: Buffer): ScannedEntry[] {
  if (isZip(data)) {
    return zipEntries(data).map((entry) => ({ name: `${name} > ${entry.name}`, text: entry.text }))
  }
  const raw = data.toString('utf8')
  const embedded = embeddedReport.exec(raw)
  const nested =
    embedded?.[1] === undefined
      ? []
      : zipEntries(Buffer.from(embedded[1], 'base64')).map((entry) => ({
          name: `${name} > report data > ${entry.name}`,
          text: entry.text,
        }))
  return [{ name, text: `${raw}\n${withoutEntities(raw)}\n${decodedJson(raw)}` }, ...nested]
}

function filesUnder(folder: string): string[] {
  if (!existsSync(folder)) {
    return []
  }
  return readdirSync(folder, { recursive: true, encoding: 'utf8' })
    .filter((name) => !skippedFolders.has(name.split(/[\\/]/)[0] ?? ''))
    .map((name) => join(folder, name))
    .filter((path) => statSync(path).isFile())
    .sort()
}

function contentKind(path: string, entries: readonly ScannedEntry[]): string {
  const extension = extname(path).toLowerCase()
  if (extension === '.zip') {
    return entries.some((entry) => entry.name.endsWith('.trace')) ? 'trace' : 'archive'
  }
  if (basename(path) === 'error-context.md') {
    return 'error context'
  }
  if (extension === '.md') {
    return 'markdown file'
  }
  if (extension === '.xml') {
    return 'JUnit report'
  }
  if (screenshotExtensions.has(extension)) {
    return 'screenshot'
  }
  if (extension === '.webm') {
    return 'video'
  }
  return 'file'
}

function fileKind(
  path: string,
  entries: readonly ScannedEntry[],
  reportFolder: string | null,
): string {
  const kind = contentKind(path, entries)
  const inside = reportFolder === null ? null : relative(reportFolder, path)
  if (inside === null || inside.startsWith('..') || isAbsolute(inside)) {
    return kind
  }
  if (inside === 'index.html') {
    return 'HTML report page with its embedded report data'
  }
  return inside.split(sep)[0] === 'data'
    ? `${kind} in the report data`
    : `${kind} of the HTML report`
}

function errorCode(error: unknown): string {
  if (error instanceof Error && 'code' in error && typeof error.code === 'string') {
    return error.code
  }
  return error instanceof Error ? error.name : 'unknown error'
}

function removeSecretFiles(hits: readonly SecretFile[]): Removal {
  const byPath = new Map(hits.map((hit) => [hit.path, hit]))
  const problems: string[] = []
  let deleted = 0
  for (const hit of byPath.values()) {
    try {
      rmSync(hit.path)
    } catch (error) {
      problems.push(
        `${hit.file} (${hit.kind}) holds ${hit.found} and could not be deleted (${errorCode(error)}).`,
      )
      continue
    }
    deleted += 1
    problems.push(`Deleted ${hit.file} (${hit.kind}), which held ${hit.found}.`)
  }
  return { deleted, problems }
}

function scanFolderOutput(
  folder: string,
  root: string,
  secrets: readonly Sentinel[],
  control: string,
  reportFolder: string | null,
): FolderReport {
  const files = filesUnder(folder)
  const hits: SecretFile[] = []
  const unreadable: string[] = []
  let entries = 0
  let controlHits = 0
  for (const path of files) {
    const file = relative(root, path)
    let scanned: ScannedEntry[]
    try {
      scanned = textsOf(file, readFileSync(path))
    } catch (error) {
      unreadable.push(`${file} (${error instanceof Error ? error.message : 'unreadable'})`)
      continue
    }
    entries += scanned.length
    const labels = new Map<string, number>()
    for (const entry of scanned) {
      if (entry.name.includes(' > report data > ') && entry.text.includes(control)) {
        controlHits += 1
      }
      for (const secret of secrets) {
        if (entry.text.includes(secret.value)) {
          labels.set(secret.label, (labels.get(secret.label) ?? 0) + 1)
        }
      }
    }
    if (labels.size > 0) {
      const found = [...labels].map(
        ([label, count]) => `${withArticle(label)} in ${count} entr${count === 1 ? 'y' : 'ies'}`,
      )
      hits.push({
        path,
        file,
        kind: fileKind(path, scanned, reportFolder),
        found: found.join(', '),
      })
    }
  }
  return {
    folder: relative(root, folder),
    files: files.length,
    entries,
    hits,
    unreadable,
    control: controlHits,
  }
}

function htmlReportFolder(config: FullConfig, configDir: string): string | null {
  const fromEnvironment =
    process.env.PLAYWRIGHT_HTML_OUTPUT_DIR ?? process.env.PLAYWRIGHT_HTML_REPORT
  if (fromEnvironment !== undefined && fromEnvironment.length > 0) {
    return resolve(fromEnvironment)
  }
  const html = config.reporter.find(([name]) => name === 'html')
  if (html === undefined) {
    return null
  }
  const options = html[1] as { outputFolder?: unknown } | undefined
  const folder =
    typeof options?.outputFolder === 'string' ? options.outputFolder : 'playwright-report'
  return resolve(configDir, folder)
}

export default class ReportScan implements Reporter {
  private config: FullConfig | null = null

  onBegin(config: FullConfig): void {
    this.config = config
  }

  printsToStdio(): boolean {
    return false
  }

  async onEnd(result: FullResult): Promise<{ status?: FullResult['status'] } | undefined> {
    const runId = process.env.E2E_RUN_ID
    const config = this.config
    if (runId === undefined || runId.length === 0 || config === null) {
      process.stdout.write('e2e report scan: no run id, nothing to scan\n')
      return undefined
    }
    const configDir = dirname(config.configFile ?? process.cwd())
    const handedOver = takeReportSecrets(runId)
    const secrets: Sentinel[] = [
      ...(handedOver ?? []),
      { label: 'demo password', value: demoPassword() },
    ]
    const reportFolder = htmlReportFolder(config, configDir)
    const outputFolders = [...new Set(config.projects.map((project) => project.outputDir))]
    const folders = [...(reportFolder === null ? [] : [reportFolder]), ...outputFolders]
    const reports = folders.map((folder) =>
      scanFolderOutput(folder, configDir, secrets, runId, reportFolder),
    )
    const removal = removeSecretFiles(reports.flatMap((report) => report.hits))
    const problems = [
      ...removal.problems,
      ...reports.flatMap((report) =>
        report.unreadable.map((file) => `${file} could not be scanned`),
      ),
    ]
    if (handedOver === null) {
      problems.push(
        'The global teardown handed over no secret sentinels, so the report scan checked the demo password only.',
      )
    }
    const report = reports[0]
    if (reportFolder !== null && (report === undefined || report.control === 0)) {
      problems.push(
        'The positive control (the run id in the HTML report data) was not found, so the report scan proves nothing.',
      )
    }
    process.stdout.write(
      [
        `e2e report scan for run ${runId} (${result.status}): ${secrets.length} secret sentinel(s)`,
        ...reports.map(
          (item) =>
            `  ${item.folder}: ${item.files} file(s), ${item.entries} entr${item.entries === 1 ? 'y' : 'ies'} read, ${item.hits.length} file(s) holding a secret, ${item.unreadable.length} unreadable${item.folder === (report?.folder ?? '') && reportFolder !== null ? `; positive control: the run id in ${item.control} report data entr${item.control === 1 ? 'y' : 'ies'}` : ''}`,
        ),
        `  files deleted because they held a secret: ${removal.deleted}`,
        ...problems.map((problem) => `  ${problem}`),
        '',
      ].join('\n'),
    )
    return problems.length > 0 ? { status: 'failed' } : undefined
  }
}
