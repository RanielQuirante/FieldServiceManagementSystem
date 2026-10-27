# Security

This document describes how FieldService protects its users and data: the security objectives, the trust boundaries, the authentication and session model, the controls at each boundary, the operator procedures and the accepted limitations. It covers what the applications do today and grows with each feature. The decision behind the session model is [ADR 0007](decisions/0007-token-authentication-with-rotating-refresh-tokens.md); the decisions behind real-time updates are [ADR 0008](decisions/0008-socket-tickets-for-the-notification-service.md) and [ADR 0009](decisions/0009-signed-webhooks-from-api-to-notifications.md).

## Contents

- [Objectives](#objectives)
- [Trust boundaries](#trust-boundaries)
- [Authentication and sessions](#authentication-and-sessions)
- [Authorization](#authorization)
- [Real-time updates](#real-time-updates)
- [Request controls](#request-controls)
- [Headers and content security policy](#headers-and-content-security-policy)
- [Secrets and startup validation](#secrets-and-startup-validation)
- [Logging](#logging)
- [Client error reports](#client-error-reports)
- [Operator procedures](#operator-procedures)
- [Review results](#review-results)
- [Accepted limitations](#accepted-limitations)

## Objectives

1. Only authenticated users reach business endpoints, and each role reaches only its own endpoints.
2. A technician reads and changes only the jobs assigned to them.
3. Passwords, tokens, keys and signatures never appear in URLs, logs, web storage, source control or error responses.
4. Job history is append-only, a submitted inspection cannot change, and concurrent writes never silently overwrite each other.
5. Failures reveal nothing about internals, and every failure carries a correlation id for support.
6. Third-party code enters only through pinned, reviewed and licence-checked dependencies.
7. Production defaults are safe: no interactive API tooling, no demo data, no permissive cross-origin access, and TLS on every external hop.

The design assumes a TLS-terminating proxy in front of the applications in production, one instance of each service, administrators who are trusted with every job, devices protected by the platform screen lock, and synchronised clocks. Multi-factor authentication, self-service password reset, account lockout and tenant isolation are outside the scope of the current version.

## Trust boundaries

| Boundary | Between | Main protections |
|---|---|---|
| Browser to API | The dashboard and the API, on one origin | Bearer access tokens, the HttpOnly refresh cookie with a CSRF header, rate limits, input limits, security headers |
| Mobile app to API | The technician app and the API | Bearer access tokens, refresh tokens in the platform's secure storage, the same server-side controls |
| Browser to notification service | The dashboard and the notification service's Socket.IO endpoint, on the same origin | An Origin allowlist, a single-use 60-second socket ticket in the handshake, WebSocket only, small frames, no client-to-server events, a session cap |
| API to notification service | The API posting job events to `/internal/job-events` | Standard Webhooks signatures, a timestamp tolerance, id deduplication, a size limit and a strict schema; the path is never exposed to browsers |
| API to database | The API and PostgreSQL | A least-privilege application role; schema changes only through a separate migrator role ([database.md](database.md)) |
| Developer machine | Local services and secrets | Secrets created locally, stored outside the repository, never printed ([development.md](development.md)) |

## Authentication and sessions

### Passwords

- Passwords are hashed with PBKDF2-HMAC-SHA512 at 210,000 iterations, through ASP.NET Core's password hasher. A stored hash in an older format is upgraded at the next successful sign-in.
- An unknown email address is checked against a dummy hash, so a response takes the same time whether or not the account exists.
- An unknown email, a wrong password and an inactive account all get the same `401` with the code `auth.invalid-credentials`.
- Each client accepts one role: the dashboard (`client: Web`) accepts administrators and the mobile app (`client: Mobile`) technicians. The wrong account type gets `403 auth.wrong-application`, and only after the password has verified, so the answer never confirms an account to someone without its password.

### Access tokens

| Property | Value |
|---|---|
| Format | JSON Web Token, HS256 only |
| Issuer and audience | `fieldservice-api` |
| Lifetime | 15 minutes, with 30 seconds of clock skew |
| Claims | User id (`sub`), role, session id (`sid`), token id, issue and expiry times |
| Signing key | `Auth:AccessTokenKey`, at least 32 random bytes, held only by the API |
| Transport | `Authorization: Bearer` header only; a token in a query string or cookie is ignored |
| Client storage | Memory only, never web storage |

An invalid or expired token gets `401` with `WWW-Authenticate: Bearer error="invalid_token"` and no further description.

### Sessions and refresh tokens

Each sign-in creates a session and its first refresh token. A refresh token is 256 random bits; the database stores only its SHA-256 hash.

| Client | Idle expiry | Absolute expiry | Where the refresh token lives |
|---|---|---|---|
| Dashboard | 60 minutes | 12 hours | The `fs_refresh` cookie |
| Mobile app | 7 days | 30 days | The platform's secure storage, sent in the request body |

Every refresh rotates the token in one transaction that locks the session:

- The presented token is consumed and a successor is issued. The session's idle expiry moves forward, never past its absolute expiry.
- **Grace.** A token presented again less than 30 seconds after its rotation, while its successor is unused, counts as a retry after a lost response: the unused successor is revoked and a new one is issued.
- **Reuse.** Any other use of a consumed token revokes the whole session with the reason `RefreshTokenReuse` and is logged as a security event. A stolen refresh token therefore stops working for both parties as soon as either uses it after the other.
- A token presented through the other client's channel is treated as unknown.

Sign-out revokes the session with the reason `SignOut` and always answers `204`. Access tokens already issued stay valid until they expire, at most 15 minutes.

### The refresh cookie

| Attribute | Value |
|---|---|
| Name and path | `fs_refresh`, `Path=/api/auth` |
| Flags | `HttpOnly`, `SameSite=Strict`, `Secure` (in Development over plain http, `Secure` follows the request scheme) |
| Lifetime | `Max-Age` up to the session's idle expiry, never past its absolute expiry; no `Domain`, no `Expires` |
| Cleared | With an empty value, `Max-Age=0` and the 1970 expiry, on a sign-out or a `401` refresh whose request presented the cookie, and so passed the header check below; a request without the cookie gets no `Set-Cookie` |

Refresh and sign-out with the cookie also require the header `X-FieldService-Csrf: 1`. A request with the cookie and without the header gets `403 auth.csrf-required`, and the cookie and its token stay untouched. Together with `SameSite=Strict` and the same-origin deployment, this keeps other sites from using the cookie.

### The dashboard session

- On page load the dashboard restores the session with one refresh, and every navigation waits for it.
- It refreshes about 60 seconds before the access token expires. Refreshes are serialised across browser tabs with the Web Lock `fieldservice.session-refresh`, so two tabs never present the same token.
- A request that gets `401` triggers one shared refresh and one retry.
- Sign-out in one tab signs out the others through the BroadcastChannel `fieldservice.session`.
- If the sign-out request fails twice (no connection or a server error), the dashboard keeps the session, stays on the page and says that sign-out did not finish, so a reload never signs the administrator back in after a sign-out that looked complete. A `204` or a `401` ends the session.
- After sign-in, the dashboard returns only to a path on its own origin: a value that is not a single-slash path, contains control characters, or normalises to another origin falls back to the start page.

### The technician app session

- The refresh token lives only in the platform's secure storage, under the key `fieldservice.refresh-token`. The access token, the signed-in user and the email typed at sign-in live only in memory.
- One refresh runs at a time. A request that gets `401` triggers one shared refresh and one retry. The access token is sent only to the configured API address: a request to any other scheme, host or port carries no `Authorization` header.
- A network failure during a refresh keeps the session, so a technician can keep reading the jobs already loaded while offline. Only a refused refresh (`400` or `401`) ends it.
- A session ends on sign-out, a refused refresh, unreadable secure storage, or a stored session the API rejects at startup. Every one of these clears the stored token, the in-memory session, every loaded job list and job detail, and the sign-in form, and discards any answer still in flight. The next person on a shared device sees nothing of the previous session. The sign-in screen says "Your session ended." after an expiry or a revocation, and nothing after a chosen sign-out.
- When the sign-out request fails, the app still ends the session on the device; the server session stays valid until it expires (see [Accepted limitations](#accepted-limitations)).
- A refresh or restore that finishes after the session has ended or changed writes nothing back, and its rotated token is signed out. A request is retried after a refresh only for the session it started under, so a command never runs under the next technician's session. A sign-in that replaces a live session signs the old one out on the server.
- Release builds require an `https` API address, checked when the app is built and again when it starts. Plain `http` is allowed only in Debug builds, for the Android emulator's loopback address.
- On Android, app backup is off, and the data extraction rules keep the secure-storage preferences and the log files out of cloud backup and device transfer. The app holds only the network permissions. It opens the dialer and the maps app through the system, without a call permission.
- Navigate opens the device's maps app with the job site address. When no maps app can take it, the app opens a Bing Maps search for that address in the browser. Only the address is sent, and only when the technician taps Navigate.

## Authorization

- Every endpoint requires an authenticated user unless it is on a short anonymous list: sign-in, refresh, sign-out and the health endpoints, plus the API document in Development and Testing only. A test reads every mapped endpoint and fails on any endpoint missing from the expected list or without an explicit policy.
- Administrator endpoints require the `Administrator` policy. Job details require `AdministratorOrTechnician`, and a technician sees only the jobs assigned to them: any other job answers `404 job.not-found`, the same answer as a job that does not exist.
- Assigning a job (`PUT /api/jobs/{jobId}/assignment`) and listing technicians (`GET /api/technicians`) require `Administrator`. A technician's job list (`GET /api/technicians/me/jobs`) requires `Technician` and always uses the caller's id from the access token; it takes no technician parameter. After a job is reassigned, the previous technician gets the same `404` as for a job that does not exist.
- An assignment carries the job's current version, so two administrators assigning the same job at once cannot both succeed: the second gets `409 concurrency.conflict`.
- Changing a job's status (`PUT /api/jobs/{jobId}/status`) requires `Technician` and only the assigned technician's own job; an administrator gets `403`, and any other technician the same `404` as for a missing job. Only the allowed changes are accepted: start an assigned job, put a started job on hold, and resume a job on hold. A broken rule answers before a stale version does, re-sending the current status changes nothing, and two simultaneous changes never both succeed. The change event is recorded only after the change is saved.
- A hold reason is required, at most 500 characters, trimmed, and holds no control characters other than line breaks and tabs. It is stored in the job's history, shown as plain text in both apps, and never written to a log.
- Reading an inspection (`GET /api/jobs/{jobId}/inspection`) requires `AdministratorOrTechnician`, and a technician reads only their own jobs. Saving it (`PUT /api/jobs/{jobId}/inspection`) and submitting the job (`POST /api/jobs/{jobId}/submission`) require `Technician` and only the assigned technician's own job; an administrator gets `403`, and any other technician the same `404` as for a missing job, before any job-dependent check runs.
- A submitted inspection cannot change. The API refuses any changed answer with `409 inspection.locked`, re-sending the saved answers changes nothing, and a database trigger refuses any update or delete of a submitted inspection's rows, so the rule holds even against a direct database write by the application role. Submission completes the job, locks the inspection and writes the history entry in one transaction; two simultaneous submissions or a save racing a submission never produce two completions or a changed checklist.
- Inspection notes, readings and technician notes are stored with the inspection, shown as plain text, and never written to a log; a test runs a whole inspection flow with every log level captured and fails if any of them appears.
- The dashboard summary (`GET /api/dashboard/summary`), cancelling a job (`POST /api/jobs/{jobId}/cancellation`) and client error reports (`POST /api/client-errors`) require `Administrator`. A technician gets `403` before any validation or job lookup, so the answer never tells whether a job exists. The summary returns only what an administrator already sees on the job pages: no email address, phone number, access note or contact.
- A cancellation carries the job's version and works from Unassigned, Assigned, In progress and On hold; a completed job answers `409 job.invalid-transition`, and an already cancelled job `200` with nothing written. The reason is required, at most 500 characters, trimmed, holds no control characters other than line breaks and tabs, is stored in the job's history, shown as plain text in both apps, and never written to a log. A cancelled job's draft inspection stays unsubmitted and can no longer be saved.
- Endpoint parameters and responses use dedicated request and response records, never database entities, and a test enforces it.
- A request to a route that does not exist gets `404`, and a wrong method gets `405`, for anonymous callers too.

## Real-time updates

The notification service relays job changes from the API to signed-in administrators. It has no database connection and holds no credential the API accepts, so a compromised service can send at most false hints, which dashboards answer by refetching real data from the API at a capped rate. The wire formats are in [contracts/events.md](contracts/events.md).

### Socket tickets

| Property | Value |
|---|---|
| Endpoint | `POST /api/auth/socket-tickets`, `Administrator` policy, 30 requests a minute per user |
| Session check | The access token's session must exist, belong to the caller, be unrevoked and unexpired, and belong to an active user; otherwise `401 auth.session-expired` |
| Format | JSON Web Token, HS256, signed with `Auth:SocketTicketKey`; issuer `fieldservice-api`, audience `fieldservice-notifications` |
| Lifetime | 60 seconds; each ticket id is accepted once |
| Transport | Only the `auth` member of the Socket.IO handshake, never a URL, header or cookie; the response is `no-store` |
| Accepted by | The notification service only; the API rejects a ticket sent as a bearer token |

The notification service checks the browser's `Origin` against `SOCKET_ALLOWED_ORIGINS` before the handshake, then verifies the ticket: HS256 only, the issuer and audience above, at most 60 seconds old with 5 seconds of clock tolerance, every claim present, and the `Administrator` role. It remembers verified ticket ids for 120 seconds, at most 10,000 of them, and refuses the handshake when none can be evicted. Sockets use the WebSocket transport only, frames are limited to 4 KB, clients cannot send events, and the service disconnects every socket after `SOCKET_SESSION_MAX_SECONDS` (900 by default), so the session check runs again on the reconnect.

### Job events

The API queues an event only after a change commits and posts it to the notification service with a Standard Webhooks signature: HMAC-SHA256 over the event id, the timestamp and the exact body. The service checks, in order, the route and method, the media type, a body of at most 16 KB, a timestamp within the tolerance (300 seconds by default), the signature against each configured secret in constant time, and a strict schema whose id must match the signed id. It remembers accepted ids for twice the tolerance plus 60 seconds, so while it keeps running a replayed request fails either the timestamp or the id check, and it records an id only after the signature and schema pass, so unsigned traffic cannot fill the list. Events carry identifiers and statuses only, never names, addresses or notes, and the `/internal` path is never proxied to browsers.

The dashboard treats every event as a hint: it never copies data from an event, refetches the affected job at most once a second, and makes at most 60 event-driven requests a minute before falling back to one full refetch a minute ([ADR 0010](decisions/0010-refetch-on-event-and-reconnect.md)).

## Request controls

| Control | Rule |
|---|---|
| Rate limits | Fixed 60-second windows: sign-in 10 and refresh 30 per client address; socket tickets 30 and client error reports 10 per user; every `/api` request, these included, 300 per user or 60 per anonymous address. Requests with a missing or invalid token count against the address limit. A rejected request gets `429` with `Retry-After`. Health checks and the API document are not limited. |
| Body size | 8 KB on client error reports, 16 KB on sign-in, refresh, sign-out and socket tickets, 1 MB on every other request; larger bodies get `413`. |
| Lists | An inspection save takes 1 to 100 answers and at most 10 readings per answer. A longer list is refused while the body is read, with one error, so an oversized request never produces a large response. |
| Numbers | Reading values lie between −99,999,999.99 and 99,999,999.99 with at most two decimals; a third decimal is refused, never rounded by the API. Numbers with more than 28 significant digits are rounded by the JSON parser before the check. |
| Media type | Request bodies must be JSON; any other `Content-Type` gets `415`, even with an empty body. Refresh and sign-out also accept a request with no body and no `Content-Type`. |
| JSON | Duplicate members, numbers sent as strings, integer enumeration values and nulls for required members are rejected with `400`. |
| Strings | Single-line fields reject control characters; multi-line fields allow only line feed, carriage return and tab. Passwords and refresh tokens are used exactly as sent. |
| Query strings | Every query parameter is checked before binding; a bad value is a `400` keyed by the parameter name. The dashboard summary's `from` and `to` are RFC 3339 instants with `Z` or a numeric offset, `to` after `from`, at most 31 days apart; they reach the database as UTC. |
| Errors | Problem Details (`application/problem+json`) with a stable `code` and the correlation id; no stack trace, exception text or query string outside Development. |
| Cross-origin access | Only the configured dashboard origins; `*` is refused at startup. |
| Forwarded headers | Trusted only from loopback in Development and Testing, and elsewhere only from the configured proxy addresses, which must name at least one single host; in the container deployment that is nginx's fixed address alone. No trusted network range is accepted outside Development and Testing, and `X-Forwarded-Host` is never applied. |
| Host header | In Production, `AllowedHosts` must list the public host names; `*` or an empty value stops the API at startup. |

## Headers and content security policy

The API writes these headers on every response, error responses included:

| Header | Value |
|---|---|
| `X-Content-Type-Options` | `nosniff` |
| `Referrer-Policy` | `no-referrer` |
| `Content-Security-Policy` (on `/api` and `/health`) | `default-src 'none'; frame-ancestors 'none'` |
| `Cache-Control` (on `/api`) | `no-store` |
| `X-Correlation-ID` | The request's correlation id |
| `Strict-Transport-Security` | `max-age=31536000` on https requests outside Development, except for loopback hosts |

The API sends no `Server` header.

The dashboard is served with this policy:

```text
default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'
```

It loads no third-party origin: fonts are self-hosted, styles are static rules in the built stylesheet, and templates carry no inline `style` attributes. Values computed at run time go through the CSS Object Model, which this policy allows. The built application is tested under this policy before each release.

## Secrets and startup validation

| Secret | Setting | Rules |
|---|---|---|
| Access-token key | `Auth:AccessTokenKey` | Standard base64 of at least 32 bytes |
| Socket-ticket key | `Auth:SocketTicketKey` | As above, and different from the access-token key |
| Webhook secret | `Notifications:WebhookSecret` | `whsec_` followed by base64 of at least 32 bytes; different from both keys |
| Database connection | `ConnectionStrings:FieldService` | Present; detailed database errors stay off |
| Demo password | `Seed:DemoPassword` | At least 12 characters; demo data is refused in Production |
| Notification service webhook secrets | `WEBHOOK_SECRETS` | One or two `whsec_` secrets of at least 32 bytes, newest first; none may decode to the same bytes as the ticket key |
| Notification service ticket key | `SOCKET_TICKET_KEY` | The API's socket-ticket key, standard base64 of at least 32 bytes |

- Locally, every secret is created by `infrastructure/scripts/create-dev-env.mjs` with a cryptographic random generator and stored outside the repository. Outside Development, the applications read secrets from environment variables only.
- No secret appears in a settings file, a build variable, a container image or a log.
- The API does not use ASP.NET Core Data Protection: no cookie or token depends on it, the service is not registered, and so no key ring is created or stored. A feature that needs it must persist its keys to a volume first.
- The API and the notification service stop at startup when a key is missing, too short, patterned (all bytes equal, or a constant step), equal to another key, or equal to a published test value. The message names the setting and the rule, never the value. [configuration.md](configuration.md) lists every rule.
- The contract test vectors described in [contracts/README.md](contracts/README.md) hold published test values so that every implementation can be checked against the same bytes. **These values must never be configured in any environment**; the applications refuse them at startup.

## Logging

Logs carry identifiers, never passwords, tokens, cookies, `Authorization` headers, request bodies, query strings, email addresses, names, phone numbers or addresses. Apart from unhandled exceptions, which are logged with their message and stack trace, the one API entry that carries free text from outside the services is the client error report, scrubbed as described in [Client error reports](#client-error-reports). Security events (sign-in, refresh, reuse detection, sign-out, refused authorization and rate-limit rejections) are named and listed with their properties in [logging.md](logging.md). A test signs in, refreshes, issues a socket ticket, creates and changes jobs, sends their signed events and signs out with known sentinel values, and fails if any of them, a ticket, a key, a webhook signature or an event body reaches a log event or a log file. The API's outgoing HTTP client writes no request logs of its own, and its retry and circuit-breaker events carry no headers or bodies.

The notification service logs event ids, job ids, socket ids, user ids, ticket ids and correlation ids, never tickets, signatures, secrets, request bodies or the refused `Origin` value. An error is logged with its type, message, stack trace and code only, and a failure value that is not an error is logged by its type alone, so data attached to a failure never reaches a log. A test of each service scans its logs for sentinel values.

The database container writes only the error line of a failed statement, without the statement's text or the failing row's values, so names, addresses and role passwords stay out of the database log. Production database servers should use the same settings ([deployment.md](deployment.md)).

The technician app writes one entry per API call, with the method, the route template without its query, the status, the duration and the correlation id. The HTTP client's own request logs are turned off, and a test of the production client setup fails if they come back. Data records print only their type and id.

## Client error reports

The dashboard sends important browser errors to the API, which writes them to its log; browser code never writes log files. Two layers keep personal data and secrets out of the log.

**In the dashboard**, before a report is sent:

- Only a signed-in administrator's errors are reported, at most 10 a minute, each error once; a refused report is never retried.
- A failed API call is described by its status and error code only, never by the rest of the response. No form value or other text the user typed goes into a report.
- Quoted text in the message and stack becomes `[redacted]`, and the stack is reduced to its frames. A response that cannot be read as JSON, and a thrown value that is not an error, are reported with a fixed message and no stack.
- Token-shaped values, email addresses and query strings are removed, and the route is sent as its path only.

**In the API**:

- `POST /api/client-errors` requires `Administrator`, accepts 10 reports a minute per user and 8 KB per report, and reads only the members of its fixed schema; unknown members are ignored and never logged.
- Before logging, the message, source, stack, route, correlation id and application version are scrubbed again: JSON Web Token shapes, bearer values, webhook secrets, email addresses and refresh-token shapes become `[redacted]`, and query strings `?[redacted]`.
- The report is written as one Warning, `client-error.reported`, with `Source` `web`, the user id and the request's correlation id, and the answer is `202` with no body: nothing is echoed. The entry is described in [logging.md](logging.md).

A test sends reports carrying sentinel tokens, secrets, email addresses and query strings in every member, with every log level captured, and fails if any of them reaches a log event or file.

## Operator procedures

### Revoking a user's sessions

Use this when a device is lost or a refresh token may have been stolen. Deactivating the user alone is not enough: sessions are otherwise revoked only when they next refresh, and re-enabling the account would revive any session that did not refresh in the meantime.

1. Connected as the migrator role or a database administrator, deactivate the user and revoke every live session in one transaction:

   ```sql
   \set user_id '<user id>'
   BEGIN;
   UPDATE fieldservice.users SET is_active = false, updated_at = now() WHERE id = :'user_id';
   UPDATE fieldservice.user_sessions SET revoked_at = now(), revoked_reason = 'Operator' WHERE user_id = :'user_id' AND revoked_at IS NULL;
   COMMIT;
   ```

2. Wait at least 15 minutes, the access-token lifetime, so that every access token issued before the revocation has expired.
3. Set `is_active` back to true. The user signs in again on a trusted device. Revoked sessions stay revoked.

Record each use with the user id, the time and the reason.

### A lost or stolen technician device

1. Revoke the technician's sessions with the procedure above. The account stays inactive until step 3 of that procedure.
2. The refresh token on the device stops working at once. The app finds out at its next refresh or start, clears its stored session, its loaded jobs and its sign-in form, and shows "Your session ended.". Until then, it can show only the jobs it had already loaded, and its access token expires within 15 minutes.
3. Re-enable the account when the technician has a trusted device, and have them sign in there.

The device's screen lock is the control that protects the jobs already on screen. Secure storage is tied to the device and is excluded from backup and device transfer, so the refresh token cannot be restored onto another device.

### Rotating secrets

| Secret | Procedure | Effect on clients |
|---|---|---|
| Access-token key | Replace the value and restart the API | Current access tokens fail once; clients refresh silently |
| Socket-ticket key | Change it in the API and the notification service together | Real-time connections fail for about a minute, then recover |
| Webhook secret | Add the new secret to the front of the notification service's `WEBHOOK_SECRETS` and restart it, then change the API's `Notifications:WebhookSecret` and restart the API, then remove the old secret from the list | None; events signed with either secret are accepted during the change |
| Database passwords | `ALTER ROLE … PASSWORD`, update the stored connection string, restart | A brief reconnect |
| Demo password | Re-create the local secrets, then reset the database, because the seeder never rewrites existing password hashes | Local demo only |

## Review results

Each feature set is reviewed against these controls before it is merged.

| Date | Scope | Result | Follow-up |
|---|---|---|---|
| 2026-10-20 | Repository setup: configuration, database roles, scaffolds, build pipeline | No high or medium finding | Health endpoints limited to GET and HEAD, the production host list and the forwarded-network rule were added with sign-in and jobs |
| 2026-10-21 | Administrator sign-in, sessions, job creation and the job list | No high or medium finding | A refresh or sign-out now clears the refresh cookie only when the request presented it, as defence in depth against forced sign-out; a whitespace-only password is an ordinary failed sign-in. Requiring a configured proxy address outside development and testing was added with the container deployment: the API now refuses to start without one. |
| 2026-10-22 | Job assignment, the technician list, the technician app's sign-in, session, job list and job details | No high finding; one medium finding fixed before merge | The technician app kept the previous technician's loaded jobs after a session ended by expiry or revocation; every way a session ends now clears all per-user state, the sign-in form included. Also added: the access token is sent only to the configured API address, a refresh that finishes after sign-out writes nothing back, log files are excluded from backup and device transfer, and the assignment check reads only the columns it needs. |
| 2026-10-22 | Technician status changes: start, hold and resume, the status history in the dashboard, and the technician app's Update status sheet | No high, medium or low finding | Retries stay with the session a request started under, every way a session ends goes through one path, abandoned sessions are signed out on the server, and hold reasons are kept out of logs and printed requests. |
| 2026-10-23 | Real-time updates: socket tickets, signed job events, the notification service's relay and sockets, and the dashboard's live states | No high or medium finding; one low finding resolved before merge | The event contract now states the reconnect timing the design specifies, and the dashboard pins those values with a test. Also added: the notification service refuses a webhook secret equal to its ticket key, logs an error with its type, message, stack and code only and any other failure value by its type alone, and exits with code 1 when it cannot listen; end-to-end tests fail if a socket handshake URL carries anything but its two protocol keys. The accepted limitations below gained the real-time items. |
| 2026-10-24 | Inspections and submission: the checklist save and submission, the lock after submission, the technician app's checklist and review screens, and the dashboard's inspection report | No high finding; one medium finding fixed before merge | A save with an oversized list of empty answers produced a very large error response; lists are now limited while the body is read, with one error. Also fixed: database refusals and lock timeouts that the database driver wrapped were answered as server errors instead of the documented conflicts. A test now runs a whole inspection flow with every log level captured and fails if a note, reading or technician note appears. |
| 2026-10-24 | The dashboard, the technicians page, job cancellation and client error reports | No high or medium finding; three low findings fixed before merge | The API's scrubber missed refresh tokens that start with a hyphen, and the dashboard's quoted-text redaction missed values holding an apostrophe or a line break; both are fixed with tests. A race of an inspection save against a cancellation now has a test. The dashboard's error reporter never sends response data, form values or typed text, and the remaining free-text risk is listed below. |
| 2026-10-25 | Containers, the reverse proxy, the end-to-end tests against the containers and the pipeline job | No high or medium finding; five low findings resolved before merge | The proxy logs a correlation id only when it is well formed, and the containers' dynamic address range keeps clear of the proxy's fixed address. An end-to-end report or trace that holds a secret is deleted and fails the run, and a run without that scan fails. The pipeline's diagnostics artifact holds no environment file or database log and is kept for three days. |
| 2026-10-27 | Final review of the whole repository, including the technician app's Windows, keyboard and screen-reader changes | No high or medium finding; three low findings fixed before merge and one accepted | A failed sign-out in the dashboard keeps the session and says so; the database log no longer holds statement text or row values; key and secret files are ignored. The technician app's sign-out after a failed request is listed below. A vulnerability scan of every container image found no reachable critical or high issue; the notification service image no longer ships the package manager it never uses, and the web image drops `curl` and takes the distribution's fixed `brotli` and `zlib` packages, so both now scan clean. The database base image has unfixed issues in tools and libraries the database does not use; it is rescanned when its base image changes. |

## Accepted limitations

Other known limits, outside security, are listed in [limitations.md](limitations.md).

- Revocation takes effect at the next refresh; an access token already issued stays valid for up to 15 minutes.
- An open real-time connection outlives a revoked session until its session cap, 15 minutes by default, and a socket ticket requested at the same moment as a sign-out can still be issued. The connection receives only job identifiers and statuses.
- One administrator can hold many real-time connections at once; their number is bounded by the ticket rate limit and the session cap.
- Job events are delivered at most once and are lost if the API stops with events still queued. A dashboard that misses an event shows the previous data until its next event, navigation or reconnect.
- The notification service keeps the ids of accepted events in memory, so a restart clears them. A signed event captured on the internal network and sent again within the timestamp tolerance after a restart is broadcast once more. Dashboards answer it by refetching real data under the usual limits.
- Rate limits are per process and per window. They slow guessing but do not lock an account, and a distributed attempt from many addresses is outside their scope.
- There is no self-registration, password reset or multi-factor authentication; accounts come from the seeded data set in local environments.
- Administrators are not separated from each other: every administrator can see and change every job.
- A client error report can still carry personal data that a browser prints without quotation marks, or a name, phone number or address in free text: no pattern finds those reliably, and a phone pattern would also remove identifiers and dates. Reports come only from signed-in administrators, who can see the same data, at most 10 a minute each. They are written to the API's local log files under their retention and to its console output, which the host or container platform keeps under its own rules.
- The technician app keeps the jobs it has loaded in memory while it runs, so a technician can read them offline. On an unlocked device, anyone holding it can read them until the session ends.
- When no maps app is installed, Navigate sends the job site address to a public map search in the browser, and the address stays in the browser's history.
- When the technician app's sign-out request fails (no connection, a timeout or a server error), the app ends the session on the device, but the server session stays valid until it expires: after 7 days without use, and 30 days at most. Only someone who already holds that session's refresh token could use it, and an administrator can end it at once ([Revoking a user's sessions](#revoking-a-users-sessions)).
