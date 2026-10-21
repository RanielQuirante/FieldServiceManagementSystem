export type FieldErrors = Readonly<Record<string, readonly string[]>>

export interface ApiErrorDetails {
  readonly status: number
  readonly code: string | null
  readonly title: string | null
  readonly detail: string | null
  readonly fieldErrors: FieldErrors
  readonly correlationId: string | null
  readonly retryAfterSeconds: number | null
}

export class ApiError extends Error {
  readonly status: number
  readonly code: string | null
  readonly title: string | null
  readonly detail: string | null
  readonly fieldErrors: FieldErrors
  readonly correlationId: string | null
  readonly retryAfterSeconds: number | null

  constructor(details: ApiErrorDetails) {
    super(summarize(details))
    this.name = 'ApiError'
    this.status = details.status
    this.code = details.code
    this.title = details.title
    this.detail = details.detail
    this.fieldErrors = details.fieldErrors
    this.correlationId = details.correlationId
    this.retryAfterSeconds = details.retryAfterSeconds
  }

  get isNetworkError(): boolean {
    return this.status === 0
  }
}

export interface FetchOutcome<Data> {
  readonly data?: Data
  readonly error?: unknown
  readonly response: Response
}

const noFieldErrors: FieldErrors = Object.freeze({})

function summarize(details: ApiErrorDetails): string {
  if (details.status === 0) {
    return 'The request did not reach the server'
  }
  return details.code === null
    ? `Request failed with status ${details.status}`
    : `Request failed with status ${details.status} (${details.code})`
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function readString(source: Record<string, unknown> | null, key: string): string | null {
  const value = source?.[key]
  return typeof value === 'string' && value.length > 0 ? value : null
}

function readFieldErrors(value: unknown): FieldErrors {
  if (!isRecord(value)) {
    return noFieldErrors
  }
  const entries = Object.entries(value).flatMap(([key, messages]) => {
    if (!Array.isArray(messages)) {
      return []
    }
    const texts = messages.filter((message): message is string => typeof message === 'string')
    return texts.length > 0 ? [[key, Object.freeze(texts)] as const] : []
  })
  return entries.length > 0 ? Object.freeze(Object.fromEntries(entries)) : noFieldErrors
}

function readRetryAfter(header: string | null): number | null {
  if (header === null || !/^\d+$/.test(header.trim())) {
    return null
  }
  return Math.max(Number.parseInt(header.trim(), 10), 1)
}

export function apiErrorFromResponse(response: Response, body: unknown): ApiError {
  const problem = isRecord(body) ? body : null
  return new ApiError({
    status: response.status,
    code: readString(problem, 'code'),
    title: readString(problem, 'title'),
    detail: readString(problem, 'detail'),
    fieldErrors: readFieldErrors(problem?.errors),
    correlationId: readString(problem, 'correlationId') ?? response.headers.get('X-Correlation-ID'),
    retryAfterSeconds: readRetryAfter(response.headers.get('Retry-After')),
  })
}

export function networkError(): ApiError {
  return new ApiError({
    status: 0,
    code: null,
    title: null,
    detail: null,
    fieldErrors: noFieldErrors,
    correlationId: null,
    retryAfterSeconds: null,
  })
}

export function isApiError(value: unknown): value is ApiError {
  return value instanceof ApiError
}

export function firstFieldError(errors: FieldErrors, key: string): string | null {
  return errors[key]?.[0] ?? null
}

export function unmappedFieldErrors(errors: FieldErrors, mappedKeys: readonly string[]): string[] {
  return Object.entries(errors)
    .filter(([key]) => !mappedKeys.includes(key))
    .flatMap(([, messages]) => messages)
}

async function settle<Data>(pending: Promise<FetchOutcome<Data>>): Promise<FetchOutcome<Data>> {
  try {
    return await pending
  } catch (error) {
    if (error instanceof TypeError) {
      throw networkError()
    }
    throw error
  }
}

export async function unwrap<Data>(pending: Promise<FetchOutcome<Data>>): Promise<Data> {
  const outcome = await settle(pending)
  if (!outcome.response.ok) {
    throw apiErrorFromResponse(outcome.response, outcome.error)
  }
  if (outcome.data === undefined) {
    throw apiErrorFromResponse(outcome.response, null)
  }
  return outcome.data
}

export async function unwrapEmpty(pending: Promise<FetchOutcome<unknown>>): Promise<void> {
  const outcome = await settle(pending)
  if (!outcome.response.ok) {
    throw apiErrorFromResponse(outcome.response, outcome.error)
  }
}
