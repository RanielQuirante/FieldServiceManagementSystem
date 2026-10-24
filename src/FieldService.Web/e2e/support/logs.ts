import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import { setTimeout as delay } from 'node:timers/promises'

import { logsRoot } from './run.ts'

export type LogRecord = Readonly<Record<string, unknown>>

function apiLogLines(): string[] {
  const folder = join(logsRoot(), 'api')
  let names: string[]
  try {
    names = readdirSync(folder)
  } catch {
    return []
  }
  return names
    .filter((name) => name.endsWith('.log'))
    .sort()
    .slice(-2)
    .flatMap((name) => readFileSync(join(folder, name), 'utf8').split('\n'))
}

function parse(line: string): LogRecord | null {
  try {
    const value: unknown = JSON.parse(line)
    return typeof value === 'object' && value !== null ? (value as LogRecord) : null
  } catch {
    return null
  }
}

export function eventName(record: LogRecord): string | null {
  const eventId = record.EventId
  if (typeof eventId !== 'object' || eventId === null) {
    return null
  }
  const name = (eventId as { Name?: unknown }).Name
  return typeof name === 'string' ? name : null
}

export async function waitForApiLogRecords(
  marker: string,
  matches: (record: LogRecord) => boolean,
  timeoutMilliseconds: number = 10_000,
): Promise<LogRecord[]> {
  const deadline = Date.now() + timeoutMilliseconds
  for (;;) {
    const records = apiLogLines()
      .filter((line) => line.includes(marker))
      .map(parse)
      .filter((record): record is LogRecord => record !== null && matches(record))
    if (records.length > 0 || Date.now() > deadline) {
      return records
    }
    await delay(250)
  }
}
