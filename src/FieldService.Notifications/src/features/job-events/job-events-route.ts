import type { IncomingHttpHeaders, IncomingMessage } from 'node:http'
import type { Config } from '../../config.ts'
import type { Logger } from '../../logging.ts'
import type { Clock } from '../../shared/clock.ts'
import { readBody } from '../../shared/http/body-reader.ts'
import {
  HttpError,
  InvalidPayloadError,
  UnsupportedMediaTypeError,
} from '../../shared/http/http-errors.ts'
import { correlationHeaders, correlationIdOf, sendJson } from '../../shared/http/responses.ts'
import type { RouteHandler, Router } from '../../shared/http/router.ts'
import { administratorsRoom } from '../../shared/socket-types.ts'
import type { AdminBroadcaster } from './job-event-broadcast.ts'
import { envelopeRejectionReasons, parseEnvelope } from './job-event-envelope.ts'
import type { RecentEventIds } from './recent-event-ids.ts'
import { verifyWebhook, webhookHeaders } from './webhook-signature.ts'

export const jobEventsPath = '/internal/job-events'
export const maxEventBytes = 16_384

const eventIdPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/
const jsonMediaType = 'application/json'

export type JobEventOutcome = 'broadcast' | 'duplicate' | 'ignored'

export interface JobEventsRouteDependencies {
  readonly config: Pick<Config, 'webhookSecrets' | 'webhookToleranceSeconds'>
  readonly logger: Logger
  readonly clock: Clock
  readonly recentEventIds: RecentEventIds
  readonly broadcaster: AdminBroadcaster
}

function claimedEventId(headers: IncomingHttpHeaders): string | undefined {
  const value = headers[webhookHeaders.id]
  return typeof value === 'string' && eventIdPattern.test(value) ? value : undefined
}

function mediaTypeOf(request: IncomingMessage): string | undefined {
  return request.headers['content-type']?.split(';')[0]?.trim().toLowerCase()
}

export function createJobEventsRoute(dependencies: JobEventsRouteDependencies): RouteHandler {
  const { config, logger, clock, recentEventIds, broadcaster } = dependencies

  return async function handleJobEvent(request, response) {
    const headerCorrelationId = correlationIdOf(request)
    const claimed = logger.child({
      eventId: claimedEventId(request.headers),
      correlationId: headerCorrelationId,
    })
    const answer = (outcome: JobEventOutcome): void => {
      sendJson(response, 202, { outcome }, correlationHeaders(headerCorrelationId))
    }

    try {
      if (mediaTypeOf(request) !== jsonMediaType) {
        throw new UnsupportedMediaTypeError()
      }

      const body = await readBody(request, maxEventBytes)
      const webhookId = verifyWebhook(request.headers, body, {
        secrets: config.webhookSecrets,
        toleranceSeconds: config.webhookToleranceSeconds,
        nowMs: clock.now(),
      })
      const { envelope, event } = parseEnvelope(body)
      if (envelope.id !== webhookId) {
        throw new InvalidPayloadError(envelopeRejectionReasons.idMismatch)
      }

      const log = logger.child({
        eventId: envelope.id,
        correlationId: envelope.correlationId,
        type: envelope.type,
        version: envelope.version,
        jobId: event?.data.jobId,
      })
      log.info('job-event.received')

      if (!recentEventIds.add(webhookId)) {
        log.info('job-event.duplicate')
        answer('duplicate')
        return
      }

      if (event === null) {
        log.warn('job-event.ignored')
        answer('ignored')
        return
      }

      const recipients = broadcaster.broadcast(event)
      log.info({ room: administratorsRoom, recipients }, 'job-event.broadcast')
      answer('broadcast')
    } catch (error) {
      if (error instanceof HttpError) {
        claimed.error({ reason: error.reason, status: error.status }, 'job-event.rejected')
      }
      throw error
    }
  }
}

export function registerJobEventsRoute(
  router: Router,
  dependencies: JobEventsRouteDependencies,
): void {
  router.post(jobEventsPath, createJobEventsRoute(dependencies))
}
