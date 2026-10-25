import { randomBytes } from 'node:crypto'
import { request, type IncomingHttpHeaders, type OutgoingHttpHeaders } from 'node:http'
import { connect } from 'node:net'

import type { HostPort } from './containers.ts'

export interface RawResponse {
  readonly status: number
  readonly headers: IncomingHttpHeaders
  readonly body: string
}

export interface RawRequest {
  readonly method: 'GET' | 'POST'
  readonly path: string
  readonly headers?: OutgoingHttpHeaders
  readonly body?: string
}

export const directApi: HostPort = { host: '127.0.0.1', port: 5080 }

export const directNotifications: HostPort = { host: '127.0.0.1', port: 5090 }

const probeTimeoutMilliseconds = 10_000

export function hostPortOf(baseUrl: string): HostPort {
  const url = new URL(baseUrl)
  return { host: url.hostname, port: Number(url.port || (url.protocol === 'https:' ? 443 : 80)) }
}

export function probeMarker(prefix: string): string {
  return `${prefix}${randomBytes(5).toString('hex')}`
}

export function sendRaw(target: HostPort, raw: RawRequest): Promise<RawResponse> {
  return new Promise((resolve, reject) => {
    const outgoing = request(
      {
        host: target.host,
        port: target.port,
        method: raw.method,
        path: raw.path,
        headers: raw.headers ?? {},
        timeout: probeTimeoutMilliseconds,
      },
      (response) => {
        let body = ''
        response.setEncoding('utf8')
        response.on('data', (chunk: string) => {
          body += chunk
        })
        response.on('end', () => {
          resolve({ status: response.statusCode ?? 0, headers: response.headers, body })
        })
      },
    )
    outgoing.on('timeout', () => outgoing.destroy(new Error(`${raw.method} probe timed out`)))
    outgoing.on('error', reject)
    outgoing.end(raw.body)
  })
}

export function upgradeStatus(target: HostPort, path: string, origin: string): Promise<number> {
  return new Promise((resolve, reject) => {
    const outgoing = request({
      host: target.host,
      port: target.port,
      path,
      timeout: probeTimeoutMilliseconds,
      headers: {
        Connection: 'Upgrade',
        Upgrade: 'websocket',
        'Sec-WebSocket-Version': '13',
        'Sec-WebSocket-Key': randomBytes(16).toString('base64'),
        Origin: origin,
      },
    })
    outgoing.on('upgrade', (response, socket) => {
      socket.destroy()
      resolve(response.statusCode ?? 0)
    })
    outgoing.on('response', (response) => {
      response.resume()
      resolve(response.statusCode ?? 0)
    })
    outgoing.on('timeout', () => outgoing.destroy(new Error('upgrade probe timed out')))
    outgoing.on('error', reject)
    outgoing.end()
  })
}

function startupPacket(parameters: readonly string[]): Buffer {
  const body = Buffer.concat([
    Buffer.from([0, 3, 0, 0]),
    ...parameters.map((parameter) => Buffer.from(`${parameter}\0`, 'utf8')),
    Buffer.from([0]),
  ])
  const length = Buffer.alloc(4)
  length.writeUInt32BE(body.length + 4)
  return Buffer.concat([length, body])
}

export function refusedStartup(target: HostPort, marker: string): Promise<string> {
  return new Promise((resolve, reject) => {
    const socket = connect(target.port, target.host)
    let reply = Buffer.alloc(0)
    socket.setTimeout(probeTimeoutMilliseconds, () =>
      socket.destroy(new Error('startup timed out')),
    )
    socket.on('connect', () => {
      socket.write(startupPacket(['user', 'e2e', 'database', 'postgres', 'replication', marker]))
    })
    socket.on('data', (chunk: Buffer) => {
      reply = Buffer.concat([reply, chunk])
    })
    socket.on('close', () => {
      resolve(reply.toString('utf8'))
    })
    socket.on('error', reject)
  })
}
