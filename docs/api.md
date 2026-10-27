# API reference

This document describes the FieldService REST API: its conventions, its error model and every endpoint it serves today. The machine-readable contract is the OpenAPI 3.1 document `docs/contracts/openapi.v1.json`; how it is produced, tested and changed is described in [contracts/README.md](contracts/README.md). When this document and the snapshot disagree, the snapshot wins and this document is corrected.

The examples use the demo data that the API seeds in Development (see [database.md](database.md)): Monday 19 October 2026, 10:42 AM in UTC−4, which is 14:42 UTC. Job, user and history ids are illustrative. Access tokens and socket tickets are shortened to `eyJhbGciOiJIUzI1NiIs…`; passwords and refresh tokens appear as placeholders such as `<password>`.

## Contents

- [Conventions](#conventions)
- [Errors](#errors)
- [Shared response objects](#shared-response-objects)
- [Endpoint index](#endpoint-index)
- [Auth](#auth)
- [Dashboard](#dashboard)
- [Technicians](#technicians)
- [Jobs](#jobs)
- [Inspections](#inspections)
- [Checklist templates](#checklist-templates)
- [Diagnostics](#diagnostics)
- [Infrastructure endpoints](#infrastructure-endpoints)

## Conventions

### Paths, versioning and methods

| Item | Rule |
|---|---|
| Base path | `/api`. Every business endpoint lives under it. This is version 1 of the contract. |
| Other paths | `/health/live`, `/health/ready`, and `/openapi/v1.json` in the Development and Testing environments only |
| Versioning | Changes under `/api` are additive only. A breaking change gets a decision record and a parallel `/api/v2` route group. |
| Methods | GET reads. POST creates a resource (`/api/jobs`) or performs an action (`/submission`, `/cancellation`, `/api/client-errors` and the auth actions). PUT replaces the state of a sub-resource (`/assignment`, `/status`, `/inspection`). There is no PATCH and no DELETE. |
| Route ids | `{jobId}` must be a GUID. Any other value matches no route and returns 404 `resource.not-found`. |
| Unknown input | Unknown query parameters and unknown JSON members are ignored. |

### JSON

| Rule | Value |
|---|---|
| Request media type | `Content-Type: application/json`, with an optional `charset=utf-8`, on every request that has a body. Anything else is 415. On an endpoint with a body, a request that sends another `Content-Type` gets 415 even when its body is empty. |
| Response media type | `application/json; charset=utf-8` for success bodies, `application/problem+json` for errors |
| Property names | camelCase |
| Enumerations | Strings with the PascalCase member name, for example `InProgress`. Integer values are rejected, in bodies and in query strings. |
| Numbers | JSON numbers only. A number sent as a string is a field error. |
| Response members | Every member of a response record is always present. A missing value is `null`; an empty collection is `[]`. |
| Request nulls | `null` for a required member is a field error. |
| Duplicate members | A JSON object with the same member twice is a 400. |
| Maximum depth | 32 |

### Identifiers, numbers and enumerations

- **Ids** are UUID strings in lower-case canonical form, for example `01a15415-3b40-79a4-859f-2cf627f64d02`. Clients treat them as opaque. A technician's id is their user id.
- **Job numbers** are integers starting at 1001. Clients display them as `JOB-` followed by the number, for example `JOB-1042`.
- **`version`** is an unsigned 32-bit integer. It is opaque: a client sends back the last value it read and never compares or increments it.
- **Counts** are 32-bit integers of 0 or more.
- **Reading values** are JSON numbers from −99999999.99 to 99999999.99 with at most two decimal places, the range of the database column `numeric(10,2)`. The OpenAPI document types them as `number` with `format: double`, because that is how it describes a decimal; the two-decimal rule is the API's. A value with a third decimal, such as `54.125`, is a field error and is never rounded by the API. A number with more than 28 significant digits is rounded by the JSON parser before the check, so `54.0000000000000000000000000001` arrives as `54`; a number too large to read, such as `1e29`, is a field error. Trailing zeros carry no meaning: `54`, `54.0` and `54.00` are the same value, and responses may write `54.00`.

| Enumeration | Values | Notes |
|---|---|---|
| JobStatus | `Unassigned`, `Assigned`, `InProgress`, `OnHold`, `Completed`, `Cancelled` | `Completed` and `Cancelled` are terminal. |
| JobPriority | `Low`, `Normal`, `High`, `Urgent` | |
| UserRole | `Administrator`, `Technician` | The interface labels Administrator "Dispatcher". |
| SessionClient | `Web`, `Mobile` | The `client` member of SignIn |
| Trade | `Hvac`, `Electrical`, `Plumbing`, `FireSafety` | Interface labels: HVAC, Electrical, Plumbing, Fire safety |
| JobHistoryAction | `Created`, `Assigned`, `Started`, `PutOnHold`, `Resumed`, `Completed`, `Cancelled` | |
| InspectionResult | `Pass`, `Fail`, `NotApplicable` | Interface label for NotApplicable: N/A |

Clients accept enumeration values they do not know and render them neutrally. Adding a value is still a contract change.

### Instants

| Rule | Value |
|---|---|
| Wire format | RFC 3339 in UTC with exactly three fractional digits and `Z`, for example `2026-10-19T14:42:00.000Z` (the format of JavaScript's `Date.prototype.toISOString()`) |
| Precision | Milliseconds. The API truncates every instant it stores to whole milliseconds. |
| Request values | RFC 3339 with `Z` or a numeric offset, for example `2026-10-20T09:00:00-04:00`, with 0 to 7 fractional digits, in a body member or a query parameter. A value without an offset is a field error. The API converts it to UTC and truncates it to milliseconds. |
| Time zones | The API has no time zone. All display formatting is done by the clients. |

### Strings

- Length limits are counted in UTF-16 code units and match the database columns.
- The API trims leading and trailing white space from request strings before storing them, except `password` and `refreshToken`, which are used exactly as sent.
- An optional string that is empty after trimming is stored as `null`. A required string must contain a character that is not white space, except `password`, which may be white space only and is then checked like any other password.
- **Control characters.** Single-line fields reject U+0000 to U+001F and U+007F to U+009F. Multi-line fields (`description`, `siteAccessNotes`, the status `note`, the cancellation `reason`, an inspection answer's `note`, `technicianNotes`, and a client error's `message` and `stack`) allow only line feed, carriage return and tab among them. Either violation is a field error keyed by the field.
- Responses return strings as stored.

### Collections, paging, sorting and filtering

- Collections are wrapped in a named record: `{ "items": [ … ] }`.
- Only GetJobs pages: `{ items, page, pageSize, totalCount, statusCounts }`. `page` is 1 to 10000 (default 1) and `pageSize` 1 to 100 (default 25). `totalCount` counts every item that matches the filters. A page past the end returns `items: []` with the real `totalCount`.
- Sorting is fixed per endpoint, and every order ends in a unique key, so pages are stable.
- Filters combine with AND. An omitted filter does not restrict.

| Collection | Order |
|---|---|
| GetJobs `items` | `updatedAt` descending, then `id` descending |
| GetAssignedJobs `items`, `view=active` | `scheduledStart` ascending, then `number` ascending |
| GetAssignedJobs `items`, `view=completed` | `updatedAt` descending, then `id` descending; at most 50 items |
| GetTechnicians `items` | `displayName` ascending, then `id` ascending |
| GetChecklistTemplates `items` | `name` ascending |
| `JobDetailsResponse.history` | `occurredAt` ascending; among equal instants a `Created` entry comes first; then `id` |
| GetDashboardSummary `needsTechnician` | `scheduledStart` ascending, then `number` ascending; at most 10 items |
| GetDashboardSummary `recentActivity` | `occurredAt` descending; among equal instants a `Created` entry comes last; then `id` descending; at most 6 items |
| `InspectionResponse.items` and each item's `readingFields` | `position` ascending |
| An answer's `readings` | The `position` of each reading's field, ascending |

The `Created` tie-breaks exist because CreateJob with a technician writes its `Created` and `Assigned` entries in one transaction with the same instant: the job's history lists `Created` first, and the dashboard's newest-first activity lists `Assigned` first.

### Query parameters

- A parameter is sent at most once. A repeated parameter is a field error keyed by its name.
- Enumeration parameters take the exact member name (`status=InProgress`). Integers, other spellings and lists are field errors.
- GUID, integer and enumeration parameters with an empty value (`status=`) are field errors; leave the parameter out instead. An empty `search` means no search.
- GetAssignedJobs' `view` takes exactly `active` or `completed`, in lower case. Any other value, an empty `view=` included, is a field error keyed `view`; leaving it out means `active`.
- Instant parameters (GetDashboardSummary's `from` and `to`) follow the rule of [Instants](#instants): `Z` or a numeric offset, 0 to 7 fractional digits, lower-case `t` and `z` accepted. A `+` in an offset must be sent percent-encoded as `%2B`, because an unencoded `+` in a query string means a space and makes the value invalid. An empty value (`from=`) is a field error.
- Every query error is keyed by the parameter's name, and its message never repeats the value sent.

### Headers

**Request headers**

| Header | Rule |
|---|---|
| `Authorization` | `Bearer {accessToken}` on every `/api` endpoint except SignIn, RefreshSession and SignOut |
| `X-Correlation-ID` | Optional. Accepted when it matches `^[A-Za-z0-9._-]{1,64}$` and occurs once; otherwise the request's W3C trace id (32 lower-case hex characters) is used. Clients send a new UUID for every request. |
| `X-FieldService-Csrf` | Exactly `1`. Required on RefreshSession and SignOut when the `fs_refresh` cookie is the credential; otherwise 403 `auth.csrf-required`. Ignored everywhere else. |
| `Content-Type` | `application/json` on every request with a body |
| `Cookie` | The browser sends `fs_refresh` only to paths under `/api/auth`. |

**Response headers**

| Header | When | Value |
|---|---|---|
| `X-Correlation-ID` | Every response, errors and health included | The effective correlation id |
| `Cache-Control` | Every `/api` response | `no-store` |
| `X-Content-Type-Options` | Every response | `nosniff` |
| `Referrer-Policy` | Every response | `no-referrer` |
| `Content-Security-Policy` | Every `/api` and `/health` response | `default-src 'none'; frame-ancestors 'none'` |
| `Strict-Transport-Security` | https requests outside Development | `max-age=31536000` |
| `Server` | Never | |
| `Location` | CreateJob's 201 | `/api/jobs/{id}`, a relative URI |
| `Retry-After` | 429 | Whole seconds, at least 1 |
| `WWW-Authenticate` | 401 from bearer authentication | `Bearer`, or `Bearer error="invalid_token"` when a token was sent; never an error description |
| `Set-Cookie` | Web sign-in and a successful Web refresh; a Web sign-out, and a Web refresh that fails with `auth.session-expired`, when the request presented the cookie | See below |
| `Allow` | 405 | The methods the route accepts |

### The `fs_refresh` cookie

| Attribute | Value |
|---|---|
| Value | The opaque refresh token: 32 random bytes in base64url without padding, 43 characters. Only its SHA-256 hash is stored. |
| HttpOnly | Always |
| Secure | Always outside Development. In Development it is set only when the request uses https. |
| SameSite | `Strict` |
| Path | `/api/auth` |
| Domain | Not set |
| Max-Age | Seconds until the session's idle expiry, never past its absolute expiry. Web sessions idle out after 60 minutes and end after 12 hours. |
| Expires | Not sent when the cookie is set |
| Clearing | A Web sign-out, and a Web refresh that returns 401 `auth.session-expired`, send an empty value with `Max-Age=0` and `Expires=Thu, 01 Jan 1970 00:00:00 GMT` when the request presented the cookie, which also requires `X-FieldService-Csrf: 1`. A request without the cookie gets no `Set-Cookie`. |
| Untouched | A 403 `auth.csrf-required` leaves the cookie and its token as they were. |

The technician app never uses the cookie: it sends its refresh token in the request body. Mobile sessions idle out after 7 days and end after 30 days.

### Rate limits

Every limit uses a fixed 60-second window and queues nothing; a rejected request gets 429 `rate-limit.exceeded` with `Retry-After`.

| Policy | Applies to | Partition | Limit per window |
|---|---|---|---|
| `sign-in` | SignIn | Client IP address | 10 |
| `refresh` | RefreshSession | Client IP address | 30 |
| `socket-tickets` | IssueSocketTicket | The user | 30 |
| `client-errors` | ReportClientError | The user | 10 |
| Global | Every `/api` request, including those under a named policy and those that end in 401 or 403 | The user when authenticated, otherwise the client IP address | 300 per user, 60 per anonymous address |
| None | `/health/*` and `/openapi/v1.json` | | Not limited |

The limiter runs after authentication and before authorization, so requests with no token or an invalid one count against the per-address limit. A named policy adds its limit on top of the global one; `socket-tickets` and `client-errors` apply only to authenticated callers, and a request without a valid token meets the global per-address limit before it gets 401. A request that a policy counts is counted whatever its outcome, so invalid client error reports (400) use up the caller's 10 as well.

### Request size limits

| Scope | Limit |
|---|---|
| Every endpoint | 1 MB |
| SignIn, RefreshSession, SignOut, IssueSocketTicket | 16 KB |
| ReportClientError | 8 KB (8,192 bytes) |
| SaveInspection | `answers` 1 to 100 entries, `readings` at most 10 per answer |

A larger body returns 413 `request.too-large`. Too many answers or readings is a field error (400), not a 413: the API stops reading the list at the first entry past the limit and returns that one error, keyed `answers` or `answers[i].readings`, without checking the entries.

### CORS

Browsers reach the API from the same origin, through the development proxy or the web server, so CORS is normally unused. For deployments that call it cross-origin, one policy admits only the origins in `Cors:AllowedOrigins` (see [configuration.md](configuration.md)), with methods GET, POST and PUT, the request headers `Authorization`, `Content-Type`, `X-Correlation-ID` and `X-FieldService-Csrf`, the exposed headers `X-Correlation-ID`, `Location` and `Retry-After`, credentials allowed, and a preflight cache of 600 seconds.

### Authentication, policies and ownership

- **Access token.** A JWT signed with HS256, valid for 15 minutes. Claims: `iss` and `aud` (both `fieldservice-api`), `sub` (the user id), `role`, `sid` (the session id), `jti`, `iat`, `nbf` and `exp`. Clients do not parse it; they use `accessTokenExpiresAt`.
- **Policies.** `Administrator`, `Technician` and `AdministratorOrTechnician`. Every endpoint not listed as anonymous requires an authenticated user.
- **Socket tickets.** The dashboard connects to the notification service with a short-lived ticket from IssueSocketTicket, never with its access token. The ticket is signed with a separate key that the API never accepts as a bearer token.
- **Anonymous endpoints.** SignIn, RefreshSession, SignOut, `/health/live`, `/health/ready`, and `/openapi/v1.json` where it is served.
- **Ownership.** A technician reads only the jobs assigned to them, and their inspections. Any other job returns 404 `job.not-found`, the same response as a job that does not exist. GetAssignedJobs always lists the caller's own jobs and accepts no technician id. Administrators read every job and every inspection.

### Concurrency and idempotency

Every job command carries the `version` the client last read. The API checks a command in this order:

1. Authentication and the endpoint's policy: 401 or 403.
2. Request validation: 400.
3. The job, loaded through the ownership rule: 404.
4. Referenced ids such as `technicianId`, `checklistItemId` and `readingFieldId`, and the answer rules of SaveInspection: 400 field errors.
5. **Same state:** when the job already holds the requested state, the API returns 200 with the current job, writes nothing, adds no history entry and queues no event, whatever `version` was sent.
6. Business rules on the current state: 409 with the rule's code.
7. Saving with the client's `version` as the expected one. A stale version, or another write between steps 3 and 7, returns 409 `concurrency.conflict`.

| Command | Same state (200, no write) | 409 codes |
|---|---|---|
| AssignJob | Status `Assigned` with the same technician | `job.not-assignable`, `technician.unavailable`, `concurrency.conflict` |
| UpdateJobStatus | The job's status equals the requested status; the request's `note` is discarded | `job.invalid-transition`, `concurrency.conflict` |
| SubmitJob | The job is `Completed`; the request's `technicianNotes` are discarded | `job.invalid-transition`, `inspection.incomplete`, `concurrency.conflict`; `inspection.locked` in a race (below) |
| CancelJob | The job is `Cancelled`; the request's `reason` is discarded | `job.invalid-transition`, `concurrency.conflict` |
| SaveInspection | Every listed answer equals the saved one: the same result, the same note after trimming and the same reading values (`54` equals `54.00`) | `inspection.locked`, `concurrency.conflict` |
| CreateJob | None; it is not idempotent | `technician.unavailable` |

- Every 409 means: refetch, explain, and let the user act again. When a stale version and a broken rule coincide, the rule's code wins, because rules are checked against the current state first. A missing hold note is also reported before a stale version.
- SaveInspection reports every field error, template membership included, before `inspection.locked`, so a broken answer is a 400 even on a locked inspection.
- Mutations are never retried automatically. An explicit Retry resends the same body, and the same-state check makes that safe after a lost response.
- Two commands that race with the same `version` never both write. One succeeds; the other gets 409 `concurrency.conflict`, or 200 with no write when it reads the job after the winner has already moved it to the requested status (two Start requests, two submissions). Only the success writes history and queues an event.
- **Inspection races.** A submission writes the inspection before the job, so the loser of two SubmitJob calls can get 409 `inspection.locked` instead of `concurrency.conflict`: the database refuses any change to an inspection that another request has just submitted. A SaveInspection that races a SubmitJob either lands first, and the submission then gets 409 `concurrency.conflict`, or gets 409 `inspection.locked` or `concurrency.conflict` itself. Two first saves of the same inspection give one 200 and one 409 `concurrency.conflict`. A submitted inspection never changes afterwards. Every case is handled the same way: refetch.
- **Cancellation races.** A CancelJob that races an AssignJob, an UpdateJobStatus, a SubmitJob, a SaveInspection or another CancelJob sent with the same `version` never writes beside it: one request succeeds and the other gets 409. While both requests read the job before either saves, the loser always gets `concurrency.conflict`. A loser that reads the job after the winner has saved meets the new state instead: AssignJob then gets `job.not-assignable`, UpdateJobStatus and SubmitJob get `job.invalid-transition`, SaveInspection gets `inspection.locked` (or 200 with no write when it resends the saved answers), a CancelJob after a submission gets `job.invalid-transition`, and a second CancelJob gets 200 with no write. A CancelJob that reads the job after an assignment, a start, a hold, a resume or a save still passes the rule and gets `concurrency.conflict` for its stale `version`. No history entry ever follows `Cancelled`, and a cancelled job never gets a submitted inspection.

### Job events

After a command commits, the API queues one event for it: `job.created` from CreateJob, `job.assigned` from AssignJob, and `job.status-changed` from UpdateJobStatus, from SubmitJob (status `Completed`, previous status `InProgress`) and from CancelJob (status `Cancelled`, previous status `Unassigned`, `Assigned`, `InProgress` or `OnHold`). SaveInspection, GetDashboardSummary and ReportClientError queue no event. A same-state 200, any error response and a rolled-back save queue nothing. The event carries identifiers and statuses only, never personal data.

- **Delivery.** When `Notifications:BaseUrl` is set (see [configuration.md](configuration.md)), one background dispatcher sends each event, in order, as a signed `POST {Notifications:BaseUrl}/internal/job-events` to the notification service, which relays it to the open dashboards. The request, the envelope and the signature follow [contracts/events.md](contracts/events.md). Without a base URL the API runs standalone and discards events.
- **Correlation.** The request carries the `X-Correlation-ID` of the command that made the change, so one id links the command's request log, the dispatch and the notification service's entries.
- **Failures never reach the client.** The command has already returned when the event is sent. Each attempt has 2 seconds, with at most 3 jittered retries for a timeout, a connection error, 408, 429 or a 5xx, inside a 10-second budget and behind a circuit breaker. A 400, 401, 404, 405, 413 or 415 is not retried. A failed delivery is logged as a Warning and never rolls back the change; dashboards refetch when they reconnect.
- **Queue.** The queue holds 1,000 events and drops the oldest when full. On shutdown the dispatcher keeps sending for at most 5 seconds. Events are invalidation hints: delivery is at most once across a restart, and the dashboard always refetches the data.

## Errors

### Problem Details

Every non-2xx response from the API has an RFC 9457 body with media type `application/problem+json`:

| Member | Type | Present | Content |
|---|---|---|---|
| `type` | string (URI) | Always | The RFC section for the status. Clients do not branch on it. |
| `title` | string | Always | The title registered for `code` |
| `status` | integer | Always | The HTTP status |
| `detail` | string | When there is something specific to say | A human-readable explanation. For 500 it is generic outside Development. |
| `instance` | string | Always | The request path without the query string |
| `errors` | object | Only with `request.validation-failed` | Field errors |
| `code` | string | Always | The stable error code. Clients branch on this. |
| `correlationId` | string | Always | The correlation id, also returned in `X-Correlation-ID`. Clients show it as the error reference. |
| `traceId` | string | Always | The W3C `traceparent` id |

No exception type, message or stack trace is returned outside Development.

In the OpenAPI document, 400 responses use the `HttpValidationProblemDetails` schema, which adds `errors`, and 401, 403, 404, 409 and 429 responses use `ProblemDetails`. Both schemas declare `code`, `correlationId` and `traceId`.

### Field errors

- `errors` maps a field path to a non-empty array of messages.
- Keys are camelCase JSON paths without the `$.` prefix, for example `title`, `scheduledEnd`, `answers[2].note` or `answers[0].readings[1].value`. Indexes count from 0 in the order the request sent the entries.
- Malformed JSON with a path, such as a wrong type, an unknown enumeration value, an explicit `null` or a duplicate member, is keyed by that path (`$.priority` becomes `priority`).
- The key `body` is used when the body is sent as `application/json` but is not JSON at all, is missing although required, or has the wrong root type.
- Query parameters are keyed by their name (`status`, `pageSize`).
- Cross-field rules are keyed by the field the user must change: `scheduledEnd` must be after `scheduledStart`.
- Messages are English sentences for display and may change. Clients key only on the path and on `code`, and show a key they cannot map to an input as a form-level message.
- Messages never repeat the submitted value.

### Error codes

| Code | HTTP | Title | Meaning | Client handling |
|---|---|---|---|---|
| `request.validation-failed` | 400 | One or more fields are invalid. | Field or query values break a rule, a referenced id does not exist, or the JSON is malformed. `errors` lists them. | Show each message on its input and unknown keys as a form message. Do not resend unchanged. |
| `auth.unauthenticated` | 401 | Authentication is required. | No bearer token, or one that is malformed, expired or signed with another key | One single-flight refresh, then retry the request once. If the refresh fails, go to sign-in. |
| `auth.invalid-credentials` | 401 | The email or password is incorrect. | Unknown email, wrong password or inactive account; the cases cannot be told apart | Show the title on the sign-in form. |
| `auth.session-expired` | 401 | The session has expired. | The refresh credential is missing, unknown, revoked, expired, reused outside the grace rule or sent through the wrong channel, or the user is inactive. IssueSocketTicket also returns it when the access token's session is revoked or past its idle or absolute expiry, or its user is inactive. | Clear the session and go to sign-in. |
| `auth.forbidden` | 403 | This action is not allowed for your role. | Authenticated, but the endpoint's policy rejects the role | Show a not-allowed message. A correct client never triggers it. |
| `auth.wrong-application` | 403 | This account uses the other FieldService app. | Correct password, wrong application for the role | Web: "Technicians sign in from the FieldService Tech app." Technician app: "Dispatchers sign in from FieldService Admin." |
| `auth.csrf-required` | 403 | The request is missing the anti-forgery header. | A cookie-based refresh or sign-out without `X-FieldService-Csrf: 1` | A client defect: report it. |
| `job.not-found` | 404 | The job was not found. | The job does not exist, or the technician is not assigned to it | Web: say the job no longer exists and go back to the list. Technician app: say the job is no longer assigned to you and reload the list. |
| `resource.not-found` | 404 | The resource was not found. | No route matches, including a route id that is not a GUID | A client defect |
| `request.method-not-allowed` | 405 | The method is not allowed for this resource. | The route exists but not for this method | A client defect |
| `concurrency.conflict` | 409 | The job was changed by someone else. | A stale `version`, or a concurrent write detected while saving | Refetch the job (and the inspection), explain that someone else changed it, and let the user act again. |
| `job.invalid-transition` | 409 | The job cannot move to that status. | UpdateJobStatus to a status the job's current status does not lead to, for example `OnHold` from `Assigned`, or any change to a `Completed` or `Cancelled` job; SubmitJob for a job that is not `InProgress`; CancelJob for a `Completed` job | Refetch and offer only the new `allowedStatusChanges`, and Cancel only while `canCancel` is true. |
| `job.not-assignable` | 409 | The job can no longer be assigned. | AssignJob on a job that is neither `Unassigned` nor `Assigned` | Refetch. The assign panel disappears because `canAssign` is false. |
| `technician.unavailable` | 409 | The technician is not available. | The chosen technician is inactive or off shift | Refetch the technicians and ask for another choice. |
| `inspection.incomplete` | 409 | The inspection checklist is not complete. | SubmitJob with unanswered checklist items, or before any answer was saved. The `detail` gives the count, for example "4 of 10 checklist items are unanswered." | Open the checklist and show how many items remain. |
| `inspection.locked` | 409 | The inspection can no longer be changed. | SaveInspection after submission, or while the job is not `InProgress`, a cancelled job's draft included; also a save or a submission that the database refuses because another request has just submitted the inspection | Refetch and show the inspection read-only. |
| `request.too-large` | 413 | The request body is too large. | The body exceeds the endpoint's limit | A client defect |
| `request.unsupported-media-type` | 415 | The request body must be JSON. | A body without `application/json`, or, on an endpoint with a body, any other `Content-Type` even when the body is empty | A client defect |
| `rate-limit.exceeded` | 429 | Too many requests. | A rate limit was hit | Wait for `Retry-After`. |
| `server.error` | 500 | An unexpected error occurred. | An unhandled failure, logged once with its stack trace and correlation id | Show a generic error with the correlation reference. Offer Retry for reads only. |

The full `type` value is `https://tools.ietf.org/html/` followed by the RFC section, for example `https://tools.ietf.org/html/rfc9110#section-15.5.1` for 400 and `https://tools.ietf.org/html/rfc6585#section-4` for 429. Codes are never renamed or reused; a client that meets an unknown code falls back to handling by status.

### Errors every endpoint can return

- Every endpoint that requires a token: 401 `auth.unauthenticated` and 403 `auth.forbidden`.
- Every endpoint with a body: 413 and 415.
- Every `/api` endpoint: 429 `rate-limit.exceeded` and 500 `server.error`.
- Every path: 404 `resource.not-found` and 405 `request.method-not-allowed`.

The endpoint sections below list only their specific errors.

### Example

CreateJob without a site address and with the end before the start:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more fields are invalid.",
  "status": 400,
  "instance": "/api/jobs",
  "errors": {
    "siteAddress": ["Enter the site address."],
    "scheduledEnd": ["The scheduled end must be after the scheduled start."]
  },
  "code": "request.validation-failed",
  "correlationId": "dd85f06c-7606-4dfc-a702-fa39647047d8",
  "traceId": "00-eafe895eb8119e6e5d06463590b2ef81-37a0025aa1f2e29f-00"
}
```

AssignJob with a stale version:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "The job was changed by someone else.",
  "status": 409,
  "instance": "/api/jobs/01a15488-9680-7989-92af-ed07fc34a890/assignment",
  "code": "concurrency.conflict",
  "correlationId": "68849554-8207-48bf-94c6-63a5151a3d93",
  "traceId": "00-eafe895eb8119e6e5d06463590b2ef81-37a0025aa1f2e29f-00"
}
```

A rule conflict carries a `detail`, for example AssignJob on a completed job:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "The job can no longer be assigned.",
  "status": 409,
  "detail": "JOB-1043 is Completed and can no longer be assigned.",
  "instance": "/api/jobs/01a15434-27e0-7d81-a2c4-3f5b8e9d1c06/assignment",
  "code": "job.not-assignable",
  "correlationId": "3f6d2a10-7c4e-4b8a-9e21-5d0c7b9a4e18",
  "traceId": "00-4b1e7c2d9a3f5e6071829304a5b6c7d8-2b3c4d5e6f708192-00"
}
```

## Shared response objects

### Reference objects

| Schema | Members | Used in |
|---|---|---|
| `UserReference` | `id` uuid, `displayName` string | Job `createdBy`; history `actor` and `technician` |
| `JobTechnicianReference` | `id` uuid, `displayName` string, `trade` Trade | `assignedTechnician` in job details and job lists |
| `ChecklistTemplateReference` | `id` uuid, `name` string, `trade` Trade | Job details; the inspection's `template` |
| `TechnicianJobReference` | `id` uuid, `number` int64, `status` JobStatus, `scheduledStart` date-time, `scheduledEnd` date-time | GetTechnicians `currentJob` and `nextJob` |

### JobDetailsResponse

Returned by CreateJob, GetJobById, AssignJob, UpdateJobStatus, SubmitJob and CancelJob.

| Member | Type | Null | Content |
|---|---|---|---|
| `id` | uuid | no | |
| `number` | int64 | no | Shown as `JOB-{number}` |
| `title` | string | no | Up to 120 characters |
| `description` | string | no | Up to 2000 |
| `priority` | JobPriority | no | |
| `status` | JobStatus | no | |
| `customerName` | string | no | Up to 120 |
| `siteAddress` | string | no | Up to 300 |
| `siteAccessNotes` | string | yes | Up to 500 |
| `contactName` | string | yes | Up to 100 |
| `contactPhone` | string | yes | Up to 32. Clients hide Call when it is null. |
| `equipment` | string | yes | Up to 200 |
| `scheduledStart` | date-time | no | |
| `scheduledEnd` | date-time | no | Always after `scheduledStart` |
| `checklistTemplate` | ChecklistTemplateReference | no | Its `trade` lets the assign panel list matching technicians first. |
| `assignedTechnician` | JobTechnicianReference | yes | Null while `Unassigned`; kept on a cancelled job that had one |
| `createdBy` | UserReference | no | |
| `createdAt` | date-time | no | |
| `updatedAt` | date-time | no | Changes on every status change, assignment and inspection save |
| `assignedAt` | date-time | yes | Latest assignment |
| `startedAt` | date-time | yes | First start |
| `completedAt` | date-time | yes | Set exactly when `Completed` |
| `cancelledAt` | date-time | yes | Set exactly when `Cancelled` |
| `history` | JobHistoryEntryResponse[] | no | The complete history, oldest first |
| `version` | uint32 | no | Send it back with the next command. |
| `affordances` | JobAffordances | no | What the caller may do with the job now |

### JobAffordances

Computed for the caller from the same transition table that enforces the commands, so no client copies a rule.

| Member | Type | Content |
|---|---|---|
| `allowedStatusChanges` | AllowedStatusChange[] | The UpdateJobStatus targets open to the caller now; `[]` when there are none |
| `canSubmit` | SubmitAffordance | Whether SubmitJob would succeed now, with the checklist progress |
| `canAssign` | boolean | AssignJob is open to the caller now |
| `canCancel` | boolean | CancelJob is open to the caller now |

`AllowedStatusChange`: `status` (JobStatus), the status to send to UpdateJobStatus, and `requiresNote` (boolean), true when the change needs a `note` (Put on hold).

`SubmitAffordance`, also returned as the inspection's `affordances.canSubmit` with the same value:

| Member | Type | Content |
|---|---|---|
| `allowed` | boolean | SubmitJob would succeed now, apart from a stale `version` |
| `blockingCode` | string or null | Null when allowed. Otherwise the error code SubmitJob would return, checked in this order: `auth.forbidden`, `job.invalid-transition`, `inspection.incomplete`. The interface text "Finish the inspection checklist first" maps to `inspection.incomplete`. |
| `answered` | int32 | Checklist items with a saved answer |
| `total` | int32 | Items in the job's checklist template |

| Caller and job status | `allowedStatusChanges` | `canSubmit` | `canAssign` | `canCancel` |
|---|---|---|---|---|
| Administrator, `Unassigned` or `Assigned` | `[]` | not allowed, `auth.forbidden` | true | true |
| Administrator, `InProgress` or `OnHold` | `[]` | not allowed, `auth.forbidden` | false | true |
| Administrator, `Completed` or `Cancelled` | `[]` | not allowed, `auth.forbidden` | false | false |
| Assigned technician, `Assigned` | `[{ "status": "InProgress", "requiresNote": false }]` (Start job) | not allowed, `job.invalid-transition` | false | false |
| Assigned technician, `InProgress` | `[{ "status": "OnHold", "requiresNote": true }]` (Put on hold) | allowed when `answered` equals `total`, otherwise `inspection.incomplete` | false | false |
| Assigned technician, `OnHold` | `[{ "status": "InProgress", "requiresNote": false }]` (Resume) | not allowed, `job.invalid-transition` | false | false |
| Assigned technician, `Completed` or `Cancelled` | `[]` | not allowed, `job.invalid-transition` | false | false |

Other technicians never receive the job: it is a 404 for them. A client offers exactly the listed changes and asks for a note when `requiresNote` is true. The dashboard shows Cancel job only while `canCancel` is true; a cancelled job is read-only in both clients, so every affordance of a `Cancelled` job is false or empty. Completing a job is SubmitJob, so `Completed` never appears in `allowedStatusChanges`; the technician app adds "Complete job" beside them and disables it while `canSubmit.allowed` is false.

### JobHistoryEntryResponse

| Member | Type | Null | Content |
|---|---|---|---|
| `id` | uuid | no | |
| `action` | JobHistoryAction | no | |
| `fromStatus` | JobStatus | yes | Null only for `Created` |
| `toStatus` | JobStatus | no | |
| `actor` | UserReference | no | Who acted |
| `technician` | UserReference | yes | The assignee, only on `Assigned` |
| `note` | string | yes | Up to 500. The reason for `PutOnHold` and `Cancelled`; null for every other action. |
| `occurredAt` | date-time | no | |

### JobSummaryResponse

One row of GetJobs.

| Member | Type | Null |
|---|---|---|
| `id` | uuid | no |
| `number` | int64 | no |
| `title` | string | no |
| `priority` | JobPriority | no |
| `status` | JobStatus | no |
| `customerName` | string | no |
| `siteAddress` | string | no |
| `scheduledStart` | date-time | no |
| `scheduledEnd` | date-time | no |
| `assignedTechnician` | JobTechnicianReference | yes |
| `updatedAt` | date-time | no |
| `version` | uint32 | no |

### InspectionResponse

Returned by GetInspection and SaveInspection.

| Member | Type | Null | Content |
|---|---|---|---|
| `jobId` | uuid | no | |
| `jobNumber` | int64 | no | |
| `jobStatus` | JobStatus | no | |
| `template` | ChecklistTemplateReference | no | The job's checklist template |
| `items` | InspectionItemResponse[] | no | Every item of the template, by `position` |
| `progress` | InspectionProgress | no | |
| `startedAt` | date-time | yes | The job's first start; time on site runs from here |
| `completedAt` | date-time | yes | The job's completion |
| `updatedAt` | date-time | yes | The last change to the inspection; null before the first save |
| `submittedAt` | date-time | yes | Set by SubmitJob |
| `technicianNotes` | string | yes | Set by SubmitJob, up to 2000 characters |
| `version` | uint32 | no | The job's `version`, which also guards the inspection. Send it with SaveInspection and SubmitJob. |
| `affordances` | InspectionAffordances | no | |

- `InspectionItemResponse`: `checklistItemId` uuid; `position` int32, from 1; `prompt` string, up to 200 characters; `readingFields` InspectionReadingFieldResponse[]; `answer` InspectionAnswerResponse or null.
- `InspectionReadingFieldResponse`: `readingFieldId` uuid; `position` int32; `label` string, up to 60; `unit` string, up to 16.
- `InspectionAnswerResponse`: `result` InspectionResult; `note` string or null, up to 1000; `readings` InspectionReadingResponse[], ordered by their field's position; `answeredAt` date-time, the save that last changed this answer.
- `InspectionReadingResponse`: `readingFieldId` uuid; `value` number with at most two decimal places.
- `InspectionProgress`: `answered`, `total`, `passCount`, `failCount` and `notApplicableCount`, all int32. Only answers to items of the job's template count.
- `InspectionAffordances`: `canEdit` (boolean), true only for the assigned technician while the job is `InProgress` and the inspection is not submitted; `canSubmit` (SubmitAffordance), the same value as on the job.

The `version` is read before the answers, so a response never pairs answers with a newer version than they were saved under.

## Endpoint index

| Feature | Operation | Method and route | Access | Rate limit | Body limit | Success |
|---|---|---|---|---|---|---|
| Auth | SignIn | `POST /api/auth/sign-in` | Anonymous | `sign-in` | 16 KB | 200 `SessionResponse` |
| Auth | RefreshSession | `POST /api/auth/refresh` | Anonymous; refresh credential | `refresh` | 16 KB | 200 `SessionResponse` |
| Auth | SignOut | `POST /api/auth/sign-out` | Anonymous; refresh credential | Global | 16 KB | 204 |
| Auth | IssueSocketTicket | `POST /api/auth/socket-tickets` | Administrator, with a live session | `socket-tickets` | 16 KB | 200 `SocketTicketResponse` |
| Dashboard | GetDashboardSummary | `GET /api/dashboard/summary?from&to` | Administrator | Global | | 200 `DashboardSummaryResponse` |
| Technicians | GetTechnicians | `GET /api/technicians` | Administrator | Global | | 200 `GetTechniciansResponse` |
| Technicians | GetAssignedJobs | `GET /api/technicians/me/jobs?view` | Technician, own jobs only | Global | | 200 `GetAssignedJobsResponse` |
| Jobs | CreateJob | `POST /api/jobs` | Administrator | Global | 1 MB | 201 `JobDetailsResponse`, `Location` |
| Jobs | GetJobs | `GET /api/jobs?search&status&technicianId&priority&page&pageSize` | Administrator | Global | | 200 `GetJobsResponse` |
| Jobs | GetJobById | `GET /api/jobs/{jobId}` | AdministratorOrTechnician, own jobs only for a technician | Global | | 200 `JobDetailsResponse` |
| Jobs | AssignJob | `PUT /api/jobs/{jobId}/assignment` | Administrator | Global | 1 MB | 200 `JobDetailsResponse` |
| Jobs | UpdateJobStatus | `PUT /api/jobs/{jobId}/status` | Technician, own jobs only | Global | 1 MB | 200 `JobDetailsResponse` |
| Jobs | SubmitJob | `POST /api/jobs/{jobId}/submission` | Technician, own jobs only | Global | 1 MB | 200 `JobDetailsResponse` |
| Jobs | CancelJob | `POST /api/jobs/{jobId}/cancellation` | Administrator | Global | 1 MB | 200 `JobDetailsResponse` |
| Inspections | GetInspection | `GET /api/jobs/{jobId}/inspection` | AdministratorOrTechnician, own jobs only for a technician | Global | | 200 `InspectionResponse` |
| Inspections | SaveInspection | `PUT /api/jobs/{jobId}/inspection` | Technician, own jobs only | Global | 1 MB | 200 `InspectionResponse` |
| ChecklistTemplates | GetChecklistTemplates | `GET /api/checklist-templates` | Administrator | Global | | 200 `GetChecklistTemplatesResponse` |
| Diagnostics | ReportClientError | `POST /api/client-errors` | Administrator | `client-errors` | 8 KB | 202, no body |
| Infrastructure | Liveness | `GET /health/live` | Anonymous | None | | 200 text |
| Infrastructure | Readiness | `GET /health/ready` | Anonymous | None | | 200 or 503 text |
| Infrastructure | OpenAPI document | `GET /openapi/v1.json` (Development and Testing) | Anonymous | None | | 200 JSON |

OpenAPI tags equal the feature names, and operation ids equal the operation names. GET endpoints are safe and idempotent and read no body; AssignJob, UpdateJobStatus, SubmitJob, CancelJob and SaveInspection are idempotent through their same-state rules. ReportClientError is not idempotent: every accepted report is logged. Every response carries `X-Correlation-ID`, `Cache-Control: no-store` and the security headers listed above; the examples show only the first two.

## Auth

Route group `/api/auth`. Sessions belong to one client: `Web` for administrators in the dashboard, `Mobile` for technicians in the technician app.

### SignIn: `POST /api/auth/sign-in`

Exchanges an email address and password for an access token and a new session.

| Item | Value |
|---|---|
| Access | Anonymous |
| Rate limit | `sign-in`: 10 per client IP address per minute |
| Body limit | 16 KB |
| Success | 200 `SessionResponse`. Web also gets `Set-Cookie: fs_refresh`. |
| Side effects | A session for the client with its first refresh token (hash only); the user's last sign-in time; the password hash upgraded when its parameters are outdated |
| Idempotency | None: every success creates a new session. |

Request body `SignInRequest`:

| Member | Type | Required | Rules |
|---|---|---|---|
| `email` | string | yes | Up to 256 characters; single line; an email address. Trimmed and lower-cased before the lookup. |
| `password` | string | yes | 1 to 256 characters, used exactly as sent; never logged or echoed |
| `client` | SessionClient | yes | `Web` accepts only administrators; `Mobile` accepts only technicians. |

Response `SessionResponse`:

| Member | Type | Null | Content |
|---|---|---|---|
| `accessToken` | string | no | The JWT |
| `accessTokenExpiresAt` | date-time | no | The token's `exp` |
| `user` | SessionUser | no | `id` uuid, `displayName` string, `role` UserRole |
| `refreshToken` | string | yes | Mobile: the 43-character refresh token. Web: always null, because the token travels only in the cookie. |

Errors:

| Status | Code | When |
|---|---|---|
| 400 | `request.validation-failed` | A missing or malformed member |
| 401 | `auth.invalid-credentials` | Unknown email, wrong password or inactive user. An unknown email is checked against a stand-in hash, so the response time does not reveal it. |
| 403 | `auth.wrong-application` | The password is correct, but the role does not match `client`. No session is created. |
| 429 | `rate-limit.exceeded` | More than 10 attempts in a minute from the address |

Example, Web, in Development through the dashboard's development server:

```http
POST /api/auth/sign-in HTTP/1.1
Host: localhost:5173
Content-Type: application/json
X-Correlation-ID: f0766650-82a1-49f8-b5cc-f3103d346544
```

```json
{
  "email": "dana.whitfield@fieldservice.example",
  "password": "<password>",
  "client": "Web"
}
```

```http
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
Cache-Control: no-store
X-Correlation-ID: f0766650-82a1-49f8-b5cc-f3103d346544
Set-Cookie: fs_refresh=<refresh token>; max-age=3600; path=/api/auth; samesite=strict; httponly
```

```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIs…",
  "accessTokenExpiresAt": "2026-10-19T14:55:00.000Z",
  "user": {
    "id": "01a0f6b1-2680-7152-92a5-bb4d3a6df7ca",
    "displayName": "Dana Whitfield",
    "role": "Administrator"
  },
  "refreshToken": null
}
```

Outside Development the cookie also carries `secure`. The shortened access token has the header `{"alg":"HS256","typ":"JWT"}` and the claims `sub` `01a0f6b1-2680-7152-92a5-bb4d3a6df7ca`, `role` `Administrator`, a `sid` naming the new session, a `jti`, `iat` and `nbf` at 14:40:00 UTC and `exp` at 14:55:00 UTC. The Mobile response has no `Set-Cookie` and carries the refresh token in `refreshToken`.

### RefreshSession: `POST /api/auth/refresh`

Rotates the refresh token and issues a new access token.

| Item | Value |
|---|---|
| Access | Anonymous. The credential is the refresh token. |
| Rate limit | `refresh`: 30 per client IP address per minute |
| Body limit | 16 KB |
| Success | 200 `SessionResponse`. Web also gets the rotated cookie; Mobile gets the rotated token in `refreshToken`. |

The credential travels through one of two channels:

| Channel | Request | Rules |
|---|---|---|
| Web | No body and no `Content-Type` (an empty JSON object sent as `application/json` is also accepted), the `fs_refresh` cookie and `X-FieldService-Csrf: 1` | A cookie without the header, or with another value, gets 403 `auth.csrf-required`. No cookie and no body token gets 401 `auth.session-expired`. Only a session created with `client: Web` is accepted. |
| Mobile | Body `RefreshSessionRequest` with `refreshToken` | The cookie is ignored and no anti-forgery header is needed. Only a session created with `client: Mobile` is accepted. |

A token presented through the wrong channel is treated as unknown.

Request body `RefreshSessionRequest` (optional):

| Member | Type | Required | Rules |
|---|---|---|---|
| `refreshToken` | string or null | no | Up to 64 characters. A value that does not decode to 32 bytes of base64url is rejected as unknown. |

Rotation:

- Concurrent refreshes of one session are serialized; each use consumes the token and issues a successor.
- The session's idle expiry moves to now plus 60 minutes (Web) or 7 days (Mobile), never past its absolute expiry.
- **Grace.** Presenting a token again less than 30 seconds after its rotation, while its successor is still unused, is treated as a retry after a lost response: the successor is revoked and a new one is issued (200).
- **Reuse.** Any other consumed token revokes the whole session: 401 `auth.session-expired`, logged as a security warning.

Errors:

| Status | Code | When |
|---|---|---|
| 400 | `request.validation-failed` | A body sent as `application/json` that is malformed or breaks the rules above. Any other `Content-Type` gets 415. |
| 401 | `auth.session-expired` | No credential; an unknown, revoked or expired token; reuse outside the grace rule; the wrong channel; an inactive user. Web: when the request presented the cookie, the response also clears it. |
| 403 | `auth.csrf-required` | Web channel without `X-FieldService-Csrf: 1`. The cookie and its token stay untouched. |
| 429 | `rate-limit.exceeded` | |

Example, Web:

```http
POST /api/auth/refresh HTTP/1.1
Host: localhost:5173
Cookie: fs_refresh=<refresh token>
X-FieldService-Csrf: 1
X-Correlation-ID: 8ce2212e-a8b0-4f11-a814-9428162f7bd0
Content-Length: 0
```

```http
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
Cache-Control: no-store
X-Correlation-ID: 8ce2212e-a8b0-4f11-a814-9428162f7bd0
Set-Cookie: fs_refresh=<rotated refresh token>; max-age=3600; path=/api/auth; samesite=strict; httponly
```

The body has the shape of the SignIn response, with a new access token and expiry, and `refreshToken` null.

Example, technician app, which sends the token in the body and receives the rotated token in `refreshToken`:

```http
POST /api/auth/refresh HTTP/1.1
Host: 10.0.2.2:5080
Content-Type: application/json
X-Correlation-ID: 2f6d8a14-3c7e-4b95-a0d1-6e9f2b4c8a73
```

```json
{
  "refreshToken": "<refresh token>"
}
```

```http
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
Cache-Control: no-store
X-Correlation-ID: 2f6d8a14-3c7e-4b95-a0d1-6e9f2b4c8a73
```

```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIs…",
  "accessTokenExpiresAt": "2026-10-19T15:07:00.000Z",
  "user": {
    "id": "01a0f6b1-2a68-77d9-8aa0-8a58bef6a99e",
    "displayName": "Marco Reyes",
    "role": "Technician"
  },
  "refreshToken": "<rotated refresh token>"
}
```

### SignOut: `POST /api/auth/sign-out`

Revokes the session that the refresh credential belongs to.

| Item | Value |
|---|---|
| Access | Anonymous. The credential is the refresh token, through the same channels as RefreshSession. |
| Rate limit | Global: 60 per minute per anonymous address |
| Body limit | 16 KB |
| Request | Web: no body and no `Content-Type`, the `fs_refresh` cookie and `X-FieldService-Csrf: 1`. Mobile: `SignOutRequest { refreshToken }`, with the rules of `RefreshSessionRequest`. |
| Success | 204 with no body. Web: a 204 clears `fs_refresh` when the request presented it; a request without the cookie gets no `Set-Cookie`. |
| Idempotency | Always 204 when the credential is missing, unknown, expired or already revoked |
| Errors | 400 for a malformed body; 403 `auth.csrf-required` for a cookie without the header, which leaves the cookie untouched; 429 |

Access tokens already issued stay valid until they expire, at most 15 minutes later.

```http
POST /api/auth/sign-out HTTP/1.1
Host: localhost:5173
Cookie: fs_refresh=<refresh token>
X-FieldService-Csrf: 1
X-Correlation-ID: 5b0f7c51-7f6e-4e43-9d0a-2c1e8b7d4a90
Content-Length: 0
```

```http
HTTP/1.1 204 No Content
Cache-Control: no-store
X-Correlation-ID: 5b0f7c51-7f6e-4e43-9d0a-2c1e8b7d4a90
Set-Cookie: fs_refresh=; expires=Thu, 01 Jan 1970 00:00:00 GMT; max-age=0; path=/api/auth; samesite=strict; httponly
```

### IssueSocketTicket: `POST /api/auth/socket-tickets`

Issues a ticket for one connection attempt to the notification service's Socket.IO endpoint. The dashboard calls it on every connect and reconnect and sends the ticket only in the handshake, as [contracts/events.md](contracts/events.md) describes.

| Item | Value |
|---|---|
| Access | Administrator |
| Rate limit | `socket-tickets`: 30 per user per minute |
| Body limit | 16 KB |
| Request | No body |
| Session check | Before issuing, the API reads the access token's session (`sid`) with its user in one indexed query. A revoked session, a session past its idle or absolute expiry, or an inactive user gets 401 `auth.session-expired`, so an access token that outlives sign-out, a reuse revocation or a deactivation cannot obtain tickets. |
| Success | 200 `SocketTicketResponse` |
| Idempotency | None: every call issues a new ticket with a new `jti`. |
| Side effects | `auth.socket-ticket.issued` is logged with the user id and the ticket id; the ticket itself is never logged. |

Response `SocketTicketResponse`:

| Member | Type | Null | Content |
|---|---|---|---|
| `ticket` | string | no | A compact JWS, valid for 60 seconds and accepted once by the notification service |
| `expiresAt` | date-time | no | The ticket's `exp`, 60 seconds after issue |

The ticket has the header `{"alg":"HS256","typ":"JWT"}` and the claims, in this order, `iss` `fieldservice-api`, `aud` `fieldservice-notifications`, `sub` (the administrator's id), `role` `Administrator`, `jti` (a new UUIDv7), `iat`, `nbf` equal to `iat`, and `exp` 60 seconds later. It is signed with `Auth:SocketTicketKey` (see [configuration.md](configuration.md)), a key separate from the access-token key, so the notification service never holds a credential the API accepts. The API's issuer reproduces the published test vector `docs/contracts/events/socket-ticket-test-vector.json`, whose key must never be configured.

Errors:

| Status | Code | When |
|---|---|---|
| 401 | `auth.unauthenticated` | No access token, or one that is malformed, expired or signed with another key. Refresh once, then retry. |
| 401 | `auth.session-expired` | The session check failed. Clear the session and go to sign-in. |
| 403 | `auth.forbidden` | A technician |
| 429 | `rate-limit.exceeded` | More than 30 tickets in a minute for the user |

Example:

```http
POST /api/auth/socket-tickets HTTP/1.1
Host: localhost:5173
Authorization: Bearer eyJhbGciOiJIUzI1NiIs…
X-Correlation-ID: 2acf6b82-ab57-48e1-882a-436523221b25
Content-Length: 0
```

```http
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
Cache-Control: no-store
X-Correlation-ID: 2acf6b82-ab57-48e1-882a-436523221b25
```

```json
{
  "ticket": "eyJhbGciOiJIUzI1NiIs…",
  "expiresAt": "2026-10-19T14:41:05.000Z"
}
```

The shortened ticket's claims are `sub` `01a0f6b1-2680-7152-92a5-bb4d3a6df7ca` (Dana Whitfield), `role` `Administrator`, `jti` `01a1549a-f988-7eaa-9ac7-728ebe591925`, `iat` and `nbf` `1792420805` (14:40:05 UTC) and `exp` `1792420865`.

## Dashboard

Route group `/api/dashboard`, policy Administrator for the whole group.

### GetDashboardSummary: `GET /api/dashboard/summary?from&to`

One read for the dashboard's hero band, the six status cards, Needs a technician and Live activity. The workload table uses [GetTechnicians](#gettechnicians-get-apitechnicians).

| Item | Value |
|---|---|
| Access | Administrator |
| Rate limit | Global |
| Success | 200 `DashboardSummaryResponse` |
| Errors | 400 `request.validation-failed`, keyed `from` or `to` |
| Event | None. The dashboard refetches the summary on every socket connect and, debounced, after every job event. |

Query parameters:

| Name | Type | Required | Rules |
|---|---|---|---|
| `from` | date-time | yes | The start of the window, inclusive. The web app sends the start of the dispatcher's local day. |
| `to` | date-time | yes | The end of the window, exclusive. It must be after `from`, and the window may span at most 31 × 24 hours. The web app sends the start of the next local day. |

The window:

- **Bounds.** `from` is inclusive and `to` exclusive for every figure of `range` and for `recentActivity`: an instant equal to `from` counts, an instant equal to `to` does not.
- **Instants.** Both values follow [Instants](#instants) and [Query parameters](#query-parameters): `Z` or a numeric offset, with a `+` sent as `%2B`, for example `from=2026-10-19T12:00:00.000%2B08:00`. The API converts them to UTC and truncates them to milliseconds before it reads; `range.from` and `range.to` echo those values in UTC.
- **Length.** After that conversion, `to` must be later than `from`, and `to` minus `from` may be at most 31 × 24 hours: exactly 31 days passes, one millisecond more is refused. The API has no time zone, so a 31-day window of local days that crosses a daylight-saving change is one hour too long; the single day the dashboard sends is never affected.
- **Today.** The client sends the start of the dispatcher's local day and the start of the next local day, never `from` plus 24 hours, because a day with a daylight-saving change has 23 or 25 hours.
- **Errors.** Both window errors are keyed `to`, the value the user changes. A missing, empty, repeated or malformed value is keyed by its own parameter:

| Key | When | Message |
|---|---|---|
| `from` or `to` | Missing | "Send the start of the window." or "Send the end of the window." |
| `from` or `to` | Empty | "Enter a value, or leave the parameter out." |
| `from` or `to` | Sent more than once | "Send this parameter at most once." |
| `from` or `to` | Not an instant with `Z` or an offset | "The value must be a date and time with Z or a numeric offset, such as 2026-10-19T04:00:00.000Z." |
| `to` | Not after `from` | "The end of the window must be after its start." |
| `to` | Longer than 31 × 24 hours | "The window may span at most 31 days." |

Response `DashboardSummaryResponse`:

| Member | Type | Null | Content |
|---|---|---|---|
| `generatedAt` | date-time | no | The server time of the read, in milliseconds |
| `openJobs` | DashboardOpenJobs | no | The current state, independent of the window |
| `range` | DashboardRange | no | Counts inside [`from`, `to`) |
| `technicians` | DashboardTechnicianCounts | no | Active technicians only |
| `needsTechnician` | DashboardUnassignedJob[] | no | The 10 earliest-scheduled `Unassigned` jobs, by `scheduledStart`, then `number`; `[]` when there are none |
| `recentActivity` | DashboardActivityEntry[] | no | At most the 6 newest history entries inside the window, newest first; among entries at one instant a `Created` entry comes last, then by `id` descending; `[]` when there are none. The dashboard renders every entry it receives. |

`DashboardOpenJobs`:

| Member | Type | Null | Content | Dashboard use |
|---|---|---|---|---|
| `total` | int32 | no | `unassigned` + `assigned` + `inProgress` + `onHold` | Hero: "17 open jobs" |
| `unassigned` | int32 | no | Jobs now `Unassigned` | Unassigned card; hero: "4 need a technician" |
| `assigned` | int32 | no | Jobs now `Assigned` | Assigned card |
| `inProgress` | int32 | no | Jobs now `InProgress` | In progress card |
| `onHold` | int32 | no | Jobs now `OnHold` | On hold card |
| `withTechnician` | int32 | no | `assigned` + `inProgress` + `onHold` | Hero: "13 open jobs assigned" |
| `inProgressTechnicianCount` | int32 | no | Distinct technicians with an `InProgress` job | "Across 2 technicians" |
| `nextAssignedStart` | date-time | yes | The earliest `scheduledStart` at or after `generatedAt` among `Assigned` jobs; null when there is none | "Next starts 11:30 AM" |
| `onHoldSince` | date-time | yes | The earliest instant at which a job now `OnHold` went on hold. Each job counts from its current hold: a job that was resumed and put on hold again counts from the later hold. Null when no job is on hold. | "Oldest since Fri 1:50 PM" |

`DashboardRange`:

| Member | Type | Null | Content | Dashboard use |
|---|---|---|---|---|
| `from`, `to` | date-time | no | The window, echoed in UTC | |
| `created` | int32 | no | Jobs created in the window | |
| `completed` | int32 | no | Jobs completed in the window | "Completed today 2" |
| `cancelled` | int32 | no | Jobs cancelled in the window | Cancelled card, subline "Today" |
| `unassignedScheduled` | int32 | no | `Unassigned` jobs whose `scheduledStart` is in the window | "2 of them today", "2 scheduled today" |
| `lastCompletedAt` | date-time | yes | The latest completion in the window; null when there is none | "Last at 9:58 AM" |

`DashboardTechnicianCounts`: `total` (int32), the active technicians, and `onShift` (int32), those of them on shift. An inactive technician is in neither count.

`DashboardUnassignedJob`: `id` uuid, `number` int64, `title` string, `priority` JobPriority, `customerName` string, `siteAddress` string, `scheduledStart` date-time, `scheduledEnd` date-time.

`DashboardActivityEntry`:

| Member | Type | Null | Content |
|---|---|---|---|
| `id` | uuid | no | The history entry's id |
| `occurredAt` | date-time | no | |
| `action` | JobHistoryAction | no | |
| `jobId` | uuid | no | |
| `jobNumber` | int64 | no | |
| `fromStatus` | JobStatus | yes | Null only for `Created` |
| `toStatus` | JobStatus | no | Drives the status chip |
| `actor` | UserReference | no | "Priya Natarajan started JOB-1049" |
| `technician` | UserReference | yes | The assignee, only on `Assigned` ("assigned JOB-1047 to Marco Reyes") |
| `note` | string | yes | The hold or cancellation reason; null for every other action |

- Every figure comes from one consistent snapshot of the database, so a change that commits while the summary is being read appears in all of the figures of the next read, never in some of them.
- With no open job, every count of `openJobs` is 0 and both of its instants are null. With no job in the window, every count of `range` is 0 and `lastCompletedAt` is null.
- Completed and cancelled jobs never change again, and a job on hold changes only when it is resumed or cancelled. The API enforces both rules, and the completion, cancellation and hold figures rely on them.

Example, the demo day, read at 10:42 AM in UTC−4. The window holds 17 history entries, and `recentActivity` returns the 6 newest:

```http
GET /api/dashboard/summary?from=2026-10-19T04:00:00.000Z&to=2026-10-20T04:00:00.000Z HTTP/1.1
Host: localhost:5173
Authorization: Bearer {accessToken}
X-Correlation-ID: ed9d4c43-3b48-403b-8edf-0f984d6942cd
```

```http
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
Cache-Control: no-store
X-Correlation-ID: ed9d4c43-3b48-403b-8edf-0f984d6942cd
```

```json
{
  "generatedAt": "2026-10-19T14:42:03.517Z",
  "openJobs": {
    "total": 17,
    "unassigned": 4,
    "assigned": 8,
    "inProgress": 2,
    "onHold": 3,
    "withTechnician": 13,
    "inProgressTechnicianCount": 2,
    "nextAssignedStart": "2026-10-19T15:30:00.000Z",
    "onHoldSince": "2026-10-16T17:50:00.000Z"
  },
  "range": {
    "from": "2026-10-19T04:00:00.000Z",
    "to": "2026-10-20T04:00:00.000Z",
    "created": 5,
    "completed": 2,
    "cancelled": 0,
    "unassignedScheduled": 2,
    "lastCompletedAt": "2026-10-19T13:58:00.000Z"
  },
  "technicians": { "total": 6, "onShift": 5 },
  "needsTechnician": [
    { "id": "01a15424-cba0-7324-8dfe-aec14fd87c8b", "number": 1052, "title": "Fire alarm panel trouble signal", "priority": "Urgent", "customerName": "Cedar Ridge Elementary School", "siteAddress": "1180 Cedar Ridge Drive", "scheduledStart": "2026-10-19T18:00:00.000Z", "scheduledEnd": "2026-10-19T19:30:00.000Z" },
    { "id": "01a15488-9680-7989-92af-ed07fc34a890", "number": 1051, "title": "Replace faulty breaker panel", "priority": "High", "customerName": "Pinecrest Public Library", "siteAddress": "300 Pinecrest Road", "scheduledStart": "2026-10-19T19:30:00.000Z", "scheduledEnd": "2026-10-19T21:00:00.000Z" },
    { "id": "01a14ff4-b680-72b1-b5c0-7f2fe309d6f0", "number": 1050, "title": "Emergency lighting test", "priority": "Low", "customerName": "Northgate Logistics Warehouse", "siteAddress": "42 Industrial Parkway, Dock 4", "scheduledStart": "2026-10-21T13:00:00.000Z", "scheduledEnd": "2026-10-21T15:00:00.000Z" },
    { "id": "01a15062-9380-7aa7-8e25-c4fbbaea52ab", "number": 1054, "title": "Leaking rooftop drain", "priority": "Normal", "customerName": "Harborview Apartments", "siteAddress": "600 Bayfront Avenue", "scheduledStart": "2026-10-22T14:00:00.000Z", "scheduledEnd": "2026-10-22T16:00:00.000Z" }
  ],
  "recentActivity": [
    { "id": "01a1549c-bac0-7dd7-887f-51d55ef3411e", "occurredAt": "2026-10-19T14:42:00.000Z", "action": "Started", "jobId": "01a153ec-f2c0-7499-a012-b2be45d90129", "jobNumber": 1049, "fromStatus": "Assigned", "toStatus": "InProgress", "actor": { "id": "01a0f6b1-3a08-7fbf-89df-c40260fc01fa", "displayName": "Priya Natarajan" }, "technician": null, "note": null },
    { "id": "01a15492-a8a0-7883-a05e-b6bbfe34d3c7", "occurredAt": "2026-10-19T14:31:00.000Z", "action": "PutOnHold", "jobId": "01a153da-a340-7b8f-b0eb-d9e42e79e054", "jobNumber": 1044, "fromStatus": "InProgress", "toStatus": "OnHold", "actor": { "id": "01a0f6b1-3238-7ae6-a3c8-a68e544219c1", "displayName": "Tom Becker" }, "technician": null, "note": "Waiting for replacement gas valve" },
    { "id": "01a15488-9680-7a4a-9498-8e59978d42cc", "occurredAt": "2026-10-19T14:20:00.000Z", "action": "Created", "jobId": "01a15488-9680-7989-92af-ed07fc34a890", "jobNumber": 1051, "fromStatus": null, "toStatus": "Unassigned", "actor": { "id": "01a0f6b1-2680-7152-92a5-bb4d3a6df7ca", "displayName": "Dana Whitfield" }, "technician": null, "note": null },
    { "id": "01a1547a-dae0-7492-9ee8-6b6ebed01c3f", "occurredAt": "2026-10-19T14:05:00.000Z", "action": "Assigned", "jobId": "01a1546d-1f40-74c9-b6a6-b365b1422136", "jobNumber": 1047, "fromStatus": "Unassigned", "toStatus": "Assigned", "actor": { "id": "01a0f6b1-2680-7152-92a5-bb4d3a6df7ca", "displayName": "Dana Whitfield" }, "technician": { "id": "01a0f6b1-2a68-77d9-8aa0-8a58bef6a99e", "displayName": "Marco Reyes" }, "note": null },
    { "id": "01a15474-7240-7bde-ac0d-e24ee480a82d", "occurredAt": "2026-10-19T13:58:00.000Z", "action": "Completed", "jobId": "01a153d1-7b80-7a19-bf23-15548395a552", "jobNumber": 1043, "fromStatus": "InProgress", "toStatus": "Completed", "actor": { "id": "01a0f6b1-2e50-70c1-9f96-2344e84e3210", "displayName": "Aisha Karim" }, "technician": null, "note": null },
    { "id": "01a1544a-5500-71c6-b856-aa0a8dd0fb9c", "occurredAt": "2026-10-19T13:12:00.000Z", "action": "Started", "jobId": "01a15415-3b40-79a4-859f-2cf627f64d02", "jobNumber": 1042, "fromStatus": "Assigned", "toStatus": "InProgress", "actor": { "id": "01a0f6b1-2a68-77d9-8aa0-8a58bef6a99e", "displayName": "Marco Reyes" }, "technician": null, "note": null }
  ]
}
```

The same window sent with offsets, `from=2026-10-19T00:00:00.000-04:00&to=2026-10-20T00:00:00.000-04:00`, returns the same figures, with `range.from` and `range.to` in UTC as above. A window of more than 31 days, for example `to=2026-11-20T04:00:00.000Z` with the `from` above, returns:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more fields are invalid.",
  "status": 400,
  "instance": "/api/dashboard/summary",
  "errors": {
    "to": ["The window may span at most 31 days."]
  },
  "code": "request.validation-failed",
  "correlationId": "5e8a1c3d-7b2f-4e90-a6d4-1f3c5b7d9e02",
  "traceId": "00-2c4e6a8b0d1f3e5a7c9b1d3f5a7c9e1b-4d6f8a0c2e4b6d8f-00"
}
```

## Technicians

Route group `/api/technicians`. Each operation sets its own policy. Technician accounts and shifts are seeded; there is no endpoint that changes them.

### GetTechnicians: `GET /api/technicians`

Every technician with their workload: the assign panel, the create form's technician select, the technicians page and the dashboard's workload table.

| Item | Value |
|---|---|
| Access | Administrator |
| Query | None |
| Success | 200 `GetTechniciansResponse { items: TechnicianResponse[] }`, ordered by `displayName`, then `id` |
| Errors | Only the errors every endpoint can return |

`TechnicianResponse`:

| Member | Type | Null | Content |
|---|---|---|---|
| `id` | uuid | no | Equals the user id |
| `displayName` | string | no | |
| `email` | string | no | Lower case |
| `phoneNumber` | string | no | |
| `trade` | Trade | no | |
| `isActive` | boolean | no | |
| `isOnShift` | boolean | no | Seeded; there is no toggle. |
| `availableForAssignment` | boolean | no | Active and on shift. AssignJob and CreateJob enforce the same rule; the assign panel disables rows where it is false. |
| `openJobCount` | int32 | no | The technician's jobs in `Assigned`, `InProgress` or `OnHold` |
| `currentJob` | TechnicianJobReference | yes | The technician's `InProgress` job, or else their `OnHold` job, with the latest `updatedAt` |
| `nextJob` | TechnicianJobReference | yes | The technician's `Assigned` job with the earliest `scheduledStart`, then the lowest number |

"Finishes JOB-1049 by 12:00 PM" is `currentJob.scheduledEnd`; "next JOB-1055 at 1:00 PM" is `nextJob.scheduledStart`; "5 of 6 on shift" is counted from the items.

```http
GET /api/technicians HTTP/1.1
Host: localhost:5173
Authorization: Bearer {accessToken}
X-Correlation-ID: 0d7f3a52-91c4-4b8e-a6f1-3e2d9c8b7a61
```

```json
{
  "items": [
    { "id": "01a0f6b1-2e50-70c1-9f96-2344e84e3210", "displayName": "Aisha Karim", "email": "aisha.karim@fieldservice.example", "phoneNumber": "(555) 014-7810", "trade": "Electrical", "isActive": true, "isOnShift": true, "availableForAssignment": true, "openJobCount": 3, "currentJob": null, "nextJob": { "id": "01a15408-6a00-72b4-9869-797a1273a48d", "number": 1055, "status": "Assigned", "scheduledStart": "2026-10-19T17:00:00.000Z", "scheduledEnd": "2026-10-19T18:30:00.000Z" } },
    { "id": "01a0f6b1-3620-7891-8f84-8b8ca5e5e243", "displayName": "Lena Park", "email": "lena.park@fieldservice.example", "phoneNumber": "(555) 014-5521", "trade": "FireSafety", "isActive": true, "isOnShift": true, "availableForAssignment": true, "openJobCount": 1, "currentJob": null, "nextJob": { "id": "01a14f86-d980-7ef5-943c-f3568d9cde9e", "number": 1045, "status": "Assigned", "scheduledStart": "2026-10-19T15:30:00.000Z", "scheduledEnd": "2026-10-19T17:00:00.000Z" } },
    { "id": "01a0f6b1-2a68-77d9-8aa0-8a58bef6a99e", "displayName": "Marco Reyes", "email": "marco.reyes@fieldservice.example", "phoneNumber": "(555) 014-2231", "trade": "Hvac", "isActive": true, "isOnShift": true, "availableForAssignment": true, "openJobCount": 3, "currentJob": { "id": "01a15415-3b40-79a4-859f-2cf627f64d02", "number": 1042, "status": "InProgress", "scheduledStart": "2026-10-19T13:00:00.000Z", "scheduledEnd": "2026-10-19T15:00:00.000Z" }, "nextJob": { "id": "01a1546d-1f40-74c9-b6a6-b365b1422136", "number": 1047, "status": "Assigned", "scheduledStart": "2026-10-19T17:30:00.000Z", "scheduledEnd": "2026-10-19T19:00:00.000Z" } },
    { "id": "01a0f6b1-3a08-7fbf-89df-c40260fc01fa", "displayName": "Priya Natarajan", "email": "priya.natarajan@fieldservice.example", "phoneNumber": "(555) 014-6604", "trade": "Electrical", "isActive": true, "isOnShift": true, "availableForAssignment": true, "openJobCount": 2, "currentJob": { "id": "01a153ec-f2c0-7499-a012-b2be45d90129", "number": 1049, "status": "InProgress", "scheduledStart": "2026-10-19T14:00:00.000Z", "scheduledEnd": "2026-10-19T16:00:00.000Z" }, "nextJob": { "id": "01a14fc6-efc0-73cf-80c7-575c4ba3c50c", "number": 1046, "status": "Assigned", "scheduledStart": "2026-10-20T13:00:00.000Z", "scheduledEnd": "2026-10-20T14:30:00.000Z" } },
    { "id": "01a0f6b1-3df0-7802-8952-7b3a85cffa38", "displayName": "Samuel Osei", "email": "samuel.osei@fieldservice.example", "phoneNumber": "(555) 014-9148", "trade": "Hvac", "isActive": true, "isOnShift": false, "availableForAssignment": false, "openJobCount": 0, "currentJob": null, "nextJob": null },
    { "id": "01a0f6b1-3238-7ae6-a3c8-a68e544219c1", "displayName": "Tom Becker", "email": "tom.becker@fieldservice.example", "phoneNumber": "(555) 014-3392", "trade": "Plumbing", "isActive": true, "isOnShift": true, "availableForAssignment": true, "openJobCount": 4, "currentJob": { "id": "01a153da-a340-7b8f-b0eb-d9e42e79e054", "number": 1044, "status": "OnHold", "scheduledStart": "2026-10-19T12:00:00.000Z", "scheduledEnd": "2026-10-19T14:00:00.000Z" }, "nextJob": { "id": "01a14fbd-c800-74f1-815d-4c974cf18ac1", "number": 1048, "status": "Assigned", "scheduledStart": "2026-10-20T12:00:00.000Z", "scheduledEnd": "2026-10-20T13:30:00.000Z" } }
  ]
}
```

### GetAssignedJobs: `GET /api/technicians/me/jobs?view`

The technician app's My jobs list, with its Active and Completed segments. The app groups the items by day.

| Item | Value |
|---|---|
| Access | Technician. The list is always the caller's own; no technician id is accepted, and another technician's job never appears. |
| Success | 200 `GetAssignedJobsResponse { items: AssignedJobResponse[] }` |
| Errors | 400 `request.validation-failed` keyed `view` for any value other than `active` or `completed`, an empty `view=` included, and for a repeated `view` |
| Event | None. The app reloads when the page appears and on pull-to-refresh. |

Query parameter `view` (string, optional, default `active`). It is a mode switch with lower-case values, not an enumeration:

| `view` | Statuses | Order and size |
|---|---|---|
| `active` | `Assigned`, `InProgress`, `OnHold` | `scheduledStart` ascending, then `number`; every match |
| `completed` | `Completed`, `Cancelled` | `updatedAt` descending, then `id` descending; at most 50 |

A cancelled job stays in the Completed segment of the technician it was assigned to.

`AssignedJobResponse`:

| Member | Type | Null |
|---|---|---|
| `id` | uuid | no |
| `number` | int64 | no |
| `title` | string | no |
| `priority` | JobPriority | no |
| `status` | JobStatus | no |
| `customerName` | string | no |
| `siteAddress` | string | no |
| `scheduledStart` | date-time | no |
| `scheduledEnd` | date-time | no |
| `startedAt` | date-time | yes |
| `completedAt` | date-time | yes |
| `cancelledAt` | date-time | yes |
| `updatedAt` | date-time | no |
| `version` | uint32 | no |

Example, Marco Reyes's active jobs from the technician app on the Android emulator:

```http
GET /api/technicians/me/jobs?view=active HTTP/1.1
Host: 10.0.2.2:5080
Authorization: Bearer {accessToken}
X-Correlation-ID: 4a7c2e19-5d83-4f60-b1e2-9c0a8d6f3b27
```

```json
{
  "items": [
    { "id": "01a15415-3b40-79a4-859f-2cf627f64d02", "number": 1042, "title": "Rooftop HVAC unit not cooling", "priority": "High", "status": "InProgress", "customerName": "Brightwater Medical Clinic", "siteAddress": "214 Harbor Road, Suite 3", "scheduledStart": "2026-10-19T13:00:00.000Z", "scheduledEnd": "2026-10-19T15:00:00.000Z", "startedAt": "2026-10-19T13:12:00.000Z", "completedAt": null, "cancelledAt": null, "updatedAt": "2026-10-19T13:50:00.000Z", "version": 48213 },
    { "id": "01a1546d-1f40-74c9-b6a6-b365b1422136", "number": 1047, "title": "Walk-in freezer temperature alarm", "priority": "Urgent", "status": "Assigned", "customerName": "Elm Street Bakery", "siteAddress": "88 Elm Street", "scheduledStart": "2026-10-19T17:30:00.000Z", "scheduledEnd": "2026-10-19T19:00:00.000Z", "startedAt": null, "completedAt": null, "cancelledAt": null, "updatedAt": "2026-10-19T14:05:00.000Z", "version": 48004 },
    { "id": "01a1502b-a500-76e7-86a7-51ea54ddabb9", "number": 1053, "title": "Rooftop unit preventive maintenance", "priority": "Low", "status": "Assigned", "customerName": "Harborview Apartments", "siteAddress": "600 Bayfront Avenue", "scheduledStart": "2026-10-20T17:00:00.000Z", "scheduledEnd": "2026-10-20T19:00:00.000Z", "startedAt": null, "completedAt": null, "cancelledAt": null, "updatedAt": "2026-10-18T20:20:00.000Z", "version": 47120 }
  ]
}
```

`GET /api/technicians/me/jobs?view=Active` returns:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more fields are invalid.",
  "status": 400,
  "instance": "/api/technicians/me/jobs",
  "errors": {
    "view": ["Choose the active or completed view."]
  },
  "code": "request.validation-failed",
  "correlationId": "9b2e4c71-0d3a-4f86-a5e2-7c1b8d9f0a34",
  "traceId": "00-1c2d3e4f5a6b7c8d9e0f1a2b3c4d5e6f-3c4d5e6f7a8b9c0d-00"
}
```

## Jobs

Route group `/api/jobs`. Each operation sets its own policy.

### CreateJob: `POST /api/jobs`

The administrator creates a job, optionally assigned to a technician at once.

| Item | Value |
|---|---|
| Access | Administrator |
| Rate limit | Global |
| Success | 201 Created, `Location: /api/jobs/{id}`, body `JobDetailsResponse` with status `Unassigned`, or `Assigned` when `technicianId` was given |
| Side effects | The next job number; a `Created` history entry with the same instant as `createdAt`, plus an `Assigned` entry naming the technician, at the same instant and in the same transaction, when `technicianId` was given; after the database commit, one `job.created` event with status `Unassigned` or `Assigned` and no separate `job.assigned` event (see [Job events](#job-events)) |
| Idempotency | None: repeating the request creates a second job. |

Request body `CreateJobRequest`:

| Member | Type | Required | Rules |
|---|---|---|---|
| `title` | string | yes | Up to 120 characters; single line |
| `description` | string | yes | Up to 2000; multi-line |
| `priority` | JobPriority | yes | |
| `customerName` | string | yes | Up to 120; single line |
| `siteAddress` | string | yes | Up to 300; single line |
| `siteAccessNotes` | string or null | no | Up to 500; multi-line |
| `contactName` | string or null | no | Up to 100; single line |
| `contactPhone` | string or null | no | Up to 32 characters matching `^\+?[0-9 ().-]{3,31}$`: an optional leading `+`, then 3 to 31 digits, spaces, parentheses, dots or hyphens. `(555) 014-6620` passes; `*`, `#`, letters and a `+` after the first character do not. |
| `equipment` | string or null | no | Up to 200; single line |
| `scheduledStart` | date-time | yes | RFC 3339 with an offset |
| `scheduledEnd` | date-time | yes | After `scheduledStart`, compared at millisecond precision (error key `scheduledEnd`) |
| `checklistTemplateId` | uuid | yes | One of the checklist templates; otherwise 400 keyed `checklistTemplateId` |
| `technicianId` | uuid or null | no | Omitted or null: the job starts `Unassigned`. Otherwise it must be a technician (400 keyed `technicianId`) who is active and on shift (409 `technician.unavailable`). |

Errors:

| Status | Code | When |
|---|---|---|
| 400 | `request.validation-failed` | A field rule, an unknown checklist template, or an unknown technician. An unknown template and an unknown technician are reported together. |
| 409 | `technician.unavailable` | The technician is inactive or off shift. |

Nothing is written and no event is queued when the request fails.

Example, the dashboard's create form filled in:

```http
POST /api/jobs HTTP/1.1
Host: localhost:5173
Authorization: Bearer {accessToken}
Content-Type: application/json
X-Correlation-ID: b2b49504-1cd6-4228-9a7b-85f907ad5a91
```

```json
{
  "title": "Exterior signage lights out",
  "description": "Both pylon signs at the north entrance are dark after 6 PM. Timer and photocell suspected.",
  "priority": "Normal",
  "customerName": "Riverside Shopping Center",
  "siteAddress": "500 Riverside Boulevard",
  "siteAccessNotes": "Meet security at the north office",
  "contactName": "Grace Liu",
  "contactPhone": "(555) 014-8812",
  "equipment": null,
  "scheduledStart": "2026-10-20T09:00:00-04:00",
  "scheduledEnd": "2026-10-20T11:00:00-04:00",
  "checklistTemplateId": "01a15175-3c00-7000-8000-000101000000"
}
```

```http
HTTP/1.1 201 Created
Content-Type: application/json; charset=utf-8
Location: /api/jobs/01a1549f-79e0-7c56-bc65-86a395b0ccdc
Cache-Control: no-store
X-Correlation-ID: b2b49504-1cd6-4228-9a7b-85f907ad5a91
```

```json
{
  "id": "01a1549f-79e0-7c56-bc65-86a395b0ccdc",
  "number": 1056,
  "title": "Exterior signage lights out",
  "description": "Both pylon signs at the north entrance are dark after 6 PM. Timer and photocell suspected.",
  "priority": "Normal",
  "status": "Unassigned",
  "customerName": "Riverside Shopping Center",
  "siteAddress": "500 Riverside Boulevard",
  "siteAccessNotes": "Meet security at the north office",
  "contactName": "Grace Liu",
  "contactPhone": "(555) 014-8812",
  "equipment": null,
  "scheduledStart": "2026-10-20T13:00:00.000Z",
  "scheduledEnd": "2026-10-20T15:00:00.000Z",
  "checklistTemplate": { "id": "01a15175-3c00-7000-8000-000101000000", "name": "General electrical inspection", "trade": "Electrical" },
  "assignedTechnician": null,
  "createdBy": { "id": "01a0f6b1-2680-7152-92a5-bb4d3a6df7ca", "displayName": "Dana Whitfield" },
  "createdAt": "2026-10-19T14:45:00.000Z",
  "updatedAt": "2026-10-19T14:45:00.000Z",
  "assignedAt": null,
  "startedAt": null,
  "completedAt": null,
  "cancelledAt": null,
  "history": [
    { "id": "01a1549f-79e0-71ae-8e7f-1896175a9d87", "action": "Created", "fromStatus": null, "toStatus": "Unassigned", "actor": { "id": "01a0f6b1-2680-7152-92a5-bb4d3a6df7ca", "displayName": "Dana Whitfield" }, "technician": null, "note": null, "occurredAt": "2026-10-19T14:45:00.000Z" }
  ],
  "version": 48241,
  "affordances": {
    "allowedStatusChanges": [],
    "canSubmit": { "allowed": false, "blockingCode": "auth.forbidden", "answered": 0, "total": 8 },
    "canAssign": true,
    "canCancel": true
  }
}
```

After the demo data is seeded, the first job created gets number 1056.

The same request with `"technicianId": "01a0f6b1-2e50-70c1-9f96-2344e84e3210"` (Aisha Karim) creates the job `Assigned`. The response then carries the assignee, `assignedAt` equal to `createdAt`, and both history entries at the same instant, `Created` first:

```json
{
  "status": "Assigned",
  "assignedTechnician": { "id": "01a0f6b1-2e50-70c1-9f96-2344e84e3210", "displayName": "Aisha Karim", "trade": "Electrical" },
  "assignedAt": "2026-10-19T14:45:00.000Z",
  "history": [
    { "id": "01a1549f-79e0-71ae-8e7f-1896175a9d87", "action": "Created", "fromStatus": null, "toStatus": "Unassigned", "actor": { "id": "01a0f6b1-2680-7152-92a5-bb4d3a6df7ca", "displayName": "Dana Whitfield" }, "technician": null, "note": null, "occurredAt": "2026-10-19T14:45:00.000Z" },
    { "id": "01a1549f-79e0-7cd5-8b6c-20edce648bc9", "action": "Assigned", "fromStatus": "Unassigned", "toStatus": "Assigned", "actor": { "id": "01a0f6b1-2680-7152-92a5-bb4d3a6df7ca", "displayName": "Dana Whitfield" }, "technician": { "id": "01a0f6b1-2e50-70c1-9f96-2344e84e3210", "displayName": "Aisha Karim" }, "note": null, "occurredAt": "2026-10-19T14:45:00.000Z" }
  ]
}
```

The other members are as above. Choosing Samuel Osei, who is off shift, returns 409 `technician.unavailable` with the detail "The technician is inactive or off shift." and creates nothing.

### GetJobs: `GET /api/jobs`

The administrator's jobs table: search, filters, status counts and paging.

| Item | Value |
|---|---|
| Access | Administrator |
| Success | 200 `GetJobsResponse` |
| Errors | 400 `request.validation-failed`, keyed by the parameter name |

Query parameters:

| Name | Type | Required | Rules |
|---|---|---|---|
| `search` | string | no | Trimmed. Empty or white space only means no search. At most 100 characters after trimming, with no control characters; otherwise 400 keyed `search`. A case-insensitive substring match against `JOB-{number}`, `title`, `customerName` and `siteAddress`, so `1042` and `job-1042` both find JOB-1042. `%`, `_` and `\` match literally. |
| `status` | JobStatus | no | One status |
| `technicianId` | uuid | no | Jobs assigned to this technician. An id with no jobs returns an empty page. |
| `priority` | JobPriority | no | One priority |
| `page` | int32 | no | 1 to 10000, default 1 |
| `pageSize` | int32 | no | 1 to 100, default 25 |

The rules for query values in [Query parameters](#query-parameters) apply: each parameter at most once, exact enumeration names, and no empty value except for `search`.

Response `GetJobsResponse`:

| Member | Type | Content |
|---|---|---|
| `items` | JobSummaryResponse[] | The page, ordered by `updatedAt` descending, then `id` descending |
| `page` | int32 | The page returned |
| `pageSize` | int32 | The page size applied |
| `totalCount` | int32 | Every job that matches all the filters |
| `statusCounts` | JobStatusCount[] | All six statuses in enumeration order, zeros included, counted with every filter except `status`. The status chips use them; "All" is their sum. |

`JobStatusCount`: `status` (JobStatus) and `count` (int32).

Example:

```http
GET /api/jobs?status=Unassigned&page=1&pageSize=25 HTTP/1.1
Host: localhost:5173
Authorization: Bearer {accessToken}
X-Correlation-ID: 584a850a-e194-4a91-a626-6b0bcf5d172d
```

```json
{
  "items": [
    { "id": "01a15488-9680-7989-92af-ed07fc34a890", "number": 1051, "title": "Replace faulty breaker panel", "priority": "High", "status": "Unassigned", "customerName": "Pinecrest Public Library", "siteAddress": "300 Pinecrest Road", "scheduledStart": "2026-10-19T19:30:00.000Z", "scheduledEnd": "2026-10-19T21:00:00.000Z", "assignedTechnician": null, "updatedAt": "2026-10-19T14:20:00.000Z", "version": 48101 },
    { "id": "01a15424-cba0-7324-8dfe-aec14fd87c8b", "number": 1052, "title": "Fire alarm panel trouble signal", "priority": "Urgent", "status": "Unassigned", "customerName": "Cedar Ridge Elementary School", "siteAddress": "1180 Cedar Ridge Drive", "scheduledStart": "2026-10-19T18:00:00.000Z", "scheduledEnd": "2026-10-19T19:30:00.000Z", "assignedTechnician": null, "updatedAt": "2026-10-19T12:31:00.000Z", "version": 47861 },
    { "id": "01a14ff4-b680-72b1-b5c0-7f2fe309d6f0", "number": 1050, "title": "Emergency lighting test", "priority": "Low", "status": "Unassigned", "customerName": "Northgate Logistics Warehouse", "siteAddress": "42 Industrial Parkway, Dock 4", "scheduledStart": "2026-10-21T13:00:00.000Z", "scheduledEnd": "2026-10-21T15:00:00.000Z", "assignedTechnician": null, "updatedAt": "2026-10-18T19:55:00.000Z", "version": 47188 },
    { "id": "01a15062-9380-7aa7-8e25-c4fbbaea52ab", "number": 1054, "title": "Leaking rooftop drain", "priority": "Normal", "status": "Unassigned", "customerName": "Harborview Apartments", "siteAddress": "600 Bayfront Avenue", "scheduledStart": "2026-10-22T14:00:00.000Z", "scheduledEnd": "2026-10-22T16:00:00.000Z", "assignedTechnician": null, "updatedAt": "2026-10-18T15:05:00.000Z", "version": 47103 }
  ],
  "page": 1,
  "pageSize": 25,
  "totalCount": 4,
  "statusCounts": [
    { "status": "Unassigned", "count": 4 },
    { "status": "Assigned", "count": 8 },
    { "status": "InProgress", "count": 2 },
    { "status": "OnHold", "count": 3 },
    { "status": "Completed", "count": 2 },
    { "status": "Cancelled", "count": 1 }
  ]
}
```

A bad query value, for example `GET /api/jobs?status=1&technicianId=abc`, returns:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more fields are invalid.",
  "status": 400,
  "instance": "/api/jobs",
  "errors": {
    "status": ["The value must be one of Unassigned, Assigned, InProgress, OnHold, Completed or Cancelled."],
    "technicianId": ["The value must be a UUID."]
  },
  "code": "request.validation-failed",
  "correlationId": "3c9f1f0e-5a4b-4f53-9d0a-6b2f7e1c8d45",
  "traceId": "00-6f1d1c2b3a4e5f60718293a4b5c6d7e8-1a2b3c4d5e6f7081-00"
}
```

### GetJobById: `GET /api/jobs/{jobId}`

Job details for both clients.

| Item | Value |
|---|---|
| Access | AdministratorOrTechnician. Administrators read any job. A technician reads only the jobs assigned to them, including completed and cancelled ones that are still assigned to them. |
| Success | 200 `JobDetailsResponse` |
| Errors | 404 `job.not-found`: the job does not exist, or a technician is not assigned to it. Both cases return the same response. |

Example, the technician view of JOB-1042:

```http
GET /api/jobs/01a15415-3b40-79a4-859f-2cf627f64d02 HTTP/1.1
Host: 10.0.2.2:5080
Authorization: Bearer {accessToken}
X-Correlation-ID: caa8b68d-452a-4513-8595-cb1ddf78a023
```

```json
{
  "id": "01a15415-3b40-79a4-859f-2cf627f64d02",
  "number": 1042,
  "title": "Rooftop HVAC unit not cooling",
  "description": "Rooftop unit RTU-2 runs but is not cooling. The waiting room is at 81°F. The customer reports the problem started on Saturday afternoon.",
  "priority": "High",
  "status": "InProgress",
  "customerName": "Brightwater Medical Clinic",
  "siteAddress": "214 Harbor Road, Suite 3",
  "siteAccessNotes": "Roof access through the east stairwell. Ask reception for the roof key.",
  "contactName": "Nina Alvarez",
  "contactPhone": "(555) 014-6620",
  "equipment": "RTU-2, 10-ton packaged rooftop unit",
  "scheduledStart": "2026-10-19T13:00:00.000Z",
  "scheduledEnd": "2026-10-19T15:00:00.000Z",
  "checklistTemplate": { "id": "01a15175-3c00-7000-8000-000102000000", "name": "Rooftop HVAC service", "trade": "Hvac" },
  "assignedTechnician": { "id": "01a0f6b1-2a68-77d9-8aa0-8a58bef6a99e", "displayName": "Marco Reyes", "trade": "Hvac" },
  "createdBy": { "id": "01a0f6b1-2680-7152-92a5-bb4d3a6df7ca", "displayName": "Dana Whitfield" },
  "createdAt": "2026-10-19T12:14:00.000Z",
  "updatedAt": "2026-10-19T13:50:00.000Z",
  "assignedAt": "2026-10-19T12:20:00.000Z",
  "startedAt": "2026-10-19T13:12:00.000Z",
  "completedAt": null,
  "cancelledAt": null,
  "history": [
    { "id": "01a15415-3b40-77f2-9c94-972cf43879ed", "action": "Created", "fromStatus": null, "toStatus": "Unassigned", "actor": { "id": "01a0f6b1-2680-7152-92a5-bb4d3a6df7ca", "displayName": "Dana Whitfield" }, "technician": null, "note": null, "occurredAt": "2026-10-19T12:14:00.000Z" },
    { "id": "01a1541a-b980-77ba-b9a7-db809866381b", "action": "Assigned", "fromStatus": "Unassigned", "toStatus": "Assigned", "actor": { "id": "01a0f6b1-2680-7152-92a5-bb4d3a6df7ca", "displayName": "Dana Whitfield" }, "technician": { "id": "01a0f6b1-2a68-77d9-8aa0-8a58bef6a99e", "displayName": "Marco Reyes" }, "note": null, "occurredAt": "2026-10-19T12:20:00.000Z" },
    { "id": "01a1544a-5500-71c6-b856-aa0a8dd0fb9c", "action": "Started", "fromStatus": "Assigned", "toStatus": "InProgress", "actor": { "id": "01a0f6b1-2a68-77d9-8aa0-8a58bef6a99e", "displayName": "Marco Reyes" }, "technician": null, "note": null, "occurredAt": "2026-10-19T13:12:00.000Z" }
  ],
  "version": 48213,
  "affordances": {
    "allowedStatusChanges": [{ "status": "OnHold", "requiresNote": true }],
    "canSubmit": { "allowed": false, "blockingCode": "inspection.incomplete", "answered": 6, "total": 10 },
    "canAssign": false,
    "canCancel": false
  }
}
```

Clients derive two display values from `history`: "In progress since 9:12 AM" is the latest entry whose `toStatus` equals the current status, and "Dana Whitfield assigned this job to you" is the latest `Assigned` entry.

### AssignJob: `PUT /api/jobs/{jobId}/assignment`

Assigns an `Unassigned` job, or reassigns an `Assigned` one, to an available technician.

| Item | Value |
|---|---|
| Access | Administrator |
| Rate limit | Global |
| Success | 200 `JobDetailsResponse` with the new `version` |
| Side effects | `assignedTechnician` and `assignedAt` set, `updatedAt` moved to the same instant; an `Assigned` history entry naming the assignee, with `fromStatus` `Unassigned` or, on a reassignment, `Assigned`; after the database commit, one `job.assigned` event with status `Assigned` and the previous status (see [Job events](#job-events)) |
| Same state | Already `Assigned` to the same technician: 200 with the current job, no write, no history entry and no event, whatever `version` was sent |
| Idempotency | Through the same-state rule, so an explicit retry after a lost response is safe |

Request body `AssignJobRequest`:

| Member | Type | Required | Rules |
|---|---|---|---|
| `technicianId` | uuid | yes | Must be a technician; any other id, an administrator's included, is 400 keyed `technicianId`. |
| `version` | uint32 | yes | The job's `version` as last read |

Errors, checked in the order of [Concurrency and idempotency](#concurrency-and-idempotency):

| Status | Code | When |
|---|---|---|
| 400 | `request.validation-failed` | A missing or malformed member, or an unknown technician |
| 404 | `job.not-found` | The job does not exist. It is reported before an unknown technician. |
| 409 | `job.not-assignable` | The job is `InProgress`, `OnHold`, `Completed` or `Cancelled`, even when the technician is its current assignee |
| 409 | `technician.unavailable` | The technician is inactive or off shift. |
| 409 | `concurrency.conflict` | A stale `version`, or another write to the job while saving |

Nothing is written and no event is queued when the request fails. Of two assignments sent with the same `version`, one succeeds and the other gets 409 `concurrency.conflict`, leaving one history entry and one event.

Example, Dana Whitfield assigns JOB-1051 to Priya Natarajan:

```http
PUT /api/jobs/01a15488-9680-7989-92af-ed07fc34a890/assignment HTTP/1.1
Host: localhost:5173
Authorization: Bearer {accessToken}
Content-Type: application/json
X-Correlation-ID: 68849554-8207-48bf-94c6-63a5151a3d93
```

```json
{
  "technicianId": "01a0f6b1-3a08-7fbf-89df-c40260fc01fa",
  "version": 48101
}
```

```http
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
Cache-Control: no-store
X-Correlation-ID: 68849554-8207-48bf-94c6-63a5151a3d93
```

```json
{
  "id": "01a15488-9680-7989-92af-ed07fc34a890",
  "number": 1051,
  "title": "Replace faulty breaker panel",
  "description": "Main breaker panel trips under load. Several 20 A breakers are warm to the touch. The library closes to the public at 5 PM.",
  "priority": "High",
  "status": "Assigned",
  "customerName": "Pinecrest Public Library",
  "siteAddress": "300 Pinecrest Road",
  "siteAccessNotes": "Main electrical room, basement level. Building supervisor will meet you at the loading door.",
  "contactName": "Owen Mitchell",
  "contactPhone": "(555) 014-3307",
  "equipment": null,
  "scheduledStart": "2026-10-19T19:30:00.000Z",
  "scheduledEnd": "2026-10-19T21:00:00.000Z",
  "checklistTemplate": { "id": "01a15175-3c00-7000-8000-000105000000", "name": "Electrical panel inspection", "trade": "Electrical" },
  "assignedTechnician": { "id": "01a0f6b1-3a08-7fbf-89df-c40260fc01fa", "displayName": "Priya Natarajan", "trade": "Electrical" },
  "createdBy": { "id": "01a0f6b1-2680-7152-92a5-bb4d3a6df7ca", "displayName": "Dana Whitfield" },
  "createdAt": "2026-10-19T14:20:00.000Z",
  "updatedAt": "2026-10-19T14:48:00.000Z",
  "assignedAt": "2026-10-19T14:48:00.000Z",
  "startedAt": null,
  "completedAt": null,
  "cancelledAt": null,
  "history": [
    { "id": "01a15488-9680-7a4a-9498-8e59978d42cc", "action": "Created", "fromStatus": null, "toStatus": "Unassigned", "actor": { "id": "01a0f6b1-2680-7152-92a5-bb4d3a6df7ca", "displayName": "Dana Whitfield" }, "technician": null, "note": null, "occurredAt": "2026-10-19T14:20:00.000Z" },
    { "id": "01a154a2-3900-7138-bfc1-d54f96a71fa5", "action": "Assigned", "fromStatus": "Unassigned", "toStatus": "Assigned", "actor": { "id": "01a0f6b1-2680-7152-92a5-bb4d3a6df7ca", "displayName": "Dana Whitfield" }, "technician": { "id": "01a0f6b1-3a08-7fbf-89df-c40260fc01fa", "displayName": "Priya Natarajan" }, "note": null, "occurredAt": "2026-10-19T14:48:00.000Z" }
  ],
  "version": 48263,
  "affordances": {
    "allowedStatusChanges": [],
    "canSubmit": { "allowed": false, "blockingCode": "auth.forbidden", "answered": 0, "total": 8 },
    "canAssign": true,
    "canCancel": true
  }
}
```

Sending the same body again returns the same job with `version` 48263 and changes nothing. Assigning Samuel Osei, who is off shift, returns 409 `technician.unavailable`; a stale `version` returns the 409 `concurrency.conflict` shown in [Example](#example).

### UpdateJobStatus: `PUT /api/jobs/{jobId}/status`

The assigned technician starts, puts on hold or resumes a job. Completing a job is not a status change and is not accepted here: it is [SubmitJob](#submitjob-post-apijobsjobidsubmission).

| Item | Value |
|---|---|
| Access | Technician, only for the jobs assigned to them |
| Rate limit | Global |
| Success | 200 `JobDetailsResponse` with the new `status` and `version` |
| Side effects | `status` and `updatedAt` set at one instant; `startedAt` on the first start only, so a resume keeps it; a `Started`, `PutOnHold` (with the note) or `Resumed` history entry by the technician; after the database commit, one `job.status-changed` event with the new status and the previous status (see [Job events](#job-events)) |
| Same state | Already in the requested status: 200 with the current job, no write, no history entry and no event, whatever `version` or `note` was sent |
| Idempotency | Through the same-state rule, so an explicit retry after a lost response is safe |

Request body `UpdateJobStatusRequest`:

| Member | Type | Required | Rules |
|---|---|---|---|
| `status` | JobStatus | yes | `InProgress` or `OnHold`. Any other value is 400 keyed `status`. |
| `note` | string or null | for `OnHold` | Up to 500 characters; multi-line. Required and not blank for `OnHold`, otherwise 400 keyed `note`. With `InProgress` it is checked for length and control characters, then discarded: only `PutOnHold` entries carry a note. |
| `version` | uint32 | yes | The job's `version` as last read |

Allowed changes, the same that `allowedStatusChanges` offers:

| From | To | History action | Offered as |
|---|---|---|---|
| `Assigned` | `InProgress` | `Started` | Start job |
| `InProgress` | `OnHold` | `PutOnHold`, with the note | Put on hold |
| `OnHold` | `InProgress` | `Resumed` | Resume |

Errors, checked in the order of [Concurrency and idempotency](#concurrency-and-idempotency):

| Status | Code | When |
|---|---|---|
| 400 | `request.validation-failed` | A missing or malformed member; a `status` other than `InProgress` or `OnHold`; a `note` over 500 characters or with control characters; `OnHold` without a usable note, which is checked after the same-state rule and the transition |
| 404 | `job.not-found` | The job does not exist or is not assigned to the caller. Both cases return the same response. |
| 409 | `job.invalid-transition` | Any other combination, for example `OnHold` from `Assigned`, or any change to a `Completed` or `Cancelled` job. The `detail` names the job and both statuses. |
| 409 | `concurrency.conflict` | A stale `version`, or another write to the job while saving |

An administrator gets 403 `auth.forbidden`. Nothing is written and no event is queued when the request fails. Of two changes sent with the same `version`, one succeeds and the other gets 409 `concurrency.conflict`, or 200 with no write when the job already holds the requested status; there is never a second history entry or event.

Example, Priya Natarajan starts JOB-1049 at 10:42 AM:

```http
PUT /api/jobs/01a153ec-f2c0-7499-a012-b2be45d90129/status HTTP/1.1
Host: 10.0.2.2:5080
Authorization: Bearer {accessToken}
Content-Type: application/json
X-Correlation-ID: 23191419-f180-45eb-bcaf-8d9912281343
```

```json
{
  "status": "InProgress",
  "note": null,
  "version": 47990
}
```

```http
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
Cache-Control: no-store
X-Correlation-ID: 23191419-f180-45eb-bcaf-8d9912281343
```

```json
{
  "id": "01a153ec-f2c0-7499-a012-b2be45d90129",
  "number": 1049,
  "title": "Standby generator load test",
  "description": "Monthly load test of the standby generator with the care home running on generator power.",
  "priority": "Normal",
  "status": "InProgress",
  "customerName": "St. Anne's Care Home",
  "siteAddress": "75 Willow Lane",
  "siteAccessNotes": null,
  "contactName": null,
  "contactPhone": null,
  "equipment": null,
  "scheduledStart": "2026-10-19T14:00:00.000Z",
  "scheduledEnd": "2026-10-19T16:00:00.000Z",
  "checklistTemplate": { "id": "01a15175-3c00-7000-8000-000101000000", "name": "General electrical inspection", "trade": "Electrical" },
  "assignedTechnician": { "id": "01a0f6b1-3a08-7fbf-89df-c40260fc01fa", "displayName": "Priya Natarajan", "trade": "Electrical" },
  "createdBy": { "id": "01a0f6b1-2680-7152-92a5-bb4d3a6df7ca", "displayName": "Dana Whitfield" },
  "createdAt": "2026-10-18T17:20:00.000Z",
  "updatedAt": "2026-10-19T14:42:00.000Z",
  "assignedAt": "2026-10-18T17:25:00.000Z",
  "startedAt": "2026-10-19T14:42:00.000Z",
  "completedAt": null,
  "cancelledAt": null,
  "history": [
    { "id": "01a153ec-f2c0-7d05-aaa3-37223455b611", "action": "Created", "fromStatus": null, "toStatus": "Unassigned", "actor": { "id": "01a0f6b1-2680-7152-92a5-bb4d3a6df7ca", "displayName": "Dana Whitfield" }, "technician": null, "note": null, "occurredAt": "2026-10-18T17:20:00.000Z" },
    { "id": "01a153f2-7100-75d3-85e9-bdb115b001ca", "action": "Assigned", "fromStatus": "Unassigned", "toStatus": "Assigned", "actor": { "id": "01a0f6b1-2680-7152-92a5-bb4d3a6df7ca", "displayName": "Dana Whitfield" }, "technician": { "id": "01a0f6b1-3a08-7fbf-89df-c40260fc01fa", "displayName": "Priya Natarajan" }, "note": null, "occurredAt": "2026-10-18T17:25:00.000Z" },
    { "id": "01a1549c-bac0-7dd7-887f-51d55ef3411e", "action": "Started", "fromStatus": "Assigned", "toStatus": "InProgress", "actor": { "id": "01a0f6b1-3a08-7fbf-89df-c40260fc01fa", "displayName": "Priya Natarajan" }, "technician": null, "note": null, "occurredAt": "2026-10-19T14:42:00.000Z" }
  ],
  "version": 48230,
  "affordances": {
    "allowedStatusChanges": [{ "status": "OnHold", "requiresNote": true }],
    "canSubmit": { "allowed": false, "blockingCode": "inspection.incomplete", "answered": 0, "total": 8 },
    "canAssign": false,
    "canCancel": false
  }
}
```

Putting the job on hold sends the reason as the note:

```json
{
  "status": "OnHold",
  "note": "Waiting for the load bank to be delivered",
  "version": 48230
}
```

The response then has status `OnHold`, a `PutOnHold` entry carrying the trimmed note, `startedAt` unchanged and `allowedStatusChanges` `[{ "status": "InProgress", "requiresNote": false }]`. Sending the same body again returns the job unchanged.

Sending `OnHold` for JOB-1047 while it is still `Assigned`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "The job cannot move to that status.",
  "status": 409,
  "detail": "JOB-1047 cannot move from Assigned to OnHold.",
  "instance": "/api/jobs/01a1546d-1f40-74c9-b6a6-b365b1422136/status",
  "code": "job.invalid-transition",
  "correlationId": "9b0e4c71-2d5a-4f86-a3c1-7e2f9d4b6a05",
  "traceId": "00-7c3a9e1f5b2d4086a1e9c7b35d2f8e40-5e1a9c3b7d2f4a60-00"
}
```

### SubmitJob: `POST /api/jobs/{jobId}/submission`

The assigned technician completes the job and submits its inspection in one transaction.

| Item | Value |
|---|---|
| Access | Technician, only for the jobs assigned to them |
| Rate limit | Global |
| Success | 200 `JobDetailsResponse` with status `Completed` and the new `version`. The job and its inspection are read-only from then on. |
| Side effects | In one transaction, at one instant: the job's `status`, `completedAt` and `updatedAt`; the inspection's `submittedAt` and `technicianNotes`; a `Completed` history entry by the technician, without a note. After the database commit, one `job.status-changed` event with status `Completed` and previous status `InProgress` (see [Job events](#job-events)). |
| Same state | Already `Completed`: 200 with the current job, no write, no history entry and no event, whatever `version` or `technicianNotes` was sent |
| Idempotency | Through the same-state rule, so an explicit retry after a lost response is safe |

Request body `SubmitJobRequest`:

| Member | Type | Required | Rules |
|---|---|---|---|
| `technicianNotes` | string or null | no | Up to 2000 characters; multi-line. Trimmed; empty after trimming is stored as `null`. |
| `siteSafetyConfirmed` | boolean | yes | Must be `true`; `false`, `null` or a missing member is 400 keyed `siteSafetyConfirmed` |
| `version` | uint32 | yes | The job's `version` as last read, for example from the SaveInspection response |

Errors, checked in the order of [Concurrency and idempotency](#concurrency-and-idempotency):

| Status | Code | When |
|---|---|---|
| 400 | `request.validation-failed` | A missing or malformed member; `siteSafetyConfirmed` not `true`; `technicianNotes` over 2000 characters or with control characters. These are request rules, so they come first, even for a job that is already `Completed`. |
| 404 | `job.not-found` | The job does not exist or is not assigned to the caller. Both cases return the same response. |
| 409 | `job.invalid-transition` | The job is not `InProgress`; a job on hold must be resumed first. The `detail` names the job and its status, for example "JOB-1047 cannot move from Assigned to Completed." |
| 409 | `inspection.incomplete` | An item has no saved answer, or no answer was ever saved. The `detail` gives the count, for example "4 of 10 checklist items are unanswered." |
| 409 | `concurrency.conflict` | A stale `version`, for example after a save from another device, or another write to the job while saving |
| 409 | `inspection.locked` | Only in a race: another request submitted the inspection after this request read it (see [Concurrency and idempotency](#concurrency-and-idempotency)). Refetch: the job is already `Completed`. |

An administrator gets 403 `auth.forbidden`. Nothing is written and no event is queued when the request fails.

Example, Marco Reyes submits JOB-1042 at 10:58 AM with the `version` that SaveInspection returned:

```http
POST /api/jobs/01a15415-3b40-79a4-859f-2cf627f64d02/submission HTTP/1.1
Host: 10.0.2.2:5080
Authorization: Bearer {accessToken}
Content-Type: application/json
X-Correlation-ID: 2b5cc4d7-0ef9-4d7c-8a3d-ec81067ed1e7
```

```json
{
  "technicianNotes": "Recharged the system to spec and cleaned the condenser coil. Unit is holding 54 °F supply air. Fan motor bearing is noisy; replacement quote requested.",
  "siteSafetyConfirmed": true,
  "version": 48377
}
```

```http
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
Cache-Control: no-store
X-Correlation-ID: 2b5cc4d7-0ef9-4d7c-8a3d-ec81067ed1e7
```

```json
{
  "id": "01a15415-3b40-79a4-859f-2cf627f64d02",
  "number": 1042,
  "title": "Rooftop HVAC unit not cooling",
  "description": "Rooftop unit RTU-2 runs but is not cooling. The waiting room is at 81°F. The customer reports the problem started on Saturday afternoon.",
  "priority": "High",
  "status": "Completed",
  "customerName": "Brightwater Medical Clinic",
  "siteAddress": "214 Harbor Road, Suite 3",
  "siteAccessNotes": "Roof access through the east stairwell. Ask reception for the roof key.",
  "contactName": "Nina Alvarez",
  "contactPhone": "(555) 014-6620",
  "equipment": "RTU-2, 10-ton packaged rooftop unit",
  "scheduledStart": "2026-10-19T13:00:00.000Z",
  "scheduledEnd": "2026-10-19T15:00:00.000Z",
  "checklistTemplate": { "id": "01a15175-3c00-7000-8000-000102000000", "name": "Rooftop HVAC service", "trade": "Hvac" },
  "assignedTechnician": { "id": "01a0f6b1-2a68-77d9-8aa0-8a58bef6a99e", "displayName": "Marco Reyes", "trade": "Hvac" },
  "createdBy": { "id": "01a0f6b1-2680-7152-92a5-bb4d3a6df7ca", "displayName": "Dana Whitfield" },
  "createdAt": "2026-10-19T12:14:00.000Z",
  "updatedAt": "2026-10-19T14:58:00.000Z",
  "assignedAt": "2026-10-19T12:20:00.000Z",
  "startedAt": "2026-10-19T13:12:00.000Z",
  "completedAt": "2026-10-19T14:58:00.000Z",
  "cancelledAt": null,
  "history": [
    { "id": "01a15415-3b40-77f2-9c94-972cf43879ed", "action": "Created", "fromStatus": null, "toStatus": "Unassigned", "actor": { "id": "01a0f6b1-2680-7152-92a5-bb4d3a6df7ca", "displayName": "Dana Whitfield" }, "technician": null, "note": null, "occurredAt": "2026-10-19T12:14:00.000Z" },
    { "id": "01a1541a-b980-77ba-b9a7-db809866381b", "action": "Assigned", "fromStatus": "Unassigned", "toStatus": "Assigned", "actor": { "id": "01a0f6b1-2680-7152-92a5-bb4d3a6df7ca", "displayName": "Dana Whitfield" }, "technician": { "id": "01a0f6b1-2a68-77d9-8aa0-8a58bef6a99e", "displayName": "Marco Reyes" }, "note": null, "occurredAt": "2026-10-19T12:20:00.000Z" },
    { "id": "01a1544a-5500-71c6-b856-aa0a8dd0fb9c", "action": "Started", "fromStatus": "Assigned", "toStatus": "InProgress", "actor": { "id": "01a0f6b1-2a68-77d9-8aa0-8a58bef6a99e", "displayName": "Marco Reyes" }, "technician": null, "note": null, "occurredAt": "2026-10-19T13:12:00.000Z" },
    { "id": "01a154ab-60c0-7ff7-b99b-3a4ca2cef8ab", "action": "Completed", "fromStatus": "InProgress", "toStatus": "Completed", "actor": { "id": "01a0f6b1-2a68-77d9-8aa0-8a58bef6a99e", "displayName": "Marco Reyes" }, "technician": null, "note": null, "occurredAt": "2026-10-19T14:58:00.000Z" }
  ],
  "version": 48402,
  "affordances": {
    "allowedStatusChanges": [],
    "canSubmit": { "allowed": false, "blockingCode": "job.invalid-transition", "answered": 10, "total": 10 },
    "canAssign": false,
    "canCancel": false
  }
}
```

Submitting the same job before the checklist is complete, at 6 of 10:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "The inspection checklist is not complete.",
  "status": 409,
  "detail": "4 of 10 checklist items are unanswered.",
  "instance": "/api/jobs/01a15415-3b40-79a4-859f-2cf627f64d02/submission",
  "code": "inspection.incomplete",
  "correlationId": "4f1d2c8e-9a3b-4e6f-8c7d-1b2a3c4d5e6f",
  "traceId": "00-3d8f1a2b4c5e6f708192a3b4c5d6e7f8-1a2b3c4d5e6f7081-00"
}
```

### CancelJob: `POST /api/jobs/{jobId}/cancellation`

The administrator cancels a job that is not yet complete.

| Item | Value |
|---|---|
| Access | Administrator |
| Rate limit | Global |
| Success | 200 `JobDetailsResponse` with status `Cancelled`, the new `version` and `canCancel` false. The job becomes read-only and keeps its assignee, so the technician still sees it in the app's Completed segment. |
| Side effects | `status`, `cancelledAt` and `updatedAt` set at one instant; a `Cancelled` history entry by the administrator, with `fromStatus` the status the job had and the trimmed reason as its note. A draft inspection stays unsubmitted, keeps its answers and can no longer be changed. After the database commit, one `job.status-changed` event with status `Cancelled` and the previous status (see [Job events](#job-events)). |
| Same state | Already `Cancelled`: 200 with the current job, no write, no history entry and no event, whatever `version` or `reason` was sent; the reason is not stored |
| Idempotency | Through the same-state rule, so an explicit retry after a lost response is safe |

Request body `CancelJobRequest`:

| Member | Type | Required | Rules |
|---|---|---|---|
| `reason` | string | yes | Up to 500 characters as sent; multi-line. It must contain a character that is not white space. Trimmed before it is stored. |
| `version` | uint32 | yes | The job's `version` as last read |

Allowed changes, the same that `canCancel` reports:

| From | To | History action |
|---|---|---|
| `Unassigned`, `Assigned`, `InProgress` or `OnHold` | `Cancelled` | `Cancelled`, with the reason |

Errors, checked in the order of [Concurrency and idempotency](#concurrency-and-idempotency):

| Status | Code | When |
|---|---|---|
| 400 | `request.validation-failed` | A missing or malformed member; a `reason` that is blank, longer than 500 characters or carries control characters other than line feed, carriage return and tab. These are request rules, so they come first, even for a job that is already `Cancelled` or does not exist. |
| 404 | `job.not-found` | The job does not exist. |
| 409 | `job.invalid-transition` | The job is `Completed`, whatever `version` was sent. The `detail` names the job, for example "JOB-1043 cannot move from Completed to Cancelled." |
| 409 | `concurrency.conflict` | A stale `version`, or another write to the job while saving. A request that races another command is described in [Concurrency and idempotency](#concurrency-and-idempotency). |

A technician gets 403 `auth.forbidden`. Nothing is written and no event is queued when the request fails.

After the cancellation:

- UpdateJobStatus and SubmitJob return 409 `job.invalid-transition`, AssignJob 409 `job.not-assignable`, and SaveInspection 409 `inspection.locked`, for example "JOB-1042 is Cancelled, so its inspection cannot be changed."; another CancelJob returns 200 with no write.
- GetJobById shows status `Cancelled`, the `Cancelled` entry with its reason, `allowedStatusChanges` `[]` and `canSubmit`, `canAssign` and `canCancel` all false, for the administrator and for the assigned technician alike.
- GetInspection shows the draft's answers with `canEdit` false and `submittedAt` null, and GetAssignedJobs lists the job under `view=completed` for its technician.

Example, Dana Whitfield cancels JOB-1036, assigned to Marco Reyes, on Friday 16 October at 9:20 AM, before its 2:00 PM slot. The demo data records this cancellation:

```http
POST /api/jobs/01a13a7f-8c80-73ff-9f1c-a6b13fb5ef59/cancellation HTTP/1.1
Host: localhost:5173
Authorization: Bearer {accessToken}
Content-Type: application/json
X-Correlation-ID: 611549e3-e997-4613-8d16-c995d8fea8ab
```

```json
{
  "reason": "Building management replaced the filters under its own maintenance contract.",
  "version": 41877
}
```

```http
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
Cache-Control: no-store
X-Correlation-ID: 611549e3-e997-4613-8d16-c995d8fea8ab
```

```json
{
  "id": "01a13a7f-8c80-73ff-9f1c-a6b13fb5ef59",
  "number": 1036,
  "title": "Office AC filter replacement",
  "description": "Replace the filters on the office split units on Floor 4.",
  "priority": "Low",
  "status": "Cancelled",
  "customerName": "Summit Coworking",
  "siteAddress": "12 Summit Plaza, Floor 4",
  "siteAccessNotes": null,
  "contactName": null,
  "contactPhone": null,
  "equipment": null,
  "scheduledStart": "2026-10-16T18:00:00.000Z",
  "scheduledEnd": "2026-10-16T19:00:00.000Z",
  "checklistTemplate": { "id": "01a15175-3c00-7000-8000-000102000000", "name": "Rooftop HVAC service", "trade": "Hvac" },
  "assignedTechnician": { "id": "01a0f6b1-2a68-77d9-8aa0-8a58bef6a99e", "displayName": "Marco Reyes", "trade": "Hvac" },
  "createdBy": { "id": "01a0f6b1-2680-7152-92a5-bb4d3a6df7ca", "displayName": "Dana Whitfield" },
  "createdAt": "2026-10-15T13:40:00.000Z",
  "updatedAt": "2026-10-16T13:20:00.000Z",
  "assignedAt": "2026-10-15T13:45:00.000Z",
  "startedAt": null,
  "completedAt": null,
  "cancelledAt": "2026-10-16T13:20:00.000Z",
  "history": [
    { "id": "01a13a7f-8c80-7316-b699-6b5813bac87c", "action": "Created", "fromStatus": null, "toStatus": "Unassigned", "actor": { "id": "01a0f6b1-2680-7152-92a5-bb4d3a6df7ca", "displayName": "Dana Whitfield" }, "technician": null, "note": null, "occurredAt": "2026-10-15T13:40:00.000Z" },
    { "id": "01a13a84-2060-7b5e-a924-5012174524d8", "action": "Assigned", "fromStatus": "Unassigned", "toStatus": "Assigned", "actor": { "id": "01a0f6b1-2680-7152-92a5-bb4d3a6df7ca", "displayName": "Dana Whitfield" }, "technician": { "id": "01a0f6b1-2a68-77d9-8aa0-8a58bef6a99e", "displayName": "Marco Reyes" }, "note": null, "occurredAt": "2026-10-15T13:45:00.000Z" },
    { "id": "01a14543-4940-7439-9ace-2cb40c72f52d", "action": "Cancelled", "fromStatus": "Assigned", "toStatus": "Cancelled", "actor": { "id": "01a0f6b1-2680-7152-92a5-bb4d3a6df7ca", "displayName": "Dana Whitfield" }, "technician": null, "note": "Building management replaced the filters under its own maintenance contract.", "occurredAt": "2026-10-16T13:20:00.000Z" }
  ],
  "version": 41902,
  "affordances": {
    "allowedStatusChanges": [],
    "canSubmit": { "allowed": false, "blockingCode": "auth.forbidden", "answered": 0, "total": 10 },
    "canAssign": false,
    "canCancel": false
  }
}
```

Sending the request again, with this or another valid `reason` and any `version`, returns this job unchanged. Cancelling JOB-1043, which Aisha Karim completed at 9:58 AM, returns:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "The job cannot move to that status.",
  "status": 409,
  "detail": "JOB-1043 cannot move from Completed to Cancelled.",
  "instance": "/api/jobs/01a153d1-7b80-7a19-bf23-15548395a552/cancellation",
  "code": "job.invalid-transition",
  "correlationId": "c81b2f6e-4d0a-4b3c-9e57-2a6d8f1c3b49",
  "traceId": "00-8e2c4a6b1d3f5e7a9c0b2d4f6a8c1e3b-6b8d0f2a4c6e8b1d-00"
}
```

## Inspections

Route group `/api/jobs/{jobId}/inspection`. Each operation sets its own policy. A job's inspection is its checklist: the items of the job's checklist template, the technician's answers with their readings, and the submission. It is a draft that the technician saves in parts, and it becomes read-only when SubmitJob completes the job, or when CancelJob cancels it: a cancelled job's draft stays unsubmitted and can no longer be changed.

### GetInspection: `GET /api/jobs/{jobId}/inspection`

The technician's checklist and review screens, and the administrator's read-only inspection report.

| Item | Value |
|---|---|
| Access | AdministratorOrTechnician. Administrators read any job's inspection; a technician reads only those of the jobs assigned to them. |
| Success | 200 `InspectionResponse`, also before the first save: every `answer` is `null`, `updatedAt` is `null` and `progress` counts 0 answered |
| Errors | 404 `job.not-found`: the job does not exist, or a technician is not assigned to it. Both cases return the same response. |

Example, JOB-1042 at 10:42 AM, after items 01 to 03 were saved at 9:31 AM and items 04 to 06 at 9:50 AM:

```http
GET /api/jobs/01a15415-3b40-79a4-859f-2cf627f64d02/inspection HTTP/1.1
Host: 10.0.2.2:5080
Authorization: Bearer {accessToken}
X-Correlation-ID: 9e4b1c7d-2f38-4a65-8d0e-7c1b5a3f6e92
```

```json
{
  "jobId": "01a15415-3b40-79a4-859f-2cf627f64d02",
  "jobNumber": 1042,
  "jobStatus": "InProgress",
  "template": { "id": "01a15175-3c00-7000-8000-000102000000", "name": "Rooftop HVAC service", "trade": "Hvac" },
  "items": [
    { "checklistItemId": "01a15175-3c00-7000-8000-000202010000", "position": 1, "prompt": "Lock out and tag out unit power", "readingFields": [], "answer": { "result": "Pass", "note": null, "readings": [], "answeredAt": "2026-10-19T13:31:00.000Z" } },
    { "checklistItemId": "01a15175-3c00-7000-8000-000202020000", "position": 2, "prompt": "Replace return air filters", "readingFields": [], "answer": { "result": "Pass", "note": null, "readings": [], "answeredAt": "2026-10-19T13:31:00.000Z" } },
    { "checklistItemId": "01a15175-3c00-7000-8000-000202030000", "position": 3, "prompt": "Check refrigerant pressures", "readingFields": [{ "readingFieldId": "01a15175-3c00-7000-8000-000302030100", "position": 1, "label": "Suction", "unit": "psi" }, { "readingFieldId": "01a15175-3c00-7000-8000-000302030200", "position": 2, "label": "Discharge", "unit": "psi" }], "answer": { "result": "Pass", "note": null, "readings": [{ "readingFieldId": "01a15175-3c00-7000-8000-000302030100", "value": 118.00 }, { "readingFieldId": "01a15175-3c00-7000-8000-000302030200", "value": 285.00 }], "answeredAt": "2026-10-19T13:31:00.000Z" } },
    { "checklistItemId": "01a15175-3c00-7000-8000-000202040000", "position": 4, "prompt": "Clean condenser coil", "readingFields": [], "answer": { "result": "Pass", "note": null, "readings": [], "answeredAt": "2026-10-19T13:50:00.000Z" } },
    { "checklistItemId": "01a15175-3c00-7000-8000-000202050000", "position": 5, "prompt": "Inspect condenser fan motor and bearings", "readingFields": [], "answer": { "result": "Fail", "note": "Bearing noise at start-up. Recommend motor replacement within 30 days.", "readings": [], "answeredAt": "2026-10-19T13:50:00.000Z" } },
    { "checklistItemId": "01a15175-3c00-7000-8000-000202060000", "position": 6, "prompt": "Check belts and pulleys", "readingFields": [], "answer": { "result": "NotApplicable", "note": "Direct-drive unit", "readings": [], "answeredAt": "2026-10-19T13:50:00.000Z" } },
    { "checklistItemId": "01a15175-3c00-7000-8000-000202070000", "position": 7, "prompt": "Clear condensate drain line", "readingFields": [], "answer": null },
    { "checklistItemId": "01a15175-3c00-7000-8000-000202080000", "position": 8, "prompt": "Record supply air temperature", "readingFields": [{ "readingFieldId": "01a15175-3c00-7000-8000-000302080100", "position": 1, "label": "Supply air temperature", "unit": "°F" }], "answer": null },
    { "checklistItemId": "01a15175-3c00-7000-8000-000202090000", "position": 9, "prompt": "Test thermostat and economizer controls", "readingFields": [], "answer": null },
    { "checklistItemId": "01a15175-3c00-7000-8000-000202100000", "position": 10, "prompt": "Restore power and confirm normal operation", "readingFields": [], "answer": null }
  ],
  "progress": { "answered": 6, "total": 10, "passCount": 4, "failCount": 1, "notApplicableCount": 1 },
  "startedAt": "2026-10-19T13:12:00.000Z",
  "completedAt": null,
  "updatedAt": "2026-10-19T13:50:00.000Z",
  "submittedAt": null,
  "technicianNotes": null,
  "version": 48213,
  "affordances": {
    "canEdit": true,
    "canSubmit": { "allowed": false, "blockingCode": "inspection.incomplete", "answered": 6, "total": 10 }
  }
}
```

After submission the same request returns `jobStatus` `Completed`, `completedAt` and `submittedAt` at the submission instant, the `technicianNotes`, `canEdit` false and `canSubmit` blocked by `job.invalid-transition`. Time on site is `startedAt` to `completedAt`.

### SaveInspection: `PUT /api/jobs/{jobId}/inspection`

Saves any subset of answers as a draft. The first save creates the inspection. The technician app saves when the technician leaves the checklist and before Review and submit.

| Item | Value |
|---|---|
| Access | Technician, only for the jobs assigned to them |
| Rate limit | Global |
| Success | 200 `InspectionResponse` with the job's new `version` |
| Side effects | Upserts the listed answers and their readings, in checklist order; sets the inspection's `updatedAt` and the job's `updatedAt` at one instant. Every save that changes an answer moves the job's `version`, even in the same millisecond as the job's previous write. |
| Same state | Every listed answer equals the saved one: 200 with the current inspection, no write, `version` unchanged, whatever `version` was sent |
| Event | None |

Request body `SaveInspectionRequest`:

| Member | Type | Required | Rules |
|---|---|---|---|
| `answers` | InspectionAnswerInput[] | yes | 1 to 100 entries, each for a different item, none `null` |
| `version` | uint32 | yes | The job's `version` as last read |

`InspectionAnswerInput`:

| Member | Type | Required | Rules |
|---|---|---|---|
| `checklistItemId` | uuid | yes | An item of the job's checklist template |
| `result` | InspectionResult | yes | `Pass`, `Fail` or `NotApplicable` |
| `note` | string or null | for `Fail` | Up to 1000 characters; multi-line. A `Fail` needs a note that is not blank after trimming every kind of white space. Trimmed; empty after trimming is stored as `null`. |
| `readings` | InspectionReadingInput[] or null | for `Pass` on an item with reading fields | At most 10, none `null`. A `Pass` supplies every reading field of its item exactly once; a `NotApplicable` answer supplies none; a `Fail` may supply any of them. |

`InspectionReadingInput`:

| Member | Type | Required | Rules |
|---|---|---|---|
| `readingFieldId` | uuid | yes | A reading field of this answer's item, not repeated |
| `value` | number | yes | −99999999.99 to 99999999.99 with at most two decimal places. A third decimal is refused, never rounded by the API; numbers with more than 28 significant digits are rounded by the JSON parser before the check (see [Identifiers, numbers and enumerations](#identifiers-numbers-and-enumerations)). |

- A listed answer replaces that item's saved answer completely, readings included: a reading the new answer does not list is removed. Unlisted items stay as they are.
- An answer cannot be cleared back to unanswered.
- `answeredAt` changes only when the answer changes.

Errors, checked in the order of [Concurrency and idempotency](#concurrency-and-idempotency). Every field error is reported before `inspection.locked`, and all of them in one response:

| Status | Code | When |
|---|---|---|
| 400 | `request.validation-failed` | The rules above, keyed by the path of the member to fix, for example `answers[2].note`, `answers[0].readings`, `answers[1].checklistItemId` or `answers[0].readings[1].value` |
| 404 | `job.not-found` | The job does not exist or is not assigned to the caller. It is reported before an unknown item. |
| 409 | `inspection.locked` | The inspection was submitted ("The inspection was submitted and can no longer be changed."), or the job is not `InProgress` ("JOB-1047 is Assigned, so its inspection cannot be changed."); also a save that reaches the database after another request submitted the inspection |
| 409 | `concurrency.conflict` | A stale `version`, for example from a second device, or a first save racing another first save |

An administrator gets 403 `auth.forbidden`. Nothing is written when the request fails.

Example, Marco Reyes answers items 07 to 10 of JOB-1042 at 10:56 AM:

```http
PUT /api/jobs/01a15415-3b40-79a4-859f-2cf627f64d02/inspection HTTP/1.1
Host: 10.0.2.2:5080
Authorization: Bearer {accessToken}
Content-Type: application/json
X-Correlation-ID: fcc23b2a-1b8a-4e99-8d09-7751ed12657f
```

```json
{
  "answers": [
    { "checklistItemId": "01a15175-3c00-7000-8000-000202070000", "result": "Pass", "note": null, "readings": null },
    { "checklistItemId": "01a15175-3c00-7000-8000-000202080000", "result": "Pass", "note": null, "readings": [{ "readingFieldId": "01a15175-3c00-7000-8000-000302080100", "value": 54 }] },
    { "checklistItemId": "01a15175-3c00-7000-8000-000202090000", "result": "Pass", "note": null, "readings": null },
    { "checklistItemId": "01a15175-3c00-7000-8000-000202100000", "result": "Pass", "note": null, "readings": null }
  ],
  "version": 48213
}
```

```http
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
Cache-Control: no-store
X-Correlation-ID: fcc23b2a-1b8a-4e99-8d09-7751ed12657f
```

```json
{
  "jobId": "01a15415-3b40-79a4-859f-2cf627f64d02",
  "jobNumber": 1042,
  "jobStatus": "InProgress",
  "template": { "id": "01a15175-3c00-7000-8000-000102000000", "name": "Rooftop HVAC service", "trade": "Hvac" },
  "items": [
    { "checklistItemId": "01a15175-3c00-7000-8000-000202010000", "position": 1, "prompt": "Lock out and tag out unit power", "readingFields": [], "answer": { "result": "Pass", "note": null, "readings": [], "answeredAt": "2026-10-19T13:31:00.000Z" } },
    { "checklistItemId": "01a15175-3c00-7000-8000-000202020000", "position": 2, "prompt": "Replace return air filters", "readingFields": [], "answer": { "result": "Pass", "note": null, "readings": [], "answeredAt": "2026-10-19T13:31:00.000Z" } },
    { "checklistItemId": "01a15175-3c00-7000-8000-000202030000", "position": 3, "prompt": "Check refrigerant pressures", "readingFields": [{ "readingFieldId": "01a15175-3c00-7000-8000-000302030100", "position": 1, "label": "Suction", "unit": "psi" }, { "readingFieldId": "01a15175-3c00-7000-8000-000302030200", "position": 2, "label": "Discharge", "unit": "psi" }], "answer": { "result": "Pass", "note": null, "readings": [{ "readingFieldId": "01a15175-3c00-7000-8000-000302030100", "value": 118.00 }, { "readingFieldId": "01a15175-3c00-7000-8000-000302030200", "value": 285.00 }], "answeredAt": "2026-10-19T13:31:00.000Z" } },
    { "checklistItemId": "01a15175-3c00-7000-8000-000202040000", "position": 4, "prompt": "Clean condenser coil", "readingFields": [], "answer": { "result": "Pass", "note": null, "readings": [], "answeredAt": "2026-10-19T13:50:00.000Z" } },
    { "checklistItemId": "01a15175-3c00-7000-8000-000202050000", "position": 5, "prompt": "Inspect condenser fan motor and bearings", "readingFields": [], "answer": { "result": "Fail", "note": "Bearing noise at start-up. Recommend motor replacement within 30 days.", "readings": [], "answeredAt": "2026-10-19T13:50:00.000Z" } },
    { "checklistItemId": "01a15175-3c00-7000-8000-000202060000", "position": 6, "prompt": "Check belts and pulleys", "readingFields": [], "answer": { "result": "NotApplicable", "note": "Direct-drive unit", "readings": [], "answeredAt": "2026-10-19T13:50:00.000Z" } },
    { "checklistItemId": "01a15175-3c00-7000-8000-000202070000", "position": 7, "prompt": "Clear condensate drain line", "readingFields": [], "answer": { "result": "Pass", "note": null, "readings": [], "answeredAt": "2026-10-19T14:56:03.250Z" } },
    { "checklistItemId": "01a15175-3c00-7000-8000-000202080000", "position": 8, "prompt": "Record supply air temperature", "readingFields": [{ "readingFieldId": "01a15175-3c00-7000-8000-000302080100", "position": 1, "label": "Supply air temperature", "unit": "°F" }], "answer": { "result": "Pass", "note": null, "readings": [{ "readingFieldId": "01a15175-3c00-7000-8000-000302080100", "value": 54.00 }], "answeredAt": "2026-10-19T14:56:03.250Z" } },
    { "checklistItemId": "01a15175-3c00-7000-8000-000202090000", "position": 9, "prompt": "Test thermostat and economizer controls", "readingFields": [], "answer": { "result": "Pass", "note": null, "readings": [], "answeredAt": "2026-10-19T14:56:03.250Z" } },
    { "checklistItemId": "01a15175-3c00-7000-8000-000202100000", "position": 10, "prompt": "Restore power and confirm normal operation", "readingFields": [], "answer": { "result": "Pass", "note": null, "readings": [], "answeredAt": "2026-10-19T14:56:03.250Z" } }
  ],
  "progress": { "answered": 10, "total": 10, "passCount": 8, "failCount": 1, "notApplicableCount": 1 },
  "startedAt": "2026-10-19T13:12:00.000Z",
  "completedAt": null,
  "updatedAt": "2026-10-19T14:56:03.250Z",
  "submittedAt": null,
  "technicianNotes": null,
  "version": 48377,
  "affordances": {
    "canEdit": true,
    "canSubmit": { "allowed": true, "blockingCode": null, "answered": 10, "total": 10 }
  }
}
```

Sending the same body again returns the inspection unchanged with `version` 48377. A body whose third answer is a `Fail` without a note, and whose reading has a third decimal:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more fields are invalid.",
  "status": 400,
  "instance": "/api/jobs/01a15415-3b40-79a4-859f-2cf627f64d02/inspection",
  "errors": {
    "answers[1].readings[0].value": ["Enter a value from -99999999.99 to 99999999.99 with at most two decimal places."],
    "answers[2].note": ["Describe the problem for a failed item."]
  },
  "code": "request.validation-failed",
  "correlationId": "c3a7e1d9-5b2f-4c86-9e0a-7d1f3b5c9a24",
  "traceId": "00-9a1b2c3d4e5f60718293a4b5c6d7e8f9-0a1b2c3d4e5f6071-00"
}
```

## Checklist templates

### GetChecklistTemplates: `GET /api/checklist-templates`

The create-job form's template list. The five templates are fixed reference data.

| Item | Value |
|---|---|
| Access | Administrator |
| Success | 200 `GetChecklistTemplatesResponse { items: ChecklistTemplateResponse[] }`, ordered by `name` |
| Errors | Only the errors every endpoint can return |

`ChecklistTemplateResponse`: `id` uuid, `name` string, `trade` Trade, `itemCount` int32.

```http
GET /api/checklist-templates HTTP/1.1
Host: localhost:5173
Authorization: Bearer {accessToken}
X-Correlation-ID: 7f2a9d14-6c3b-4e58-b0a7-1d9e2c4f8a36
```

```json
{
  "items": [
    { "id": "01a15175-3c00-7000-8000-000103000000", "name": "Boiler service", "trade": "Plumbing", "itemCount": 8 },
    { "id": "01a15175-3c00-7000-8000-000105000000", "name": "Electrical panel inspection", "trade": "Electrical", "itemCount": 8 },
    { "id": "01a15175-3c00-7000-8000-000104000000", "name": "Fire extinguisher inspection", "trade": "FireSafety", "itemCount": 7 },
    { "id": "01a15175-3c00-7000-8000-000101000000", "name": "General electrical inspection", "trade": "Electrical", "itemCount": 8 },
    { "id": "01a15175-3c00-7000-8000-000102000000", "name": "Rooftop HVAC service", "trade": "Hvac", "itemCount": 10 }
  ]
}
```

## Diagnostics

Route group `/api/client-errors`, policy Administrator for the whole group.

### ReportClientError: `POST /api/client-errors`

The web dashboard's error reporter sends important client errors to the API log. Browser code never writes log files.

| Item | Value |
|---|---|
| Access | Administrator. Only signed-in sessions report. |
| Rate limit | `client-errors`: 10 reports a minute per user, on top of the global limit. The web app also deduplicates and stops at 10 a minute. |
| Body limit | 8 KB: a body of 8,192 bytes reaches validation, one of 8,193 bytes gets 413 `request.too-large`. |
| Success | 202 Accepted with no body, no `Content-Type` and no `Location`. Nothing from the report is echoed. |
| Side effects | One Warning log event, `client-error.reported` (below). Nothing is written to the database. |
| Errors | 400, 413, 429 |
| Event | None |

Request body `ReportClientErrorRequest`:

| Member | Type | Required | Rules |
|---|---|---|---|
| `message` | string | yes | Up to 500 characters; multi-line |
| `source` | string | yes | Up to 200; single line. The web app sends `app.errorHandler`, `window.unhandledrejection` or `router.onError`. |
| `route` | string or null | no | Up to 200, matching `^/[A-Za-z0-9/_.~%-]*$`: the route path only. A value with a query string, a fragment or any other character is a 400 keyed `route`; the API never truncates it. |
| `correlationId` | string or null | no | The failed API call's correlation id, matching `^[A-Za-z0-9._-]{1,64}$` |
| `stack` | string or null | no | Up to 2000; multi-line |
| `occurredAt` | date-time | yes | When the error happened in the browser |
| `appVersion` | string or null | no | Matching `^[0-9A-Za-z.+-]{1,32}$`, for example `1.0.0` |

- Only these seven members are read. Any other member is ignored and never reaches the log, so a report cannot add properties to the event.
- Strings are trimmed. An empty `route`, `correlationId`, `appVersion` or `stack`, or one of white space only, counts as absent and is logged as null, like a member that is left out or sent as `null`.
- Every failure is a field error keyed by the member, and no message repeats the value sent. A failed report is not logged as `client-error.reported`.

**Scrubbing.** Before the event is written, `message`, `source` and `stack` pass through a scrubber that replaces each match below, applied in this order:

| Pattern | Matches | Replaced with |
|---|---|---|
| `eyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]*` | JWTs: access tokens and socket tickets | `[redacted]` |
| `(?i)bearer\s+[A-Za-z0-9._~+/=-]+` | Bearer header values, in any case | `[redacted]` |
| `whsec_[A-Za-z0-9+/=]+` | Webhook secrets | `[redacted]` |
| `[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}` | Email addresses | `[redacted]` |
| `(?<![A-Za-z0-9_-])[A-Za-z0-9_-]{43}(?![A-Za-z0-9_-])` | Refresh-token-shaped values: a run of exactly 43 letters, digits, `_` or `-`, including one that starts or ends with `-`; a longer run is left whole | `[redacted]` |
| `\?[^\s)]*` | Query strings: a `?` and everything after it up to white space or `)` | `?[redacted]` |

A token inside a URL's query string therefore leaves only `?[redacted]`, and scrubbing a scrubbed text changes nothing. `route`, `correlationId` and `appVersion` pass the same scrubber; their patterns already exclude spaces, `@` and `?`, so only a value shaped like a token or a secret changes. Ordinary error text, route paths with ids, UUIDs and version numbers pass unchanged.

**The logged event.** `client-error.reported`, event id 4010, level Warning, with these properties:

| Property | Content |
|---|---|
| `Source` | Always `web` |
| `UserId` | The reporting administrator's user id |
| `CorrelationId` | The report request's own correlation id, also returned in `X-Correlation-ID` |
| `ReportedCorrelationId` | The scrubbed `correlationId`, the id of the failed API call; null when absent |
| `ErrorSource` | The scrubbed `source` |
| `ErrorMessage` | The scrubbed `message` |
| `ErrorStack` | The scrubbed `stack`; null when absent |
| `Route` | The scrubbed `route`; null when absent |
| `OccurredAt` | `occurredAt` in UTC, truncated to milliseconds |
| `AppVersion` | The scrubbed `appVersion`; null when absent |

The request line of the [request log](logging.md) carries the method, path, status and duration as for every request, never the body. A report refused with 400, 401, 403, 413 or 429 writes no `client-error.reported` event.

Example, the report the dashboard sends when a component reads a property of `undefined`:

```http
POST /api/client-errors HTTP/1.1
Host: localhost:5173
Authorization: Bearer {accessToken}
Content-Type: application/json
X-Correlation-ID: 927ba8e3-24aa-4d0e-8ef1-d8c4203c37fe
```

```json
{
  "message": "Cannot read properties of undefined (reading '[redacted]')",
  "source": "app.errorHandler",
  "route": "/jobs/01a15488-9680-7989-92af-ed07fc34a890",
  "correlationId": null,
  "stack": "at JobDetailsCard.vue:42:18\n    at JobDetailPage.vue:118:9",
  "occurredAt": "2026-10-19T14:43:10.214Z",
  "appVersion": "1.0.0"
}
```

```http
HTTP/1.1 202 Accepted
Content-Length: 0
Cache-Control: no-store
X-Correlation-ID: 927ba8e3-24aa-4d0e-8ef1-d8c4203c37fe
```

The browser's own message names the property, `(reading 'number')`, and its stack starts with a line that repeats the message. Before sending, the dashboard replaces quoted text in `message` and `stack` with `[redacted]` from the first opening quote to the last closing quote of each kind of quote, across line breaks, so any text between two quoted segments of the same kind is removed too, and an apostrophe inside a word neither opens nor closes a segment. It sends the stack frames only. A response that cannot be read as JSON is reported as `A response could not be read as JSON`, a thrown string as `A string was thrown instead of an Error`, and any other thrown value that is not an `Error` as `A value was thrown instead of an Error` (`null` and `undefined` as `Unknown error`), each without a stack. These are client rules: the API accepts any report that meets the request rules above and scrubs it as described.

A `message` of `Request to /api/jobs?search=Harbor failed for dispatcher@fieldservice.example` is logged as `Request to /api/jobs?[redacted] failed for [redacted]`. A `route` with a query string, `/jobs?status=OnHold`, returns:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more fields are invalid.",
  "status": 400,
  "instance": "/api/client-errors",
  "errors": {
    "route": ["Send the route path only, starting with / and without a query string or fragment."]
  },
  "code": "request.validation-failed",
  "correlationId": "1d7e3a95-6c2b-4f80-b4e1-9a5c7d3f2e68",
  "traceId": "00-5a7c9e1b3d5f7a9c1e3b5d7f9a1c3e5b-7c9e1b3d5f7a9c1e-00"
}
```

## Infrastructure endpoints

### Health

| Endpoint | Checks | Responses |
|---|---|---|
| `GET /health/live` | The process is running; no dependency is checked. | 200 `text/plain` `Healthy` |
| `GET /health/ready` | The database is reachable. | 200 `text/plain` `Healthy`, or 503 `text/plain` `Unhealthy` |

Both accept GET and HEAD; any other method gets 405. They are anonymous, outside rate limiting and absent from the OpenAPI document, and the development proxy and web server never forward them. Bodies carry the status word only.

```http
GET /health/ready HTTP/1.1
Host: 127.0.0.1:5080
```

```http
HTTP/1.1 200 OK
Content-Type: text/plain
Cache-Control: no-store, no-cache
X-Correlation-ID: 4bf92f3577b34da6a3ce929d0e0e4736
X-Content-Type-Options: nosniff
Referrer-Policy: no-referrer
Content-Security-Policy: default-src 'none'; frame-ancestors 'none'

Healthy
```

### OpenAPI document: `GET /openapi/v1.json`

| Item | Value |
|---|---|
| Content | The machine-readable contract, produced at run time from endpoint metadata and typed results |
| Environments | Development and Testing only. Elsewhere the route is not mapped and returns 404. |
| Access | Anonymous |
| Rate limit | None |
| Success | 200 `application/json`, OpenAPI 3.1 |

The API serves no interactive explorer ([ADR 0017](decisions/0017-api-explorer-in-development.md)). To browse the API, open this document in an OpenAPI viewer, or read this reference.
