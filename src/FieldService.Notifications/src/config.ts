import { isIP } from 'node:net'
import { z } from 'zod'

export const nodeEnvironments = ['development', 'test', 'production'] as const

export type NodeEnvironment = (typeof nodeEnvironments)[number]

export const logLevels = ['fatal', 'error', 'warn', 'info', 'debug', 'trace', 'silent'] as const

export type LogLevel = (typeof logLevels)[number]

export interface Config {
  readonly host: string
  readonly port: number
  readonly nodeEnv: NodeEnvironment
  readonly logLevel: LogLevel
  readonly logDir: string
  readonly logPretty: boolean
  readonly webhookSecrets: readonly Uint8Array[]
  readonly webhookToleranceSeconds: number
  readonly socketTicketKey: Uint8Array
  readonly socketTicketIssuer: string
  readonly socketTicketAudience: string
  readonly socketAllowedOrigins: readonly string[]
  readonly socketSessionMaxSeconds: number
}

export type Environment = Readonly<Record<string, string | undefined>>

export interface ConfigIssue {
  readonly variable: string
  readonly rule: string
}

export class ConfigError extends Error {
  readonly issues: readonly ConfigIssue[]

  constructor(issues: readonly ConfigIssue[]) {
    const details = issues.map((issue) => `${issue.variable} ${issue.rule}`).join('; ')
    super(`config.invalid: ${details}`)
    this.name = 'ConfigError'
    this.issues = issues
  }
}

const requiredRule = 'is required'
const hostRule = 'must be an IP literal or a host name'
const nodeEnvironmentRule = 'must be development, test or production'
const logLevelRule = 'must be fatal, error, warn, info, debug, trace or silent'
const logPrettyRule = 'must be true or false'
const webhookSecretCountRule = 'must hold 1 or 2 space-separated entries'
const webhookSecretFormatRule = 'entries must be whsec_ followed by base64 of at least 32 bytes'
const socketTicketKeyFormatRule = 'must be base64 of at least 32 bytes'
const publishedTestKeyRule = 'must not be a published test key'
const patternedKeyRule = 'must not be a patterned key'
const originCountRule = 'must hold 1 to 10 space-separated origins'
const originFormatRule = 'entries must be absolute http or https origins with no path and no *'

const webhookSecretPrefix = 'whsec_'
const maximumWebhookSecrets = 2
const maximumAllowedOrigins = 10
const minimumKeyBytes = 32
const publishedTestKeyLength = 32
const publishedTestKeyStarts: readonly number[] = [0x00, 0x20, 0x40]

const hostNamePattern =
  /^(?=.{1,253}$)[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*$/
const integerPattern = /^\d+$/
const listSeparator = /\s+/

function unsetWhenBlank(value: unknown): unknown {
  if (typeof value !== 'string') {
    return value
  }
  const trimmed = value.trim()
  return trimmed === '' ? undefined : trimmed
}

function isHost(value: string): boolean {
  return isIP(value) !== 0 || hostNamePattern.test(value)
}

function withoutPadding(text: string): string {
  let end = text.length
  while (end > 0 && text[end - 1] === '=') {
    end -= 1
  }
  return text.slice(0, end)
}

function decodeBase64(text: string): Uint8Array | undefined {
  const bytes = Buffer.from(text, 'base64')
  return withoutPadding(bytes.toString('base64')) === withoutPadding(text) ? bytes : undefined
}

function isPublishedTestKey(bytes: Uint8Array): boolean {
  return (
    bytes.length === publishedTestKeyLength &&
    publishedTestKeyStarts.some((start) => bytes.every((value, index) => value === start + index))
  )
}

function isPatternedKey(bytes: Uint8Array): boolean {
  const steps = new Set<number>()
  let previous: number | undefined
  for (const value of bytes) {
    if (previous !== undefined) {
      steps.add((value - previous + 256) % 256)
    }
    previous = value
  }
  return steps.size <= 1
}

function decodeKey(text: string, formatRule: string): Uint8Array | string {
  const bytes = decodeBase64(text)
  if (bytes === undefined || bytes.length < minimumKeyBytes) {
    return formatRule
  }
  if (isPublishedTestKey(bytes)) {
    return publishedTestKeyRule
  }
  if (isPatternedKey(bytes)) {
    return patternedKeyRule
  }
  return bytes
}

function decodeWebhookSecret(entry: string): Uint8Array | string {
  if (!entry.startsWith(webhookSecretPrefix)) {
    return webhookSecretFormatRule
  }
  return decodeKey(entry.slice(webhookSecretPrefix.length), webhookSecretFormatRule)
}

function isOrigin(entry: string): boolean {
  if (entry.includes('*')) {
    return false
  }
  try {
    const url = new URL(entry)
    return (url.protocol === 'http:' || url.protocol === 'https:') && url.origin === entry
  } catch {
    return false
  }
}

function integerInRange(minimum: number, maximum: number) {
  const rule = `must be an integer from ${minimum} to ${maximum}`
  return z
    .string({ error: rule })
    .regex(integerPattern, { error: rule })
    .transform((text) => Number(text))
    .refine((value) => value >= minimum && value <= maximum, { error: rule })
}

function requiredText() {
  return z.string({ error: requiredRule })
}

const environmentSchema = z.object({
  HOST: z.preprocess(
    unsetWhenBlank,
    z.string({ error: hostRule }).refine(isHost, { error: hostRule }).default('127.0.0.1'),
  ),
  PORT: z.preprocess(unsetWhenBlank, integerInRange(0, 65535).default(5090)),
  NODE_ENV: z.preprocess(
    unsetWhenBlank,
    z.enum(nodeEnvironments, { error: nodeEnvironmentRule }).default('production'),
  ),
  LOG_LEVEL: z.preprocess(
    unsetWhenBlank,
    z.enum(logLevels, { error: logLevelRule }).default('info'),
  ),
  LOG_DIR: z.preprocess(unsetWhenBlank, requiredText()),
  LOG_PRETTY: z.preprocess(
    unsetWhenBlank,
    z.enum(['true', 'false'], { error: logPrettyRule }).default('false'),
  ),
  WEBHOOK_SECRETS: z.preprocess(
    unsetWhenBlank,
    requiredText().transform((text, context) => {
      const entries = text.split(listSeparator)
      if (entries.length > maximumWebhookSecrets) {
        context.addIssue(webhookSecretCountRule)
        return z.NEVER
      }
      const secrets: Uint8Array[] = []
      for (const entry of entries) {
        const secret = decodeWebhookSecret(entry)
        if (typeof secret === 'string') {
          context.addIssue(secret)
          return z.NEVER
        }
        secrets.push(secret)
      }
      return Object.freeze(secrets)
    }),
  ),
  WEBHOOK_TOLERANCE_SECONDS: z.preprocess(unsetWhenBlank, integerInRange(1, 300).default(300)),
  SOCKET_TICKET_KEY: z.preprocess(
    unsetWhenBlank,
    requiredText().transform((text, context) => {
      const key = decodeKey(text, socketTicketKeyFormatRule)
      if (typeof key === 'string') {
        context.addIssue(key)
        return z.NEVER
      }
      return key
    }),
  ),
  SOCKET_TICKET_ISSUER: z.preprocess(unsetWhenBlank, z.string().default('fieldservice-api')),
  SOCKET_TICKET_AUDIENCE: z.preprocess(
    unsetWhenBlank,
    z.string().default('fieldservice-notifications'),
  ),
  SOCKET_ALLOWED_ORIGINS: z.preprocess(
    unsetWhenBlank,
    requiredText().transform((text, context) => {
      const entries = text.split(listSeparator)
      if (entries.length > maximumAllowedOrigins) {
        context.addIssue(originCountRule)
        return z.NEVER
      }
      if (!entries.every(isOrigin)) {
        context.addIssue(originFormatRule)
        return z.NEVER
      }
      return Object.freeze(entries)
    }),
  ),
  SOCKET_SESSION_MAX_SECONDS: z.preprocess(unsetWhenBlank, integerInRange(1, 3600).default(900)),
})

export function loadConfig(environment: Environment): Config {
  const result = environmentSchema.safeParse(environment)
  if (!result.success) {
    throw new ConfigError(
      result.error.issues.map((issue) => ({
        variable: String(issue.path[0] ?? 'environment'),
        rule: issue.message,
      })),
    )
  }

  const values = result.data
  return Object.freeze({
    host: values.HOST,
    port: values.PORT,
    nodeEnv: values.NODE_ENV,
    logLevel: values.LOG_LEVEL,
    logDir: values.LOG_DIR,
    logPretty: values.LOG_PRETTY === 'true' && values.NODE_ENV === 'development',
    webhookSecrets: values.WEBHOOK_SECRETS,
    webhookToleranceSeconds: values.WEBHOOK_TOLERANCE_SECONDS,
    socketTicketKey: values.SOCKET_TICKET_KEY,
    socketTicketIssuer: values.SOCKET_TICKET_ISSUER,
    socketTicketAudience: values.SOCKET_TICKET_AUDIENCE,
    socketAllowedOrigins: values.SOCKET_ALLOWED_ORIGINS,
    socketSessionMaxSeconds: values.SOCKET_SESSION_MAX_SECONDS,
  })
}
