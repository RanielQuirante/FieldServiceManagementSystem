# 0009. Signed webhooks from the API to the notification service

## Status

Accepted, 2026-10-23.

## Context

When a job is created, assigned or changes status, the API must tell the notification service so that it can alert connected dashboards. The API owns every business rule and the only database connection; the notification service is a stateless relay and must never read the database.

Three forces shape the hop. A user's request must not wait for, or fail because of, the notification service. The service must be able to tell a genuine event from a forged or replayed one, because it forwards what it receives to every administrator. And the solution should use what the stack already has, without a message broker.

## Decision

- **Queue after commit.** After a change commits, the request's handler adds a job event to an in-memory bounded channel of 1,000 events. When the channel is full, the oldest event is dropped and a warning is logged. Adding never blocks or fails the request. With no notification service configured, the API runs with a queue that discards events.
- **One ordered dispatcher.** A background service reads the channel in order, serializes each event's envelope once and posts it to `/internal/job-events` on the notification service. The HTTP client allows 2 seconds per attempt, at most 3 jittered retries of transient failures, 10 seconds in total, and has a circuit breaker. A final failure is logged and the dispatcher moves on; it never rolls back the committed change. On shutdown it drains the channel for up to 5 seconds.
- **Standard Webhooks signatures.** Each request carries `webhook-id` (the event id, a UUIDv7), `webhook-timestamp` (Unix seconds) and `webhook-signature` (`v1,` followed by the base64 HMAC-SHA256 of `{id}.{timestamp}.{body}`). The key is the bytes decoded from the secret after its `whsec_` prefix. The request also carries `X-Correlation-ID`, so one id follows a change from the client through the API to the broadcast.
- **Checks in the notification service, in order:** the route and method (404 or 405), the media type (415), a body of at most 16 KB (413), the headers and a timestamp within the tolerance of 1 to 300 seconds, 300 by default (401), the signature against each configured secret in constant time (401), then the JSON schema with the envelope id equal to `webhook-id` (400). An id seen within twice the tolerance plus 60 seconds gets 202 with no broadcast; an unknown type or version gets 202 with a warning and no broadcast; anything else is broadcast to the administrators and answered with 202. An id is remembered only after the signature and schema checks pass, at most 10,000 of them.
- **Thin events.** An envelope carries its id, type, version, time, correlation id, and the job's id, number, status and previous status. It carries no names, addresses or other personal data.
- **Exposure and secrets.** The `/internal` path is never proxied to browsers. The service accepts one or two secrets, newest first, so the secret can be rotated without downtime. Both services refuse to start with a published test secret, and the notification service refuses a secret that decodes to the same bytes as its ticket key. `docs/contracts/events/signature-test-vector.json` holds a published secret, a body and the expected signature that the API's signer and the service's verifier must both match.

Rejected alternatives: an HTTP call inside the request, which ties request latency and availability to the service; a transactional outbox, kept as the upgrade path if events ever carry state; a message broker or PostgreSQL notifications, which add technology or give the service a database connection; a static bearer key, which can be replayed and does not bind the body; and full job objects in events.

## Consequences

- Requests never wait for the notification service, and a service outage loses only hints.
- Delivery is at most once across an API crash, because the queue is in memory. A dashboard that misses an event shows stale data until its next event, navigation or reconnect; [ADR 0010](0010-refetch-on-event-and-reconnect.md) makes that safe.
- Retries are idempotent, because the service drops an id it has already accepted, and the retention window covers every timestamp the tolerance still accepts, so while the service keeps running a replayed request meets one check or the other. The ids live in memory: after a restart, a captured request sent again within the tolerance is broadcast once more, which costs one extra hint.
- A compromised notification service can send at most false hints, which dashboards answer by refetching real data at a capped rate.
- Each event costs one signed HTTP request; the signature format is a published standard rather than invented headers.
