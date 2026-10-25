import { join } from 'node:path'

import { consoleLines } from './containers.ts'
import type { RawRequest, RawResponse } from './probes.ts'
import { logsRoot } from './run.ts'
import { filesChangedSince, linesOfFiles } from './sentinels.ts'

export interface ExposedPath {
  readonly method: 'GET' | 'POST'
  readonly raw: string
  readonly logged: string
}

export const exposedPaths: readonly ExposedPath[] = [
  { method: 'GET', raw: '/health/live', logged: '/health/live' },
  { method: 'GET', raw: '/health/ready', logged: '/health/ready' },
  { method: 'GET', raw: '/health', logged: '/health' },
  { method: 'POST', raw: '/internal/job-events', logged: '/internal/job-events' },
  { method: 'GET', raw: '/internal', logged: '/internal' },
  { method: 'GET', raw: '/api/..%2Fhealth/ready', logged: '/health/ready' },
  { method: 'POST', raw: '/socket.io/%2e%2e/internal/job-events', logged: '/internal/job-events' },
  { method: 'POST', raw: '/api/%2e%2e/internal/job-events', logged: '/internal/job-events' },
  { method: 'POST', raw: '/%69nternal/job-events', logged: '/internal/job-events' },
  { method: 'POST', raw: '//internal/job-events', logged: '/internal/job-events' },
  { method: 'GET', raw: '/health%2Flive', logged: '/health/live' },
  { method: 'GET', raw: '/api/%2E%2E%2Fhealth%2Fready', logged: '/health/ready' },
  { method: 'POST', raw: '/socket.io/..%2Finternal/job-events', logged: '/internal/job-events' },
  { method: 'POST', raw: '/internal%2Fjob-events', logged: '/internal/job-events' },
  { method: 'GET', raw: '/%2Fhealth/live', logged: '/health/live' },
  { method: 'GET', raw: '/./health/live', logged: '/health/live' },
  { method: 'POST', raw: '/internal/./job-events', logged: '/internal/job-events' },
  { method: 'GET', raw: '/api/jobs/..%2F..%2Fhealth%2Flive', logged: '/health/live' },
  { method: 'POST', raw: '/internal/job-events?x=1', logged: '/internal/job-events' },
]

export interface ForwardedPath {
  readonly method: 'GET' | 'POST'
  readonly raw: string
  readonly status: number
  readonly mediaType: string
}

export const doubleEncodedPaths: readonly ForwardedPath[] = [
  {
    method: 'GET',
    raw: '/api/%252e%252e/health/ready',
    status: 404,
    mediaType: 'application/problem+json',
  },
  {
    method: 'POST',
    raw: '/socket.io/%252e%252e/internal/job-events',
    status: 400,
    mediaType: 'application/json',
  },
]

export const rootEscapePath = '/assets/..%2F..%2Finternal/job-events'

export function probeRequest(method: 'GET' | 'POST', raw: string, id: string): RawRequest {
  return method === 'POST'
    ? {
        method,
        path: raw,
        headers: { 'X-Correlation-ID': id, 'Content-Type': 'application/json' },
        body: '{}',
      }
    : { method, path: raw, headers: { 'X-Correlation-ID': id } }
}

export interface NginxEntry {
  readonly method: string
  readonly path: string
  readonly status: number
  readonly correlationId: string
}

function asNginxEntry(line: string): NginxEntry[] {
  try {
    const value: unknown = JSON.parse(line)
    if (typeof value !== 'object' || value === null || !('path' in value) || !('status' in value)) {
      return []
    }
    return [value as NginxEntry]
  } catch {
    return []
  }
}

export function nginxEntries(since: string): NginxEntry[] {
  return consoleLines('web', since).flatMap(asNginxEntry)
}

export function nginxEntryOf(entries: readonly NginxEntry[], id: string): string {
  const matches = entries.filter((entry) => entry.correlationId === id)
  return matches.map((entry) => `${entry.method} ${entry.path} ${entry.status}`).join(' | ')
}

export function isNginxErrorPage(response: RawResponse): boolean {
  return (
    response.headers.server === 'nginx' &&
    String(response.headers['content-type']).startsWith('text/html') &&
    response.body.includes('<center>nginx</center>') &&
    response.headers['x-correlation-id'] === undefined
  )
}

export type ServiceSource =
  'console api' | 'console notifications' | 'logs/api' | 'logs/notifications'

export function serviceLines(since: string): Readonly<Record<ServiceSource, readonly string[]>> {
  const changedSince = Date.parse(since)
  return {
    'console api': consoleLines('api', since),
    'console notifications': consoleLines('notifications', since),
    'logs/api': linesOfFiles(filesChangedSince(join(logsRoot(), 'api'), changedSince)),
    'logs/notifications': linesOfFiles(
      filesChangedSince(join(logsRoot(), 'notifications'), changedSince),
    ),
  }
}

export function sightings(
  lines: Readonly<Record<ServiceSource, readonly string[]>>,
  values: readonly string[],
): string[] {
  return Object.entries(lines).flatMap(([source, sourceLines]) =>
    values
      .filter((value) => sourceLines.some((line) => line.includes(value)))
      .map((value) => `${value} in ${source}`),
  )
}

export function sourcesHolding(
  lines: Readonly<Record<ServiceSource, readonly string[]>>,
  value: string,
): ServiceSource[] {
  return (Object.keys(lines) as ServiceSource[]).filter((source) =>
    lines[source].some((line) => line.includes(value)),
  )
}
