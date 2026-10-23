import { ConfigError, loadConfig, type Config } from './config.ts'
import { closeLogger, createLogger, failureFields, type Logger } from './logging.ts'
import { createNotificationServer, type NotificationServer } from './server.ts'
import { systemClock } from './shared/clock.ts'

const shutdownTimeoutMs = 10_000

function readConfig(): Config | undefined {
  try {
    return loadConfig(process.env)
  } catch (error) {
    if (error instanceof ConfigError) {
      process.stderr.write(`${error.message}\n`)
      return undefined
    }
    throw error
  }
}

function exitAfterFlush(logger: Logger, code: number): void {
  process.exitCode = code
  const fallback = setTimeout(() => {
    process.exit(code)
  }, shutdownTimeoutMs)
  fallback.unref()
  void closeLogger(logger).finally(() => {
    process.exit(code)
  })
}

function installProcessHandlers(logger: Logger): void {
  process.on('uncaughtException', (error: unknown) => {
    logger.fatal(failureFields(error), 'process.uncaught-exception')
    exitAfterFlush(logger, 1)
  })
  process.on('unhandledRejection', (reason: unknown) => {
    logger.fatal(failureFields(reason), 'process.unhandled-rejection')
    exitAfterFlush(logger, 1)
  })
}

function installSignalHandlers(server: NotificationServer, logger: Logger): void {
  let stopping = false
  const stop = (signal: NodeJS.Signals): void => {
    if (stopping) {
      return
    }
    stopping = true
    logger.info({ signal }, 'server.stopping')
    const timeout = setTimeout(() => {
      logger.error({ timeoutMs: shutdownTimeoutMs }, 'server.stop-timeout')
      exitAfterFlush(logger, 1)
    }, shutdownTimeoutMs)
    timeout.unref()
    void server.close().then(() => {
      clearTimeout(timeout)
      logger.info('server.stopped')
      exitAfterFlush(logger, 0)
    })
  }
  process.on('SIGTERM', stop)
  process.on('SIGINT', stop)
}

async function main(): Promise<void> {
  const config = readConfig()
  if (config === undefined) {
    process.exitCode = 1
    return
  }

  const logger = createLogger(config)
  installProcessHandlers(logger)
  const server = createNotificationServer({ config, logger, clock: systemClock })

  try {
    const address = await server.listen()
    logger.info(address, 'server.listening')
  } catch (error) {
    logger.fatal(failureFields(error), 'server.listen-failed')
    exitAfterFlush(logger, 1)
    return
  }

  installSignalHandlers(server, logger)
}

await main()
