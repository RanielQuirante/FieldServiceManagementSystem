# Event contracts

This document describes how a change to a job travels from the API to every open dashboard: a signed webhook from the API to the notification service, then a Socket.IO event from the notification service to the browser. The change rules are in [README.md](README.md).

Events are invalidation hints, never state. The dashboard refetches the job and the lists it shows when an event arrives, so a lost event delays a refresh but never corrupts data.

## Contents

- [Internal webhook](#internal-webhook)
- [Socket.IO](#socketio)
- [Fixtures and test vectors](#fixtures-and-test-vectors)

## Internal webhook

The API sends one signed request per committed change to the notification service, in the [Standard Webhooks](https://www.standardwebhooks.com/) format. The route exists only on the notification service. It binds to 127.0.0.1 in development and to the container network otherwise, and it is never proxied by the dashboard's server.

### Request

| Part | Value |
|---|---|
| URL | `{Notifications:BaseUrl}/internal/job-events`; `http://127.0.0.1:5090` in development |
| Method | POST |
| `Content-Type` | `application/json; charset=utf-8`; the service accepts `application/json` with or without parameters |
| `webhook-id` | The event id: the envelope's `id`, a UUIDv7, the same on every retry |
| `webhook-timestamp` | Unix seconds as decimal digits, taken when the event is signed |
| `webhook-signature` | `v1,` followed by the base64 HMAC; several space-separated signatures are allowed, and the API sends one; at most 1024 characters |
| `X-Correlation-ID` | The correlation id of the request that made the change; equal to the envelope's `correlationId` |
| Body | The envelope, serialized once, compact, UTF-8, at most 16 KB |

Retries inside one dispatch resend the same bytes and headers.

### Signature

1. The secret is `whsec_` followed by the standard, padded base64 of at least 32 random bytes. The API holds one secret. The notification service holds one or two, newest first, so that the secret can be rotated without downtime: add the new secret to the service, switch the API, then remove the old one.
2. The key is the base64-decoded bytes after the `whsec_` prefix, never the text itself.
3. The signed content is the UTF-8 bytes of `{webhook-id}.{webhook-timestamp}.{body}`, where `body` is the exact raw request body.
4. The signature is the standard, padded base64 of HMAC-SHA256 over the signed content under the key. The header value is `v1,{signature}`.
5. To verify, the service reads every space-separated entry, ignores entries whose version is not `v1`, requires 32 decoded bytes, and compares each in constant time with the expected HMAC under each configured secret. Any match passes. The HMAC is always computed over the raw body, never over re-serialized JSON.

### Envelope

| Member | Type | Rules |
|---|---|---|
| `id` | string (uuid) | Equals `webhook-id`, or the service answers 400 |
| `type` | string | `job.created`, `job.assigned` or `job.status-changed` |
| `version` | integer | 1 for all three types |
| `occurredAt` | string (date-time) | When the change was recorded, in UTC with millisecond precision |
| `correlationId` | string | Matches `^[A-Za-z0-9._-]{1,64}$` |
| `data` | object | The type's payload |

The API writes the members in this order. Receivers ignore members they do not know.

### Event types, version 1

The three types share one `data` shape. It holds identifiers and statuses only, never personal data.

| Member | Type | Rules |
|---|---|---|
| `jobId` | string (uuid) | |
| `jobNumber` | integer | 1 or more, a safe JavaScript integer |
| `status` | JobStatus | The status after the change |
| `previousStatus` | JobStatus or null | The status before the change; null only for `job.created` |

| Type | Sent after | `status` | `previousStatus` |
|---|---|---|---|
| `job.created` | Creating a job | `Unassigned`, or `Assigned` when a technician was chosen | null |
| `job.assigned` | Assigning a job | `Assigned` | `Unassigned` or `Assigned` |
| `job.status-changed` | Changing, submitting or cancelling a job | `InProgress`, `OnHold`, `Completed` or `Cancelled` | `Unassigned`, `Assigned`, `InProgress` or `OnHold` |

The notification service validates the shape only. Which status pairs are legal is a rule of the API.

### Checks and responses

The service checks in this order and stops at the first failure. Signatures and bodies are never logged.

| # | Check | Response | Log entry |
|---|---|---|---|
| 1 | Route and method | 404 `resource.not-found`; 405 `request.method-not-allowed` with `Allow: POST` | |
| 2 | `Content-Type` is `application/json` | 415 `request.unsupported-media-type` | `job-event.rejected`, reason `unsupported-media-type` |
| 3 | Body at most 16384 bytes | 413 `request.too-large` | `job-event.rejected`, reason `too-large` |
| 4 | The three webhook headers are present, and the timestamp is within the tolerance of the service clock (1 to 300 seconds, default 300) | 401 `webhook.unauthenticated` | `job-event.rejected`, reason `missing-headers` or `timestamp-out-of-range` |
| 5 | A `v1` signature matches a configured secret | 401 `webhook.unauthenticated` | `job-event.rejected`, reason `signature-mismatch` |
| 6 | The body is JSON, passes the envelope schema and, for a known type and version, the data schema; `id` equals `webhook-id` | 400 `webhook.invalid-payload` | `job-event.rejected`, reason `invalid-json`, `schema` or `id-mismatch`; on success `job-event.received` |
| 7 | `webhook-id` has not been accepted before | 202 `{"outcome":"duplicate"}`, no broadcast | `job-event.duplicate` |
| 8 | Known type and version | 202 `{"outcome":"ignored"}`, no broadcast | `job-event.ignored` at Warning |
| 9 | Broadcast to the administrators | 202 `{"outcome":"broadcast"}` | `job-event.broadcast`, with the room and the recipient count |

Rejections are logged at Error with the reason and, when present, the claimed `webhook-id` and the correlation id. Accepted events are logged with their event id, type, job id and correlation id, so one correlation id traces a change from the technician's request to the broadcast.

Error responses are Problem Details (`application/problem+json`) with `type`, `title`, `status` and `code`, plus `correlationId` when the header was valid.

### Deduplication

- The service remembers accepted `webhook-id` values in memory, at most 10,000, evicting the oldest first.
- An id is remembered only after the timestamp, the signature and the schema pass, so unsigned traffic cannot fill the map.
- Each id is kept for twice the tolerance plus 60 seconds from its first acceptance: 660 seconds at the default tolerance. While the service keeps running, a replayed signed request therefore meets either the map or the timestamp check. The map lives in memory, so after a restart a request sent again within the tolerance is broadcast once more.

### What the API does with each response

| Response | Behaviour |
|---|---|
| 2xx | Done |
| 400, 401, 404, 405, 413, 415 | A permanent failure: no retry, a Warning log with the event id, type, job id, correlation id and status |
| 408, 429, 5xx, a timeout or a connection error | Retried: 2 seconds per attempt, at most 3 jittered retries, a 10-second total budget and a circuit breaker; after the budget, a Warning log and the next event |

Events leave an in-process queue of 1,000 that drops the oldest when full. Delivery is at most once across an API crash, retries are idempotent through deduplication, and a failed delivery never rolls back a change.

### Example

The event sent after a technician starts JOB-1049. The body is shown compact, exactly as it is signed:

```http
POST /internal/job-events HTTP/1.1
Host: 127.0.0.1:5090
Content-Type: application/json; charset=utf-8
Content-Length: 303
webhook-id: 01a1549c-bac0-7272-8af7-8acf82fc5f4d
webhook-timestamp: 1792420920
webhook-signature: v1,B5DJ9sRBYzYxs1BVwRzk/MdWnRyfFXvXOEnZjUt9lWc=
X-Correlation-ID: 23191419-f180-45eb-bcaf-8d9912281343
```

```json
{"id":"01a1549c-bac0-7272-8af7-8acf82fc5f4d","type":"job.status-changed","version":1,"occurredAt":"2026-10-19T14:42:00.000Z","correlationId":"23191419-f180-45eb-bcaf-8d9912281343","data":{"jobId":"01a153ec-f2c0-7499-a012-b2be45d90129","jobNumber":1049,"status":"InProgress","previousStatus":"Assigned"}}
```

The response is `202 Accepted` with `{"outcome":"broadcast"}`. The signature was made with the published test secret in `events/signature-test-vector.json`.

### Versioning

- A member may be added within a version; receivers ignore unknown members.
- A breaking change ships as a new version of the type, in parallel: the notification service learns to accept both versions, the API switches to the new one, and a later release drops the old one.
- An unknown type or version is acknowledged with 202 and a warning, so the API never retries it. New event types follow the same steps.

## Socket.IO

Only the web dashboard holds sockets. The technician app reloads when a screen appears and on pull to refresh.

### Endpoint

| Item | Value |
|---|---|
| URL | The dashboard's own origin, through its development server or its web server |
| Path | `/socket.io/` |
| Protocol | Socket.IO 4.8 on the default namespace only; any other namespace is refused |
| Transport | WebSocket only, on both sides |
| Origin check | A request whose `Origin` is not in `SOCKET_ALLOWED_ORIGINS` is refused before the handshake; requests without an `Origin`, such as test clients, rely on the ticket alone |
| Server options | No served client, a 4 KB message limit, no connection state recovery, the default ping interval and timeout, a single instance |
| Client to server events | None; frames from the client are ignored, and one larger than 4 KB closes the connection |

### Handshake and ticket

The dashboard asks the API for a socket ticket (`POST /api/auth/socket-tickets`) before every connect and reconnect, and sends it only in the handshake's `auth` object, never in a URL or cookie. A failed ticket request still completes the handshake with an empty `auth`, which the service refuses, so the connection never stalls.

The ticket is a compact JWS with the header `{"alg":"HS256","typ":"JWT"}`, signed with its own key: the API calls it `Auth:SocketTicketKey` and the notification service `SOCKET_TICKET_KEY`. That key is separate from the access-token key, so the notification service never holds a credential the API accepts.

| Claim | Value |
|---|---|
| `iss` | `fieldservice-api` |
| `aud` | `fieldservice-notifications` |
| `sub` | The administrator's user id |
| `role` | `Administrator` |
| `jti` | A new UUIDv7 per ticket |
| `iat`, `nbf`, `exp` | `nbf` equals `iat`; `exp` is 60 seconds later |

The service accepts only HS256, the issuer and audience above, a ticket at most 60 seconds old with 5 seconds of clock tolerance, every claim present, a uuid `sub` and the `Administrator` role. Each `jti` is accepted once: the service remembers verified ids for 120 seconds, at most 10,000 of them, and evicts only entries older than 65 seconds. When nothing can be evicted, the handshake is refused.

### Connection errors

The client receives a refusal as `connect_error` with `data.code`:

| Code | Cause | Client reaction |
|---|---|---|
| `ticket_missing` | No ticket in the handshake | Retry with a backoff: 1 second doubling to 30, with jitter |
| `ticket_invalid` | Bad signature, wrong issuer or audience, expired, not yet valid, a missing claim, a malformed `sub`, a reused `jti`, or a full `jti` cache | Refresh the session once and connect again; if it repeats, use the backoff |
| `forbidden` | A verified ticket whose role is not `Administrator` | Refresh once and connect again; a second refusal shows "Live updates are paused" |
| none | The origin was refused, the network failed or the service is down | Socket.IO's own reconnection: 1 to 5 seconds, with randomization 0.5 |

A refusal by the service, which always carries a code, stops Socket.IO's automatic reconnection, which is why the dashboard connects again by itself. A failure before the handshake reaches the service, such as a refused origin, a network failure or a service that is down, leaves it running. A failed ticket request also uses the backoff, except that a 401 `auth.session-expired` ends the session and a 429 waits for the longer of the backoff step and its `Retry-After`, or 60 seconds without one.

Every refusal is logged as `socket.rejected` with its code, never with the ticket.

### Room, session cap and disconnects

- An accepted socket joins the `administrators` room, the only room, and is logged as `socket.connected`.
- After `SOCKET_SESSION_MAX_SECONDS` (default 900) the service disconnects the socket. The dashboard reconnects with a fresh ticket while the user is signed in, so authorization is checked again regularly and every reconnect doubles as a resync.
- After a network loss or a service restart, Socket.IO reconnects by itself, with a delay that starts at 1 second and is capped at 5, with randomization 0.5. Each attempt that reaches the service fetches a fresh ticket.
- On shutdown the service closes every socket. Each disconnect is logged as `socket.disconnected` with the socket id, user id, reason and duration. Nothing is queued for an absent socket.

### Server events

Events go only to the `administrators` room, named after the internal event type: `job.created`, `job.assigned` and `job.status-changed`.

| Member | Type | From |
|---|---|---|
| `eventId` | string (uuid) | The envelope's `id` |
| `jobId` | string (uuid) | `data.jobId` |
| `jobNumber` | number | `data.jobNumber` |
| `status` | JobStatus | `data.status` |
| `previousStatus` | JobStatus or null | `data.previousStatus` |
| `occurredAt` | string (date-time) | The envelope's `occurredAt` |

The payload carries no names, addresses, notes or correlation ids. Members can be added; a breaking change uses a new event name.

### Dashboard behaviour

| State | Shown | Entered when |
|---|---|---|
| Connecting | The Connecting pill only | Sign-in or page load, until the first connect |
| Live | The Live pill with the time of the last refetch | A connect, after its refetch |
| Reconnecting | The pill at once, and the Reconnecting banner after 3 seconds without a connection | A disconnect or a failed attempt while signed in |
| Paused | "Live updates are paused. Showing data from {time}." with Refresh now | 30 seconds without a connection |
| Back online | "Back online. Jobs refreshed at {time}." for 8 seconds, then Live | A connect after the Reconnecting or Paused banner was shown |

On every connect, the first one included, the dashboard refetches everything the open page shows. On each event it:

1. drops a payload that fails its type guard, and an event id among the last 200 it handled;
2. refetches the affected job, at most one request in flight and one per second for each job, and uses the job's latest history entry for the toast and the row flash;
3. refetches the visible lists, debounced by 300 ms;
4. stops per-event requests above 60 a minute and runs one debounced full refetch instead, so a faulty relay cannot push a user past the API's rate limit;
5. never patches a cached row from the payload.

After a server-side disconnect the dashboard connects again while signed in, and on sign-out it disconnects.

## Fixtures and test vectors

| File | Content |
|---|---|
| `events/internal/job.created.v1.json`, `job.assigned.v1.json`, `job.status-changed.v1.json` | One envelope per type, version 1 |
| `events/client/job.created.json`, `job.assigned.json`, `job.status-changed.json` | The payload the notification service emits for each |
| `events/signature-test-vector.json` | `secret`, `id`, `timestamp` (the header's decimal digits, as a string), `body` (the compact status-changed envelope) and the expected `signature` |
| `events/socket-ticket-test-vector.json` | `key`, the expected `claims` and the signed `token` |

The fixtures use two-space indentation and LF line endings. Where bytes matter, a test first compacts a fixture with its member order preserved.

| Application | Tests |
|---|---|
| API | The envelope serializer reproduces each internal fixture byte for byte from its own field values; the webhook signer reproduces the signature vector; the ticket issuer reproduces the ticket vector's header and claims, and the vector's token validates under the API's decoding of its key |
| Notification service | The schemas accept every internal fixture and reject mutated copies; each emitted payload equals its client fixture; the verifier accepts the signature vector and rejects a changed body byte, a wrong secret, a `v2,` prefix or a stale timestamp; the ticket verifier accepts the ticket vector at its issue time and rejects a changed character, another key or a clock more than 65 seconds later |
| Dashboard | The event type guard accepts every client fixture and rejects mutated copies |

The secret and key in the two vector files are published test values. **They must never be configured in any environment**; see [README.md](README.md#published-test-values).
