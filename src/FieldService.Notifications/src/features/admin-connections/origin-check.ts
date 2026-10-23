import type { IncomingMessage } from 'node:http'
import type { Logger } from '../../logging.ts'
import { logSocketRejected } from './connection-logging.ts'

export type AllowRequest = (
  request: IncomingMessage,
  callback: (error: string | null | undefined, success: boolean) => void,
) => void

export function createOriginCheck(allowedOrigins: readonly string[], logger: Logger): AllowRequest {
  const allowed = new Set(allowedOrigins)

  return (request, callback) => {
    const origin = request.headers.origin
    if (origin === undefined || allowed.has(origin)) {
      callback(null, true)
      return
    }
    logSocketRejected(logger, { reason: 'origin-not-allowed' })
    callback(null, false)
  }
}
