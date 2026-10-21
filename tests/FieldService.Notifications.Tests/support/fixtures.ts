import { randomBytes } from 'node:crypto'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

export type Environment = Record<string, string | undefined>

export function createWebhookSecret(byteLength = 32): string {
  return `whsec_${randomBytes(byteLength).toString('base64')}`
}

export function createSocketTicketKey(byteLength = 32): string {
  return randomBytes(byteLength).toString('base64')
}

export function createRequiredEnvironment(overrides: Environment = {}): Environment {
  return {
    LOG_DIR: join(tmpdir(), 'notification-tests-unused-log-dir'),
    WEBHOOK_SECRETS: createWebhookSecret(),
    SOCKET_TICKET_KEY: createSocketTicketKey(),
    SOCKET_ALLOWED_ORIGINS: 'http://localhost:5173 http://127.0.0.1:5173',
    ...overrides,
  }
}

export function createTestEnvironment(overrides: Environment = {}): Environment {
  return createRequiredEnvironment({
    HOST: '127.0.0.1',
    PORT: '0',
    NODE_ENV: 'test',
    LOG_LEVEL: 'debug',
    LOG_PRETTY: 'false',
    ...overrides,
  })
}
