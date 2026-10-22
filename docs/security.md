# Security

This document describes how FieldService protects its users and data: the security objectives, the trust boundaries, the authentication and session model, the controls at each boundary, the operator procedures and the accepted limitations. It covers what the applications do today and grows with each feature. The decision behind the session model is [ADR 0007](decisions/0007-token-authentication-with-rotating-refresh-tokens.md).

## Contents

- [Objectives](#objectives)
- [Trust boundaries](#trust-boundaries)
- [Authentication and sessions](#authentication-and-sessions)
- [Authorization](#authorization)
- [Request controls](#request-controls)
- [Headers and content security policy](#headers-and-content-security-policy)
- [Secrets and startup validation](#secrets-and-startup-validation)
- [Logging](#logging)
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
- After sign-in, the dashboard returns only to a path on its own origin: a value that is not a single-slash path, contains control characters, or normalises to another origin falls back to the start page.

### The technician app session

- The refresh token lives only in the platform's secure storage, under the key `fieldservice.refresh-token`. The access token, the signed-in user and the email typed at sign-in live only in memory.
- One refresh runs at a time. A request that gets `401` triggers one shared refresh and one retry. The access token is sent only to the configured API address: a request to any other scheme, host or port carries no `Authorization` header.
- A network failure during a refresh keeps the session, so a technician can keep reading the jobs already loaded while offline. Only a refused refresh (`400` or `401`) ends it.
- A session ends on sign-out, a refused refresh, unreadable secure storage, or a stored session the API rejects at startup. Every one of these clears the stored token, the in-memory session, every loaded job list and job detail, and the sign-in form, and discards any answer still in flight. The next person on a shared device sees nothing of the previous session. The sign-in screen says "Your session ended." after an expiry or a revocation, and nothing after a chosen sign-out.
- A refresh or restore that finishes after the session has ended or changed writes nothing back.
- Release builds require an `https` API address, checked when the app is built and again when it starts. Plain `http` is allowed only in Debug builds, for the Android emulator's loopback address.
- On Android, app backup is off, and the data extraction rules keep the secure-storage preferences and the log files out of cloud backup and device transfer. The app holds only the network permissions. It opens the dialer and the maps app through the system, without a call permission.
- Navigate opens the device's maps app with the job site address. When no maps app can take it, the app opens a Bing Maps search for that address in the browser. Only the address is sent, and only when the technician taps Navigate.

## Authorization

- Every endpoint requires an authenticated user unless it is on a short anonymous list: sign-in, refresh, sign-out and the health endpoints, plus the API document in Development and Testing only. A test reads every mapped endpoint and fails on any endpoint missing from the expected list or without an explicit policy.
- Administrator endpoints require the `Administrator` policy. Job details require `AdministratorOrTechnician`, and a technician sees only the jobs assigned to them: any other job answers `404 job.not-found`, the same answer as a job that does not exist.
- Assigning a job (`PUT /api/jobs/{jobId}/assignment`) and listing technicians (`GET /api/technicians`) require `Administrator`. A technician's job list (`GET /api/technicians/me/jobs`) requires `Technician` and always uses the caller's id from the access token; it takes no technician parameter. After a job is reassigned, the previous technician gets the same `404` as for a job that does not exist.
- An assignment carries the job's current version, so two administrators assigning the same job at once cannot both succeed: the second gets `409 concurrency.conflict`.
- Endpoint parameters and responses use dedicated request and response records, never database entities, and a test enforces it.
- A request to a route that does not exist gets `404`, and a wrong method gets `405`, for anonymous callers too.

## Request controls

| Control | Rule |
|---|---|
| Rate limits | Fixed 60-second windows: sign-in 10 and refresh 30 per client address; every other `/api` request 300 per user or 60 per anonymous address. Requests with a missing or invalid token count against the address limit. A rejected request gets `429` with `Retry-After`. Health checks and the API document are not limited. |
| Body size | 16 KB on sign-in, refresh and sign-out; larger bodies get `413`. |
| Media type | Request bodies must be JSON; any other `Content-Type` gets `415`, even with an empty body. Refresh and sign-out also accept a request with no body and no `Content-Type`. |
| JSON | Duplicate members, numbers sent as strings, integer enumeration values and nulls for required members are rejected with `400`. |
| Strings | Single-line fields reject control characters; multi-line fields allow only line feed, carriage return and tab. Passwords and refresh tokens are used exactly as sent. |
| Query strings | Every query parameter is checked before binding; a bad value is a `400` keyed by the parameter name. |
| Errors | Problem Details (`application/problem+json`) with a stable `code` and the correlation id; no stack trace, exception text or query string outside Development. |
| Cross-origin access | Only the configured dashboard origins; `*` is refused at startup. |
| Forwarded headers | Trusted only from loopback in Development and from configured proxies elsewhere; `X-Forwarded-Host` is never applied. |
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

- Locally, every secret is created by `infrastructure/scripts/create-dev-env.mjs` with a cryptographic random generator and stored outside the repository. Outside Development, the applications read secrets from environment variables only.
- No secret appears in a settings file, a build variable, a container image or a log.
- The API stops at startup when a key is missing, too short, patterned (all bytes equal, or a constant step), equal to another key, or equal to a published test value. The message names the setting and the rule, never the value. [configuration.md](configuration.md) lists every rule.
- The contract test vectors described in [contracts/README.md](contracts/README.md) hold published test values so that every implementation can be checked against the same bytes. **These values must never be configured in any environment**; the applications refuse them at startup.

## Logging

Logs carry identifiers, never passwords, tokens, cookies, `Authorization` headers, request bodies, query strings, email addresses, names, phone numbers or addresses. Security events (sign-in, refresh, reuse detection, sign-out, refused authorization and rate-limit rejections) are named and listed with their properties in [logging.md](logging.md). A test signs in, refreshes, creates a job and signs out with known sentinel values and fails if any of them reaches a log event or a log file.

The technician app writes one entry per API call, with the method, the route template without its query, the status, the duration and the correlation id. The HTTP client's own request logs are turned off, and a test of the production client setup fails if they come back. Data records print only their type and id.

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
| Database passwords | `ALTER ROLE … PASSWORD`, update the stored connection string, restart | A brief reconnect |
| Demo password | Re-create the local secrets, then reset the database, because the seeder never rewrites existing password hashes | Local demo only |

## Review results

Each feature set is reviewed against these controls before it is merged.

| Date | Scope | Result | Follow-up |
|---|---|---|---|
| 2026-10-20 | Repository setup: configuration, database roles, scaffolds, build pipeline | No high or medium finding | Health endpoints limited to GET and HEAD, the production host list and the forwarded-network rule were added with sign-in and jobs |
| 2026-10-21 | Administrator sign-in, sessions, job creation and the job list | No high or medium finding | A refresh or sign-out now clears the refresh cookie only when the request presented it, as defence in depth against forced sign-out; a whitespace-only password is an ordinary failed sign-in. Requiring a configured proxy address in production is scheduled with the container deployment. |
| 2026-10-22 | Job assignment, the technician list, the technician app's sign-in, session, job list and job details | No high finding; one medium finding fixed before merge | The technician app kept the previous technician's loaded jobs after a session ended by expiry or revocation; every way a session ends now clears all per-user state, the sign-in form included. Also added: the access token is sent only to the configured API address, a refresh that finishes after sign-out writes nothing back, log files are excluded from backup and device transfer, and the assignment check reads only the columns it needs. |

## Accepted limitations

- Revocation takes effect at the next refresh; an access token already issued stays valid for up to 15 minutes.
- Rate limits are per process and per window. They slow guessing but do not lock an account, and a distributed attempt from many addresses is outside their scope.
- There is no self-registration, password reset or multi-factor authentication; accounts come from the seeded data set in local environments.
- Administrators are not separated from each other: every administrator can see and change every job.
- The technician app keeps the jobs it has loaded in memory while it runs, so a technician can read them offline. On an unlocked device, anyone holding it can read them until the session ends.
- When no maps app is installed, Navigate sends the job site address to a public map search in the browser, and the address stays in the browser's history.
