import { randomBytes, randomUUID } from 'node:crypto'
import { readFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

export type Environment = Record<string, string | undefined>

export const eventTypes = ['job.created', 'job.assigned', 'job.status-changed'] as const

export type EventType = (typeof eventTypes)[number]

export type JsonObject = { [key: string]: JsonValue }

export type JsonValue = string | number | boolean | null | JsonValue[] | JsonObject

export interface SignatureTestVector {
  readonly secret: string
  readonly id: string
  readonly timestamp: string
  readonly body: string
  readonly signature: string
}

export interface SocketTicketTestVector {
  readonly key: string
  readonly claims: JsonObject
  readonly token: string
}

const eventsDirectory = new URL('../../../docs/contracts/events/', import.meta.url)

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

export function readEventFile(relativePath: string): string {
  return readFileSync(new URL(relativePath, eventsDirectory), 'utf8')
}

function isJsonObject(value: unknown): value is JsonObject {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function parseObject(text: string, description: string): JsonObject {
  const value: unknown = JSON.parse(text)
  if (!isJsonObject(value)) {
    throw new Error(`${description} is not a JSON object`)
  }
  return value
}

function stringMember(object: JsonObject, name: string, description: string): string {
  const value = object[name]
  if (typeof value !== 'string') {
    throw new Error(`${description} has no string member ${name}`)
  }
  return value
}

export function compactJson(text: string): string {
  return JSON.stringify(JSON.parse(text))
}

export function readInternalFixtureText(type: EventType): string {
  return compactJson(readEventFile(`internal/${type}.v1.json`))
}

export function readInternalFixture(type: EventType): JsonObject {
  return parseObject(readEventFile(`internal/${type}.v1.json`), `internal/${type}.v1.json`)
}

export function readClientFixture(type: EventType): JsonObject {
  return parseObject(readEventFile(`client/${type}.json`), `client/${type}.json`)
}

export function readSignatureVector(): SignatureTestVector {
  const name = 'signature-test-vector.json'
  const vector = parseObject(readEventFile(name), name)
  return {
    secret: stringMember(vector, 'secret', name),
    id: stringMember(vector, 'id', name),
    timestamp: stringMember(vector, 'timestamp', name),
    body: stringMember(vector, 'body', name),
    signature: stringMember(vector, 'signature', name),
  }
}

export function readSocketTicketVector(): SocketTicketTestVector {
  const name = 'socket-ticket-test-vector.json'
  const vector = parseObject(readEventFile(name), name)
  const claims = vector.claims
  if (!isJsonObject(claims)) {
    throw new Error(`${name} has no claims object`)
  }
  return {
    key: stringMember(vector, 'key', name),
    claims,
    token: stringMember(vector, 'token', name),
  }
}

export function cloneJson<Value extends JsonValue>(value: Value): Value {
  return structuredClone(value)
}

export interface FreshEnvelope {
  readonly id: string
  readonly envelope: JsonObject
  readonly data: JsonObject
}

export function freshEnvelope(type: EventType, id: string = randomUUID()): FreshEnvelope {
  const envelope = cloneJson(readInternalFixture(type))
  envelope.id = id
  const data = envelope.data
  if (!isJsonObject(data)) {
    throw new Error(`internal/${type}.v1.json has no data object`)
  }
  return { id, envelope, data }
}
