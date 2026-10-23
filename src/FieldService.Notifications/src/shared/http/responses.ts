import type { IncomingMessage, ServerResponse } from 'node:http'
import type { HttpError } from './http-errors.ts'

const correlationIdPattern = /^[A-Za-z0-9._-]{1,64}$/

const commonHeaders: Readonly<Record<string, string>> = {
  'x-content-type-options': 'nosniff',
  'cache-control': 'no-store',
}

function send(
  response: ServerResponse,
  status: number,
  contentType: string,
  body: string,
  extraHeaders: Readonly<Record<string, string>> = {},
): void {
  response.writeHead(status, {
    ...commonHeaders,
    ...extraHeaders,
    'content-type': contentType,
    'content-length': Buffer.byteLength(body),
  })
  response.end(body)
}

export function correlationIdOf(request: IncomingMessage): string | undefined {
  const value = request.headers['x-correlation-id']
  return typeof value === 'string' && correlationIdPattern.test(value) ? value : undefined
}

export function sendText(response: ServerResponse, status: number, text: string): void {
  send(response, status, 'text/plain', text)
}

export function correlationHeaders(
  correlationId: string | undefined,
): Readonly<Record<string, string>> {
  return correlationId === undefined ? {} : { 'x-correlation-id': correlationId }
}

export function sendJson(
  response: ServerResponse,
  status: number,
  body: unknown,
  extraHeaders: Readonly<Record<string, string>> = {},
): void {
  send(response, status, 'application/json', JSON.stringify(body), extraHeaders)
}

export function sendProblem(
  response: ServerResponse,
  error: HttpError,
  correlationId: string | undefined,
): void {
  const problem = {
    type: 'about:blank',
    title: error.title,
    status: error.status,
    code: error.code,
    ...(correlationId === undefined ? {} : { correlationId }),
  }
  send(response, error.status, 'application/problem+json', JSON.stringify(problem), {
    ...error.headers,
    ...correlationHeaders(correlationId),
  })
}
