import { defineStore } from 'pinia'

import { useSessionStore } from '@/features/auth/session-store'
import { isApiError } from '@/shared/api/api-error'
import { useToasts } from '@/shared/composables/use-toasts'

import { version as packageVersion } from '../../../package.json'
import { reportClientError, type ReportClientErrorRequest } from './diagnostics-api'

export type ErrorSource = 'app.errorHandler' | 'window.unhandledrejection' | 'router.onError'

export interface ClientErrorReport {
  readonly message: string
  readonly source: ErrorSource
  readonly route: string | null
  readonly correlationId: string | null
  readonly stack: string | null
  readonly occurredAt: string
  readonly appVersion: string | null
}

export const messageMaximumLength = 500

export const routeMaximumLength = 200

export const stackMaximumLength = 2000

export const routePatternText = '^/[A-Za-z0-9/_.~%-]*$'

export const correlationIdPatternText = '^[A-Za-z0-9._-]{1,64}$'

export const appVersionPatternText = '^[0-9A-Za-z.+-]{1,32}$'

export const reportLimit = 10

export const reportWindowMilliseconds = 60_000

export const defaultReportPauseSeconds = 60

export const rememberedReports = 100

export const redactedText = '[redacted]'

export const redactedQuery = '?[redacted]'

const routePattern = new RegExp(routePatternText)

const correlationIdPattern = new RegExp(correlationIdPatternText)

const appVersionPattern = new RegExp(appVersionPatternText)

const unknownErrorMessage = 'Unknown error'

export const unreadableResponseMessage = 'A response could not be read as JSON'

export const thrownStringMessage = 'A string was thrown instead of an Error'

export const thrownValueMessage = 'A value was thrown instead of an Error'

interface QuoteKind {
  readonly open: string
  readonly close: string
  readonly opening: RegExp
  readonly closing: RegExp
}

const quoteKinds: readonly QuoteKind[] = [
  { open: "'", close: "'", opening: /(?:^|[^\p{L}\p{N}])'/u, closing: /'(?![\p{L}\p{N}])/gu },
  { open: '"', close: '"', opening: /"/, closing: /"/g },
  { open: '`', close: '`', opening: /`/, closing: /`/g },
  {
    open: '\u{2018}',
    close: '\u{2019}',
    opening: /\u{2018}/u,
    closing: /\u{2019}(?![\p{L}\p{N}])/gu,
  },
  { open: '\u{201c}', close: '\u{201d}', opening: /\u{201c}/u, closing: /\u{201d}/gu },
]

const stackFrame = /^\s+at\s/

const allowedControls: ReadonlySet<number> = new Set([0x09, 0x0a, 0x0d])

interface Scrub {
  readonly pattern: RegExp
  readonly replacement: string
}

const scrubs: readonly Scrub[] = [
  { pattern: /eyJ[A-Za-z0-9_-]{5,}(?:\.[A-Za-z0-9_-]*){0,2}/g, replacement: redactedText },
  { pattern: /bearer\s+[A-Za-z0-9._~+/=-]+/gi, replacement: redactedText },
  { pattern: /whsec_[A-Za-z0-9+/=]+/g, replacement: redactedText },
  {
    pattern: /(^|[^A-Za-z0-9._%+-])[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}/g,
    replacement: `$1${redactedText}`,
  },
  { pattern: /(^|[^A-Za-z0-9_-])[A-Za-z0-9_-]{43,}/g, replacement: `$1${redactedText}` },
  { pattern: /\?[^\s)]*/g, replacement: redactedQuery },
]

export function scrubClientErrorText(text: string): string {
  return scrubs.reduce((current, scrub) => current.replace(scrub.pattern, scrub.replacement), text)
}

function isSurrogate(code: number): boolean {
  return code >= 0xd800 && code <= 0xdfff
}

function isHighSurrogate(code: number): boolean {
  return code >= 0xd800 && code <= 0xdbff
}

function isDisallowedControl(code: number): boolean {
  return (code <= 0x1f && !allowedControls.has(code)) || (code >= 0x7f && code <= 0x9f)
}

function safeCharacter(character: string): string {
  const code = character.charCodeAt(0)
  if (character.length === 1 && isSurrogate(code)) {
    return '�'
  }
  return isDisallowedControl(code) ? ' ' : character
}

function printable(text: string): string {
  return Array.from(text, safeCharacter).join('')
}

function truncate(text: string, limit: number): string {
  if (text.length <= limit) {
    return text
  }
  const end = isHighSurrogate(text.charCodeAt(limit - 1)) ? limit - 1 : limit
  return text.slice(0, end)
}

function cleanText(text: string, limit: number): string {
  return truncate(scrubClientErrorText(printable(text)).trim(), limit).trimEnd()
}

function lastClosingIndex(text: string, closing: RegExp): number {
  let last = -1
  for (const match of text.matchAll(closing)) {
    last = match.index
  }
  return last
}

function withoutQuotedSegment(text: string, kind: QuoteKind): string {
  const opening = kind.opening.exec(text)
  if (opening === null) {
    return text
  }
  const start = opening.index + opening[0].length - kind.open.length
  const end = lastClosingIndex(text, kind.closing)
  if (end <= start) {
    return text
  }
  const redacted = `${kind.open}${redactedText}${kind.close}`
  return `${text.slice(0, start)}${redacted}${text.slice(end + kind.close.length)}`
}

export function withoutQuotedText(text: string): string {
  return quoteKinds.reduce(withoutQuotedSegment, text)
}

export function isJsonParseFailure(error: unknown): boolean {
  return (
    error instanceof Error &&
    (error instanceof SyntaxError || error.name === 'SyntaxError') &&
    error.message.includes('JSON')
  )
}

function describeError(error: unknown): string {
  if (isApiError(error)) {
    return error.message
  }
  if (isJsonParseFailure(error)) {
    return unreadableResponseMessage
  }
  if (error instanceof Error) {
    return error.message.length > 0 ? withoutQuotedText(error.message) : unknownErrorMessage
  }
  if (typeof error === 'string') {
    return thrownStringMessage
  }
  return error === null || error === undefined ? unknownErrorMessage : thrownValueMessage
}

export function stackFrames(stack: string): string {
  const lines = stack.split('\n')
  const first = lines.findIndex((line) => stackFrame.test(line))
  return first < 0 ? stack : lines.slice(first).join('\n')
}

function stackOf(error: unknown): string | null {
  if (!(error instanceof Error) || typeof error.stack !== 'string' || isJsonParseFailure(error)) {
    return null
  }
  return stackFrames(withoutQuotedText(error.stack))
}

function correlationIdOf(error: unknown): string | null {
  if (!isApiError(error) || error.correlationId === null) {
    return null
  }
  return correlationIdPattern.test(error.correlationId) ? error.correlationId : null
}

export function isClientErrorReference(value: string | null): value is string {
  return value !== null && correlationIdPattern.test(value)
}

export function clientErrorRoute(value: string | null): string | null {
  if (value === null) {
    return null
  }
  const path = value.split(/[?#]/, 1)[0] ?? ''
  if (path.length > routeMaximumLength || !routePattern.test(path)) {
    return null
  }
  return scrubClientErrorText(path) === path ? path : null
}

export function clientAppVersion(value: string = packageVersion): string | null {
  return appVersionPattern.test(value) ? value : null
}

export function buildClientErrorReport(
  error: unknown,
  source: ErrorSource,
  route: string | null,
  occurredAt: Date = new Date(),
): ClientErrorReport {
  const message = cleanText(describeError(error), messageMaximumLength)
  const stack = stackOf(error)
  const cleanStack = stack === null ? '' : cleanText(stack, stackMaximumLength)
  return {
    message: message.length > 0 ? message : unknownErrorMessage,
    source,
    route: clientErrorRoute(route),
    correlationId: correlationIdOf(error),
    stack: cleanStack.length > 0 ? cleanStack : null,
    occurredAt: occurredAt.toISOString(),
    appVersion: clientAppVersion(),
  }
}

function reportKey(report: ClientErrorReport): string {
  return JSON.stringify([
    report.source,
    report.message,
    report.route,
    report.correlationId,
    report.stack,
  ])
}

function requestOf(report: ClientErrorReport): ReportClientErrorRequest {
  return {
    message: report.message,
    source: report.source,
    route: report.route,
    correlationId: report.correlationId,
    stack: report.stack,
    occurredAt: report.occurredAt,
    appVersion: report.appVersion,
  }
}

function trimOldest<Key>(entries: Set<Key> | Map<Key, unknown>): void {
  if (entries.size <= rememberedReports) {
    return
  }
  const oldest = entries.keys().next()
  if (oldest.done !== true) {
    entries.delete(oldest.value)
  }
}

const useClientErrorReporter = defineStore('client-error-reporter', () => {
  const toasts = useToasts()
  const sentKeys = new Set<string>()
  const toastByKey = new Map<string, number>()
  let sentAt: number[] = []
  let pausedUntil = 0

  function shownToast(key: string): number | null {
    const id = toastByKey.get(key)
    return id !== undefined && toasts.toasts.some((toast) => toast.id === id) ? id : null
  }

  function takeSlot(): boolean {
    const now = Date.now()
    if (now < pausedUntil) {
      return false
    }
    sentAt = sentAt.filter((at) => now - at < reportWindowMilliseconds)
    if (sentAt.length >= reportLimit) {
      return false
    }
    sentAt.push(now)
    return true
  }

  function pauseAfter(error: unknown): void {
    if (isApiError(error) && error.status === 429) {
      const seconds = error.retryAfterSeconds ?? defaultReportPauseSeconds
      pausedUntil = Date.now() + seconds * 1_000
    }
  }

  async function send(report: ClientErrorReport, toastId: number | null): Promise<void> {
    try {
      const reference = await reportClientError(requestOf(report))
      if (toastId !== null && isClientErrorReference(reference)) {
        toasts.attachReference(toastId, reference)
      }
    } catch (error) {
      pauseAfter(error)
    }
  }

  function handle(report: ClientErrorReport): void {
    const key = reportKey(report)
    if (sentKeys.has(key)) {
      return
    }
    const toastId = shownToast(key) ?? toasts.showError(report.correlationId)
    toastByKey.delete(key)
    toastByKey.set(key, toastId)
    trimOldest(toastByKey)
    if (!useSessionStore().isAdministrator || !takeSlot()) {
      return
    }
    sentKeys.add(key)
    trimOldest(sentKeys)
    void send(report, report.correlationId === null ? toastId : null)
  }

  return { handle }
})

export function reportError(
  error: unknown,
  source: ErrorSource,
  route: string | null,
): ClientErrorReport {
  const report = buildClientErrorReport(error, source, route)
  useClientErrorReporter().handle(report)
  return report
}
