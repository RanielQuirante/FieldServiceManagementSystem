import { existsSync, readdirSync, readFileSync, rmdirSync, rmSync, statSync } from 'node:fs'
import { join, relative } from 'node:path'

import { readRunFiles, resultsFile, resultsRoot, writeJsonFile } from './run.ts'

export interface Sentinel {
  readonly label: string
  readonly value: string
}

export interface SentinelHit {
  readonly file: string
  readonly line: number
  readonly label: string
}

export interface ControlCount {
  readonly label: string
  readonly lines: number
}

export interface SourceScan {
  readonly source: string
  readonly files: number
  readonly lines: number
  readonly hits: readonly SentinelHit[]
  readonly controls: readonly ControlCount[]
}

export function withArticle(label: string): string {
  return `${/^[aeiou]/i.test(label) ? 'an' : 'a'} ${label}`
}

export const secretLabels: ReadonlySet<string> = new Set([
  'access token',
  'refresh token',
  'refresh cookie',
  'socket ticket',
  'socket ticket signature',
  'demo password',
  'header sentinel',
])

export const minimumSentinelLength = 6

export const demoAccountSentinels: readonly Sentinel[] = [
  { label: 'administrator name', value: 'Dana Whitfield' },
  { label: 'administrator email', value: 'dana.whitfield@fieldservice.example' },
  { label: 'technician name', value: 'Marco Reyes' },
  { label: 'technician email', value: 'marco.reyes@fieldservice.example' },
  { label: 'technician phone', value: '(555) 014-2231' },
  { label: 'technician name', value: 'Aisha Karim' },
  { label: 'technician email', value: 'aisha.karim@fieldservice.example' },
  { label: 'technician phone', value: '(555) 014-7810' },
  { label: 'technician name', value: 'Tom Becker' },
  { label: 'technician email', value: 'tom.becker@fieldservice.example' },
  { label: 'technician phone', value: '(555) 014-3392' },
  { label: 'technician name', value: 'Lena Park' },
  { label: 'technician email', value: 'lena.park@fieldservice.example' },
  { label: 'technician phone', value: '(555) 014-5521' },
  { label: 'technician name', value: 'Priya Natarajan' },
  { label: 'technician email', value: 'priya.natarajan@fieldservice.example' },
  { label: 'technician phone', value: '(555) 014-6604' },
  { label: 'technician name', value: 'Samuel Osei' },
  { label: 'technician email', value: 'samuel.osei@fieldservice.example' },
  { label: 'technician phone', value: '(555) 014-9148' },
]

export class SentinelCollector {
  private readonly values = new Map<string, string>()

  constructor(private readonly file: string) {}

  add(label: string, value: string | null | undefined): void {
    if (typeof value !== 'string') {
      return
    }
    const trimmed = value.trim()
    if (trimmed.length < minimumSentinelLength || this.values.has(trimmed)) {
      return
    }
    this.values.set(trimmed, label)
    this.flush()
  }

  get size(): number {
    return this.values.size
  }

  flush(): void {
    const entries: Sentinel[] = [...this.values].map(([value, label]) => ({ label, value }))
    writeJsonFile(this.file, entries)
  }
}

export function sentinelFile(runId: string, workerIndex: number): string {
  return resultsFile('sentinels', `${runId}-worker-${workerIndex}.json`)
}

export function readRunSentinels(runId: string): Sentinel[] {
  const unique = new Map<string, string>()
  for (const entries of readRunFiles<Sentinel[]>('sentinels', runId)) {
    for (const entry of entries) {
      unique.set(entry.value, entry.label)
    }
  }
  return [...unique].map(([value, label]) => ({ label, value }))
}

const sentinelFolder = join(resultsRoot, 'sentinels')

export function removeRunSentinelFiles(runId: string): number {
  if (!existsSync(sentinelFolder)) {
    return 0
  }
  const files = readdirSync(sentinelFolder).filter(
    (name) => name.startsWith(`${runId}-`) && name.endsWith('.json'),
  )
  for (const name of files) {
    rmSync(join(sentinelFolder, name))
  }
  if (readdirSync(sentinelFolder).length === 0) {
    rmdirSync(sentinelFolder)
  }
  return files.length
}

interface SecretHandover {
  readonly runId: string
  readonly secrets: readonly Sentinel[]
}

const secretHandoverKey = Symbol.for('fieldservice.e2e.report-secrets')

function handoverSlot(): Record<symbol, SecretHandover | undefined> {
  return globalThis as unknown as Record<symbol, SecretHandover | undefined>
}

export function handOverReportSecrets(runId: string, sentinels: readonly Sentinel[]): void {
  handoverSlot()[secretHandoverKey] = {
    runId,
    secrets: sentinels.filter((sentinel) => secretLabels.has(sentinel.label)),
  }
}

export function takeReportSecrets(runId: string): readonly Sentinel[] | null {
  const handover = handoverSlot()[secretHandoverKey]
  handoverSlot()[secretHandoverKey] = undefined
  return handover?.runId === runId ? handover.secrets : null
}

function filesUnder(folder: string): string[] {
  let names: string[]
  try {
    names = readdirSync(folder, { recursive: true, encoding: 'utf8' })
  } catch {
    return []
  }
  return names
    .map((name) => join(folder, name))
    .filter((path) => statSync(path).isFile())
    .sort()
}

function decodedStrings(line: string): string {
  let parsed: unknown
  try {
    parsed = JSON.parse(line)
  } catch {
    return ''
  }
  const strings: string[] = []
  const visit = (value: unknown): void => {
    if (typeof value === 'string') {
      strings.push(value)
    } else if (Array.isArray(value)) {
      value.forEach(visit)
    } else if (value !== null && typeof value === 'object') {
      Object.values(value).forEach(visit)
    }
  }
  visit(parsed)
  return strings.join('\n')
}

interface LineTally {
  lines: number
  readonly hits: SentinelHit[]
  readonly controls: Map<string, number>
}

function newTally(controls: readonly Sentinel[]): LineTally {
  return { lines: 0, hits: [], controls: new Map(controls.map((control) => [control.label, 0])) }
}

function tallyLines(
  tally: LineTally,
  file: string,
  lines: readonly string[],
  sentinels: readonly Sentinel[],
  controls: readonly Sentinel[],
): void {
  lines.forEach((line, index) => {
    if (line.length === 0) {
      return
    }
    tally.lines += 1
    const decoded = decodedStrings(line)
    const holds = (value: string): boolean => line.includes(value) || decoded.includes(value)
    for (const sentinel of sentinels) {
      if (holds(sentinel.value)) {
        tally.hits.push({ file, line: index + 1, label: sentinel.label })
      }
    }
    for (const control of controls) {
      if (holds(control.value)) {
        tally.controls.set(control.label, (tally.controls.get(control.label) ?? 0) + 1)
      }
    }
  })
}

function scanOf(source: string, files: number, tally: LineTally): SourceScan {
  return {
    source,
    files,
    lines: tally.lines,
    hits: tally.hits,
    controls: [...tally.controls].map(([label, lines]) => ({ label, lines })),
  }
}

export function scanFolder(
  folder: string,
  sentinels: readonly Sentinel[],
  root: string,
  controls: readonly Sentinel[] = [],
): SourceScan {
  const files = filesUnder(folder)
  const tally = newTally(controls)
  for (const path of files) {
    tallyLines(
      tally,
      relative(root, path),
      readFileSync(path, 'utf8').split('\n'),
      sentinels,
      controls,
    )
  }
  return scanOf(relative(root, folder), files.length, tally)
}

export function scanLines(
  source: string,
  lines: readonly string[],
  sentinels: readonly Sentinel[],
  controls: readonly Sentinel[] = [],
): SourceScan {
  const tally = newTally(controls)
  tallyLines(tally, source, lines, sentinels, controls)
  return scanOf(source, 0, tally)
}

export function filesChangedSince(folder: string, since: number): string[] {
  return filesUnder(folder).filter((path) => statSync(path).mtimeMs >= since)
}

export function linesOfFiles(paths: readonly string[]): string[] {
  return paths.flatMap((path) => readFileSync(path, 'utf8').split('\n'))
}

export function recordRunSentinels(
  runId: string,
  name: string,
  entries: readonly Sentinel[],
): void {
  writeJsonFile(resultsFile('sentinels', `${runId}-${name}.json`), entries)
}
