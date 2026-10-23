import { resolve } from 'node:path'
import { pino, type Logger, type LoggerOptions, type TransportTargetOptions } from 'pino'
import type { Config } from './config.ts'

export type { Logger }

export interface LogDestination {
  write(line: string): void
}

export type LoggingConfig = Pick<Config, 'logLevel' | 'logDir' | 'logPretty'>

type LogTransport = ReturnType<typeof pino.transport>

export const logFileBaseName = 'notifications'

export const redactPaths: readonly string[] = [
  'req.headers.authorization',
  'req.headers.cookie',
  'req.headers["webhook-signature"]',
  'headers.authorization',
  'headers.cookie',
  'headers["webhook-signature"]',
  'handshake.auth',
  'auth',
  'ticket',
  'token',
  'password',
  'secret',
  'signature',
  'webhookSecrets',
  'socketTicketKey',
  '*.auth',
  '*.ticket',
  '*.token',
  '*.password',
  '*.secret',
  '*.signature',
  '*.webhookSecrets',
  '*.socketTicketKey',
]

export const redactCensor = '[redacted]'

export type FailureFields = { readonly err: Error } | { readonly errType: string }

export interface SerializedError {
  readonly type: string
  readonly message: string
  readonly stack: string
  readonly code?: string | number
}

function failureTypeOf(value: unknown): string {
  if (value === null) {
    return 'null'
  }
  return Array.isArray(value) ? 'array' : typeof value
}

export function failureFields(value: unknown): FailureFields {
  return value instanceof Error ? { err: value } : { errType: failureTypeOf(value) }
}

function textOf(value: unknown): string {
  return typeof value === 'string' ? value : ''
}

function errorTypeOf(error: Error): string {
  const constructor: unknown = error.constructor
  return typeof constructor === 'function' ? textOf(constructor.name) : textOf(error.name)
}

function causeOf(error: Error): Error | undefined {
  return error.cause instanceof Error ? error.cause : undefined
}

function withCauses(
  error: Error,
  textOfOne: (error: Error) => string,
  separator: string,
  circularSuffix: string,
): string {
  const seen = new Set<Error>()
  let text = ''
  let current = error
  for (;;) {
    text += textOfOne(current)
    if (seen.has(current)) {
      return text + circularSuffix
    }
    seen.add(current)
    const cause = causeOf(current)
    if (cause === undefined) {
      return text
    }
    text += separator
    current = cause
  }
}

function serializeError(value: unknown): SerializedError | { readonly errType: string } {
  if (!(value instanceof Error)) {
    return { errType: failureTypeOf(value) }
  }
  const code = 'code' in value ? value.code : undefined
  return {
    type: errorTypeOf(value),
    message: withCauses(value, (error) => textOf(error.message), ': ', ': ...'),
    stack: withCauses(
      value,
      (error) => textOf(error.stack),
      '\ncaused by: ',
      '\ncauses have become circular...',
    ),
    ...(typeof code === 'string' || typeof code === 'number' ? { code } : {}),
  }
}

const transports = new WeakMap<Logger, LogTransport>()

function loggerOptions(config: LoggingConfig): LoggerOptions {
  return {
    level: config.logLevel,
    base: null,
    timestamp: pino.stdTimeFunctions.isoTime,
    serializers: { err: serializeError },
    redact: { paths: [...redactPaths], censor: redactCensor },
  }
}

function fileTarget(config: LoggingConfig): TransportTargetOptions {
  return {
    target: 'pino-roll',
    level: config.logLevel,
    options: {
      file: resolve(config.logDir, logFileBaseName),
      frequency: 'daily',
      dateFormat: 'yyyyMMdd',
      size: '50m',
      extension: '.log',
      mkdir: true,
      limit: { count: 14, removeOtherLogFiles: true },
    },
  }
}

function consoleTarget(config: LoggingConfig): TransportTargetOptions {
  if (config.logPretty) {
    return { target: 'pino-pretty', level: config.logLevel, options: { destination: 1 } }
  }
  return { target: 'pino/file', level: config.logLevel, options: { destination: 1 } }
}

export function createLogger(config: LoggingConfig, destination?: LogDestination): Logger {
  if (destination !== undefined) {
    return pino(loggerOptions(config), destination)
  }

  const transport = pino.transport({ targets: [fileTarget(config), consoleTarget(config)] })
  const logger = pino(loggerOptions(config), transport)
  transports.set(logger, transport)
  return logger
}

export function closeLogger(logger: Logger): Promise<void> {
  const transport = transports.get(logger)
  if (transport === undefined) {
    return new Promise<void>((resolveFlush) => {
      logger.flush(() => {
        resolveFlush()
      })
    })
  }

  transports.delete(logger)
  return new Promise<void>((resolveClose) => {
    transport.once('close', () => {
      resolveClose()
    })
    transport.end()
  })
}
