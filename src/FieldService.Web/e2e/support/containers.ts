import { spawnSync } from 'node:child_process'
import { resolve } from 'node:path'

import { webRoot } from './run.ts'

export const composeFile = resolve(webRoot, '../../infrastructure/docker-compose.yml')

export const consoleServices = ['web', 'api', 'notifications', 'db'] as const

export type ConsoleService = (typeof consoleServices)[number]

export interface HostPort {
  readonly host: string
  readonly port: number
}

const outputLimitBytes = 512 * 1024 * 1024

const sinceMarginMilliseconds = 5_000

function firstLine(text: string): string {
  return (text.split('\n').find((line) => line.trim().length > 0) ?? '').slice(0, 200)
}

function compose(args: readonly string[]): string {
  const result = spawnSync('docker', ['compose', '-f', composeFile, '--profile', 'full', ...args], {
    encoding: 'utf8',
    maxBuffer: outputLimitBytes,
  })
  if (result.error !== undefined) {
    throw new Error(`docker compose ${args[0] ?? ''} could not run: ${result.error.message}`)
  }
  if (result.status !== 0) {
    throw new Error(
      `docker compose ${args[0] ?? ''} exited with ${String(result.status)}: ${firstLine(result.stderr)}`,
    )
  }
  return `${result.stdout}\n${result.stderr}`
}

export function consoleSince(startedAt: string): string {
  return new Date(Date.parse(startedAt) - sinceMarginMilliseconds).toISOString()
}

export function consoleLines(service: ConsoleService, since: string): string[] {
  return compose(['logs', '--no-log-prefix', '--no-color', '--since', since, service])
    .split('\n')
    .filter((line) => line.trim().length > 0)
}

export function publishedPort(service: ConsoleService, containerPort: number): HostPort {
  const answer = firstLine(compose(['port', service, String(containerPort)]))
  const match = /^(.+):(\d+)$/.exec(answer.trim())
  if (match?.[1] === undefined || match[2] === undefined) {
    throw new Error(`docker compose port ${service} ${containerPort} printed no address.`)
  }
  return { host: match[1], port: Number(match[2]) }
}
