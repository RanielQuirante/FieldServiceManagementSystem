import { loadConfig } from '../../../src/FieldService.Notifications/src/config.ts'
import { createLogger } from '../../../src/FieldService.Notifications/src/logging.ts'
import {
  createNotificationServer,
  type NotificationServer,
} from '../../../src/FieldService.Notifications/src/server.ts'
import { createFakeClock, type FakeClock } from './clock.ts'
import { createTestEnvironment, type Environment } from './fixtures.ts'
import { createMemoryLog, type MemoryLog } from './memory-log.ts'
import { boundPort } from './ports.ts'

export interface TestServer {
  readonly baseUrl: string
  readonly io: NotificationServer['io']
  readonly logs: MemoryLog
  readonly clock: FakeClock
  close(): Promise<void>
}

export async function startServer(
  environmentOverrides: Environment = {},
  clock: FakeClock = createFakeClock(),
): Promise<TestServer> {
  const config = loadConfig(createTestEnvironment(environmentOverrides))
  const logs = createMemoryLog()
  const logger = createLogger(config, logs.destination)
  const server = createNotificationServer({ config, logger, clock })
  await server.listen()

  return {
    baseUrl: `http://127.0.0.1:${boundPort(server.httpServer)}`,
    io: server.io,
    logs,
    clock,
    close: () => server.close(),
  }
}
