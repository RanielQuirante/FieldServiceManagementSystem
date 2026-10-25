import { join } from 'node:path'
import { setTimeout as delay } from 'node:timers/promises'

import {
  consoleLines,
  consoleServices,
  consoleSince,
  publishedPort,
  type ConsoleService,
  type HostPort,
} from './containers.ts'
import {
  directApi,
  directNotifications,
  hostPortOf,
  probeMarker,
  refusedStartup,
  sendRaw,
  type RawRequest,
} from './probes.ts'
import { logsRoot } from './run.ts'
import {
  filesChangedSince,
  linesOfFiles,
  scanFolder,
  scanLines,
  withArticle,
  type Sentinel,
  type SourceScan,
} from './sentinels.ts'

export const logFolders = ['api', 'notifications'] as const

type LogFolder = (typeof logFolders)[number]

export interface LogSeeds {
  readonly sentinels: readonly Sentinel[]
  readonly folderControls: Readonly<Record<LogFolder, readonly Sentinel[]>>
  readonly consoleControls: Readonly<Record<ConsoleService, readonly Sentinel[]>>
  readonly requests: number
  readonly problems: readonly string[]
}

export interface LogScanResult {
  readonly scans: readonly SourceScan[]
  readonly problems: readonly string[]
  readonly waitedMilliseconds: number
  readonly nginxCorrelationIds: number | null
}

export interface LogScanOptions {
  readonly sentinels: readonly Sentinel[]
  readonly seeds: LogSeeds
  readonly compose: boolean
  readonly startedAt: string
}

const controlWaitMilliseconds = 20_000

const controlPollMilliseconds = 500

function controlOf(runId: string, name: string): Sentinel {
  return { label: `${name} control`, value: `e2e-${runId}-control-${name}` }
}

interface SeedTarget {
  readonly name: string
  readonly target: HostPort
  readonly request: RawRequest
  readonly status?: number
}

function headerSentinelHeaders(marker: string): Readonly<Record<string, string>> {
  return {
    'X-Correlation-ID': `${marker}@x`,
    'User-Agent': `${marker}-ua`,
    Referer: `http://example.invalid/${marker}-ref`,
    Cookie: `fs_probe=${marker}-cookie`,
    Authorization: `Bearer ${marker}-auth`,
    'X-Forwarded-For': `${marker}-xff`,
    'X-Real-IP': `${marker}-xri`,
  }
}

function seedTargets(
  baseUrl: string,
  runId: string,
  header: string,
  query: string,
  compose: boolean,
): SeedTarget[] {
  const base = hostPortOf(baseUrl)
  const headers = headerSentinelHeaders(header)
  const withId = (control: Sentinel): Readonly<Record<string, string>> => ({
    'X-Correlation-ID': control.value,
  })
  const controls: SeedTarget[] = [
    {
      name: 'the API control through the stack',
      target: base,
      request: {
        method: 'GET',
        path: '/api/technicians',
        headers: withId(controlOf(runId, 'api')),
      },
      status: 401,
    },
    {
      name: 'the notification service control',
      target: directNotifications,
      request: {
        method: 'POST',
        path: '/internal/job-events',
        headers: { ...withId(controlOf(runId, 'notifications')), 'Content-Type': 'text/plain' },
        body: 'e2e',
      },
      status: 415,
    },
    ...(compose
      ? [
          {
            name: 'the nginx control',
            target: base,
            request: {
              method: 'GET',
              path: `/internal/e2e-${runId}-control`,
              headers: withId(controlOf(runId, 'nginx')),
            },
            status: 404,
          } satisfies SeedTarget,
        ]
      : []),
  ]
  const sentinelRequests: SeedTarget[] = [
    `/internal/e2e-${runId}-sentinel`,
    `/jobs/e2e-${runId}-sentinel`,
    '/api/technicians',
    '/socket.io/?EIO=4&transport=polling',
  ].map((path) => ({
    name: `the header sentinel on ${path.split('?')[0] ?? path}`,
    target: base,
    request: { method: 'GET', path, headers },
  }))
  const direct: SeedTarget[] = [
    {
      name: 'the header sentinel directly to the API',
      target: directApi,
      request: { method: 'GET', path: '/api/technicians', headers },
    },
    {
      name: 'the header sentinel directly to the notification service',
      target: directNotifications,
      request: { method: 'GET', path: '/health/live', headers },
    },
  ]
  const queries: SeedTarget[] = [
    { target: base, path: `/api/technicians?search=${query}` },
    { target: base, path: `/technicians?search=${query}` },
    { target: directApi, path: `/api/technicians?search=${query}` },
  ].map(({ target, path }) => ({
    name: `the query sentinel on ${path.split('?')[0] ?? path}`,
    target,
    request: { method: 'GET', path },
  }))
  return [...controls, ...sentinelRequests, ...direct, ...queries]
}

export async function sendLogSeeds(
  baseUrl: string,
  runId: string,
  compose: boolean,
): Promise<LogSeeds> {
  const header = probeMarker(`hs${runId}`)
  const query = probeMarker(`qs${runId}`)
  const problems: string[] = []
  let requests = 0
  for (const seed of seedTargets(baseUrl, runId, header, query, compose)) {
    requests += 1
    try {
      const response = await sendRaw(seed.target, seed.request)
      if (seed.status !== undefined && response.status !== seed.status) {
        problems.push(
          `The log seed "${seed.name}" answered ${response.status}, not ${seed.status}.`,
        )
      }
    } catch (error) {
      problems.push(
        `The log seed "${seed.name}" failed: ${error instanceof Error ? error.message : 'unknown error'}.`,
      )
    }
  }
  const db = controlOf(runId, 'db')
  if (compose) {
    requests += 1
    try {
      const reply = await refusedStartup(publishedPort('db', 5432), db.value)
      if (!reply.includes('"replication"')) {
        problems.push('The database control was not refused for its replication value.')
      }
    } catch (error) {
      problems.push(
        `The database control failed: ${error instanceof Error ? error.message : 'unknown error'}.`,
      )
    }
  }
  const api = controlOf(runId, 'api')
  const notifications = controlOf(runId, 'notifications')
  return {
    sentinels: [
      { label: 'header sentinel', value: header },
      { label: 'query sentinel', value: query },
    ],
    folderControls: { api: [api], notifications: [notifications] },
    consoleControls: {
      web: [controlOf(runId, 'nginx'), api],
      api: [api],
      notifications: [notifications],
      db: [db],
    },
    requests,
    problems,
  }
}

function missingControls(scan: SourceScan): string[] {
  return scan.controls.filter((control) => control.lines === 0).map((control) => control.label)
}

function quickFolderScan(
  folder: LogFolder,
  since: number,
  controls: readonly Sentinel[],
): SourceScan {
  return scanLines(
    `logs/${folder}`,
    linesOfFiles(filesChangedSince(join(logsRoot(), folder), since)),
    [],
    controls,
  )
}

function consoleScan(
  service: ConsoleService,
  since: string,
  sentinels: readonly Sentinel[],
  controls: readonly Sentinel[],
): SourceScan {
  return scanLines(`console ${service}`, consoleLines(service, since), sentinels, controls)
}

function quickScans(options: LogScanOptions, since: string): SourceScan[] {
  const sinceMilliseconds = Date.parse(since)
  const folders = logFolders.map((folder) =>
    quickFolderScan(folder, sinceMilliseconds, options.seeds.folderControls[folder]),
  )
  const consoles = options.compose
    ? consoleServices.map((service) =>
        consoleScan(service, since, [], options.seeds.consoleControls[service]),
      )
    : []
  return [...folders, ...consoles]
}

export async function scanRunLogs(options: LogScanOptions): Promise<LogScanResult> {
  const since = consoleSince(options.startedAt)
  const started = Date.now()
  while (
    quickScans(options, since).some((scan) => missingControls(scan).length > 0) &&
    Date.now() - started < controlWaitMilliseconds
  ) {
    await delay(controlPollMilliseconds)
  }
  const waitedMilliseconds = Date.now() - started
  const root = logsRoot()
  const folders = logFolders.map((folder) => ({
    ...scanFolder(
      join(root, folder),
      options.sentinels,
      root,
      options.seeds.folderControls[folder],
    ),
    source: `logs/${folder}`,
  }))
  const consoleText = new Map<ConsoleService, string[]>(
    options.compose
      ? consoleServices.map((service) => [service, consoleLines(service, since)])
      : [],
  )
  const consoles = [...consoleText].map(([service, lines]) =>
    scanLines(
      `console ${service}`,
      lines,
      options.sentinels,
      options.seeds.consoleControls[service],
    ),
  )
  const scans = [...folders, ...consoles]
  const nginxIds = nginxCorrelationIds(consoleText.get('web') ?? [])
  const problems: string[] = [...nginxIds.problems]
  for (const scan of scans) {
    if (scan.source.startsWith('logs/') && scan.files === 0) {
      problems.push(`No log file was found under ${scan.source}, so the secret scan cannot pass.`)
    }
    for (const label of missingControls(scan)) {
      problems.push(
        `The positive control "${label}" is missing from ${scan.source}, so its scan proves nothing.`,
      )
    }
    for (const hit of scan.hits) {
      problems.push(`${hit.file} line ${hit.line} contains ${withArticle(hit.label)}.`)
    }
  }
  return {
    scans,
    problems,
    waitedMilliseconds,
    nginxCorrelationIds: options.compose ? nginxIds.checked : null,
  }
}

const correlationIdRule = /^[A-Za-z0-9._-]{1,64}$/

interface CorrelationIdCheck {
  readonly checked: number
  readonly problems: readonly string[]
}

function nginxCorrelationIds(lines: readonly string[]): CorrelationIdCheck {
  let checked = 0
  const problems: string[] = []
  lines.forEach((line, index) => {
    let entry: unknown
    try {
      entry = JSON.parse(line)
    } catch {
      return
    }
    if (typeof entry !== 'object' || entry === null || !('correlationId' in entry)) {
      return
    }
    const id = (entry as { readonly correlationId: unknown }).correlationId
    if (typeof id !== 'string' || id.length === 0) {
      return
    }
    checked += 1
    if (!correlationIdRule.test(id)) {
      problems.push(
        `console web line ${index + 1} logs a correlation id outside ${correlationIdRule.source}.`,
      )
    }
  })
  return { checked, problems }
}

export function describeScan(scan: SourceScan): string {
  const files = scan.source.startsWith('logs/') ? `${scan.files} file(s), ` : ''
  const controls = scan.controls.map((control) => `${control.label} ${control.lines}`).join(', ')
  return `${scan.source}: ${files}${scan.lines} line(s), ${scan.hits.length} hit(s); positive controls: ${controls}`
}
