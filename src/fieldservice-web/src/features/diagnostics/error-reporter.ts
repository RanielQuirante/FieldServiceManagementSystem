import { isApiError } from '@/shared/api/api-error'
import { useToasts } from '@/shared/composables/use-toasts'

export type ErrorSource = 'app.errorHandler' | 'window.unhandledrejection' | 'router.onError'

export interface ClientErrorReport {
  readonly message: string
  readonly source: ErrorSource
  readonly route: string | null
  readonly correlationId: string | null
  readonly stack: string | null
  readonly occurredAt: string
}

const unknownErrorMessage = 'Unknown error'

function describeError(error: unknown): string {
  if (error instanceof Error && error.message.length > 0) {
    return error.message
  }

  if (typeof error === 'string' && error.length > 0) {
    return error
  }

  return unknownErrorMessage
}

function stackOf(error: unknown): string | null {
  return error instanceof Error && typeof error.stack === 'string' ? error.stack : null
}

function correlationIdOf(error: unknown): string | null {
  return isApiError(error) ? error.correlationId : null
}

export function reportError(
  error: unknown,
  source: ErrorSource,
  route: string | null,
): ClientErrorReport {
  const report: ClientErrorReport = {
    message: describeError(error),
    source,
    route,
    correlationId: correlationIdOf(error),
    stack: stackOf(error),
    occurredAt: new Date().toISOString(),
  }
  useToasts().showError(report.correlationId)
  return report
}
