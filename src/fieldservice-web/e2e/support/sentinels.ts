import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join, relative } from 'node:path'

import { readRunFiles, resultsFile, writeJsonFile } from './run.ts'

export interface Sentinel {
  readonly label: string
  readonly value: string
}

export interface SentinelHit {
  readonly file: string
  readonly line: number
  readonly label: string
}

export interface FolderScan {
  readonly folder: string
  readonly files: number
  readonly hits: readonly SentinelHit[]
}

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

export function scanFolder(
  folder: string,
  sentinels: readonly Sentinel[],
  root: string,
): FolderScan {
  const files = filesUnder(folder)
  const hits: SentinelHit[] = []
  for (const path of files) {
    const lines = readFileSync(path, 'utf8').split('\n')
    lines.forEach((line, index) => {
      const decoded = decodedStrings(line)
      for (const sentinel of sentinels) {
        if (line.includes(sentinel.value) || decoded.includes(sentinel.value)) {
          hits.push({ file: relative(root, path), line: index + 1, label: sentinel.label })
        }
      }
    })
  }
  return { folder: relative(root, folder), files: files.length, hits }
}
