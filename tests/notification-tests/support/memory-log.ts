export interface LogEntry {
  readonly level: number
  readonly time: string
  readonly msg: string
  readonly [key: string]: unknown
}

export const levelNames: Readonly<Record<number, string>> = {
  10: 'trace',
  20: 'debug',
  30: 'info',
  40: 'warn',
  50: 'error',
  60: 'fatal',
}

export interface MemoryLog {
  readonly destination: { write(line: string): void }
  readonly entries: readonly LogEntry[]
  readonly lines: readonly string[]
  find(message: string): LogEntry | undefined
  levelOf(entry: LogEntry): string | undefined
}

export function createMemoryLog(): MemoryLog {
  const entries: LogEntry[] = []
  const lines: string[] = []

  return {
    destination: {
      write(chunk: string) {
        for (const line of chunk.split('\n')) {
          if (line.trim() === '') {
            continue
          }
          lines.push(line)
          const entry: LogEntry = JSON.parse(line)
          entries.push(entry)
        }
      },
    },
    entries,
    lines,
    find: (message) => entries.find((entry) => entry.msg === message),
    levelOf: (entry) => levelNames[entry.level],
  }
}
