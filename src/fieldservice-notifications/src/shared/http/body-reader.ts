import type { IncomingMessage } from 'node:http'
import { PayloadTooLargeError } from './http-errors.ts'

function declaredLength(request: IncomingMessage): number | undefined {
  const header = request.headers['content-length']
  if (header === undefined) {
    return undefined
  }
  const length = Number(header)
  return Number.isFinite(length) ? length : undefined
}

export function readBody(request: IncomingMessage, maxBytes: number): Promise<Buffer> {
  const declared = declaredLength(request)
  if (declared !== undefined && declared > maxBytes) {
    return Promise.reject(new PayloadTooLargeError())
  }

  return new Promise<Buffer>((resolve, reject) => {
    const chunks: Buffer[] = []
    let total = 0

    const cleanup = (): void => {
      request.off('data', onData)
      request.off('end', onEnd)
      request.off('error', onError)
    }

    const onData = (chunk: Buffer): void => {
      total += chunk.length
      if (total > maxBytes) {
        cleanup()
        reject(new PayloadTooLargeError())
        return
      }
      chunks.push(chunk)
    }

    const onEnd = (): void => {
      cleanup()
      resolve(Buffer.concat(chunks, total))
    }

    const onError = (error: Error): void => {
      cleanup()
      reject(error)
    }

    request.on('data', onData)
    request.on('end', onEnd)
    request.on('error', onError)
  })
}
