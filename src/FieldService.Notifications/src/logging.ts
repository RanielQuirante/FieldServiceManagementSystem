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
  '*.ticket',
  '*.token',
  '*.password',
  '*.secret',
  '*.signature',
  '*.webhookSecrets',
  '*.socketTicketKey',
]

export const redactCensor = '[redacted]'

const transports = new WeakMap<Logger, LogTransport>()

function loggerOptions(config: LoggingConfig): LoggerOptions {
  return {
    level: config.logLevel,
    base: null,
    timestamp: pino.stdTimeFunctions.isoTime,
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
