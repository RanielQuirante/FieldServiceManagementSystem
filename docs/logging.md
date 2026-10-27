# Logging

This document describes where each FieldService application writes its logs, how files are named, rolled and kept, which levels apply per environment, how one request, and one job change across the services, is traced through its correlation id, which named events the API and the notification service write, and what is never logged. The settings named here are listed with their defaults in [configuration.md](configuration.md); the file naming decision is recorded in [ADR 0012](decisions/0012-log-file-naming-and-retention.md).

## Destinations

| Application | Environment | Destination | Level |
|---|---|---|---|
| API | Development | `logs/api/` plus a text console | Debug |
| API | Testing | File sink off; text console. One test writes to a temporary folder and deletes it | Warning |
| API | Staging (Compose) | `/app/logs`, bind-mounted from the host's `logs/api/`; compact JSON console to Docker's `json-file` driver | Information |
| API | Production | Compact JSON to stdout for the platform's log collector, plus `/app/logs` on a volume where no collector exists | Information |
| Notification service | Development | `logs/notifications/` plus a readable console | `debug` |
| Notification service | Test | An in-memory destination; one smoke test writes to a temporary folder | Per test |
| Notification service | Staging (Compose) | `/app/logs`, bind-mounted from the host's `logs/notifications/`; JSON stdout to `json-file` | `info` |
| Notification service | Production | JSON stdout, plus a volume | `info` |
| Technician app | Debug | `logs/` inside the app's data folder | Debug |
| Technician app | Release | Same | Information |
| Web dashboard | All | No log files. The errors it reports reach the API's log as `client-error.reported` ([Client error reports](#client-error-reports)) | — |
| nginx | Staging (Compose) | JSON access log to stdout, without query strings or client addresses; errors to stderr at `crit` only ([Containers](#containers)) | — |
| PostgreSQL | Compose | Container stdout, with `log_min_error_statement=panic` and `log_error_verbosity=terse` ([Containers](#containers)) | Server default |
| PostgreSQL | Homebrew on macOS | `/opt/homebrew/var/log/postgresql@17.log` | Server default |
| PostgreSQL | The installer on Windows | The `log` folder of the data directory, `C:\Program Files\PostgreSQL\17\data\log` by default | Server default |

## File names, rotation and retention

| Application | File | Rotation | Retention |
|---|---|---|---|
| API | `logs/api/api-YYYYMMDD.log`; a file that reaches the size limit continues in `api-YYYYMMDD_001.log`, `api-YYYYMMDD_002.log` and so on | Daily, and at 50 MB | 14 files, the current one included |
| Notification service | `logs/notifications/notifications.YYYYMMDD.N.log`, where `N` counts the files of the day | Daily, and at 50 MB | 14 rotated files plus the active one; older files are removed, also across restarts |
| Technician app | `<app data folder>/logs/mobile-YYYYMMDD.log` | Daily, and at 5 MB | 7 files |
| Docker console logs (Compose) | Docker's `json-file` driver, per service | 10 MB per file | 3 files |
| PostgreSQL (Homebrew) | `/opt/homebrew/var/log/postgresql@17.log` | None | Truncate it by hand |
| Production | The platform's log collector for every console; `/app/logs` on a volume only where no collector exists | The collector's own; the files as above | 14 days in the collector, like the daily files, because three security events carry client addresses ([Redaction](#redaction), [deployment.md](deployment.md#production-guidance)) |

- Each application writes to its own folder, so one library's retention never deletes another application's files.
- The API's folder, file count and size limit come from the `Logs` settings: `Logs:Directory` (`../../logs/api` from the project folder on a developer machine, `/app/logs` in a container), `Logs:RetainedFileCountLimit` (14) and `Logs:FileSizeLimitBytes` (52428800). The notification service's folder comes from `LOG_DIR`; its rotation is fixed in code.
- `create-dev-env.mjs` creates `logs/api/` and `logs/notifications/`. The Compose log mounts do not create a missing host folder, so a missing folder fails fast instead of being created with the wrong owner.

**Worst-case disk use per installation**

| Source | Worst case |
|---|---|
| API | 14 × 50 MB = 700 MB |
| Notification service | 15 × 50 MB = 750 MB |
| Technician app, per device | 7 × 5 MB = 35 MB |
| Docker console, per service | 30 MB |

**Dates.** File names follow the process time zone: local time on a developer machine, UTC in containers. Time stamps inside every line are ISO 8601 in UTC: `@t` in the API's events and `time` in the notification service's lines.

## Containers

In the Compose stack ([deployment.md](deployment.md#running-the-staging-stack-with-compose)) each service writes to its console, which Docker's `json-file` driver keeps, and the API and the notification service also write files:

| Service | Files | Console |
|---|---|---|
| `api` | `/app/logs`, bind-mounted from the host's `logs/api/`: the same `api-YYYYMMDD.log` files, rotation and retention as on a developer machine | Compact JSON events |
| `notifications` | `/app/logs`, bind-mounted from the host's `logs/notifications/`: the same `notifications.YYYYMMDD.N.log` files | pino's JSON lines |
| `migrate` | None | The migrations it applied, as text |
| `web` | None | nginx's access log, one JSON object per line; errors at `crit` only |
| `db` | None | PostgreSQL's server log. The service starts PostgreSQL with `log_min_error_statement=panic` and `log_error_verbosity=terse`, which the first start's bootstrap server receives too, so an error is logged as its error line only, without the failing statement's text and without the `DETAIL`, `HINT`, `QUERY` and `CONTEXT` lines that would hold row values or a role password |

- **Console limits.** Every service's console goes to Docker's `json-file` driver with `max-size` 10 MB and `max-file` 3, so the console of one service never takes more than 30 MB. These logs belong to the container: `down` removes them with it, and the files under `logs/` stay.
- **Folders.** The bind mounts never create a missing host folder. `create-dev-env.mjs` creates `logs/api/` and `logs/notifications/`, and `up` stops with "bind source path does not exist" when one is missing.
- **Ownership.** On a Linux host, `logs/api/` must be writable by UID 1654, the API's user, and `logs/notifications/` by UID 1000, `node`. Docker Desktop on macOS and Windows maps ownership through its file sharing: the files the containers write belong to your own account on the host, and to the container's user inside the container.
- **Dates.** A container names its files by the UTC date, and a host process by the local date. Both write into the same folders, and a container continues a host file of the same date name, so the time stamps inside the lines tell the two apart. Run each service either on the host or in Compose, never both at once.
- **nginx access log.** Each line holds `time`, `method`, `path`, `status`, `bytes`, `durationSeconds` and `correlationId`. The path is nginx's normalized path without the query string, so a search such as `/api/technicians?search=…` is logged as `/api/technicians`, and a path served through the dashboard fallback as `/index.html`. No client address, header other than the correlation id, or body is logged.
- **nginx correlation id.** `correlationId` is the `X-Correlation-ID` response header of the API or the notification service when nginx proxied the request and the service returned one, and otherwise the request's `X-Correlation-ID` header. nginx writes either value only when it follows the [correlation id rule](#correlation-ids), 1 to 64 letters, digits, `.`, `_` and `-`, and an empty string otherwise. So a proxied API request carries the same id in nginx's line and in the API's request line, the 32-character trace id included when the API refused the caller's header; a request nginx answers itself, such as a file, the dashboard fallback or its own `404`, carries the caller's id when it is valid.
- **nginx error log.** nginx's error lines quote the whole request line, query string included, the client address and the referrer, so the error log is limited to the `crit` level, which normal operation never reaches. A failed upstream request still shows in the access log, with status `502`.

## Formats

- **API.** The file sink writes compact log event format (CLEF): one JSON object per line, with `@t` (time), `@mt` (message template), `@l` (level, omitted for Information) and the event's properties. The console writes text in Development and Testing, and CLEF when `Logs:ConsoleFormat` is `Json`.
- **Notification service.** pino writes one JSON object per line, with `level` as a number (20 debug, 30 info, 40 warn, 50 error, 60 fatal), `time`, the event's properties, and `msg`, which holds the event name. The console output is readable text through pino-pretty only when `LOG_PRETTY` is `true` and `NODE_ENV` is `development`; otherwise it is the same JSON.
- **Technician app.** CLEF, as the API.

Every format is plain JSON lines, so `grep`, `jq` or any log viewer that reads JSON lines can search the files.

## Levels

**API.** Levels come from `Serilog:MinimumLevel`, with namespace overrides, in the settings files:

| Environment | Default | Overrides |
|---|---|---|
| Development | Debug | `Microsoft`, `Microsoft.AspNetCore`, `Microsoft.EntityFrameworkCore` and `System.Net.Http` at Information, so the executed SQL commands appear without parameter values; `Microsoft.AspNetCore.Hosting.Diagnostics` at Warning |
| Testing | Warning | The base file's overrides |
| Staging and Production | Information | `Microsoft.AspNetCore`, `Microsoft.AspNetCore.Hosting.Diagnostics`, `Microsoft.EntityFrameworkCore` and `System.Net.Http` at Warning |

`Microsoft.AspNetCore.Hosting.Diagnostics` stays at Warning in every environment, because its "Request starting" and "Request finished" lines contain the query string. To change a level without editing a file, set an environment variable, for example `Serilog__MinimumLevel__Default=Debug` in the process or Compose environment.

**Notification service.** `LOG_LEVEL` sets one level for every target: `debug` in Development, `info` in Staging and Production. Change it in the `.env` file or the Compose environment.

**Technician app.** The level is fixed at compile time: Debug in Debug builds, Information in Release builds.

## API request log

The API writes one event per request when the response completes, with these properties:

| Property | Content |
|---|---|
| `RequestMethod` | The HTTP method |
| `RequestPath` | The path without its query string |
| `StatusCode` | The status sent, or the status the error handler chose for an exception |
| `Elapsed` | Milliseconds |
| `CorrelationId` | The request's correlation id |
| `UserId`, `Role` | The signed-in user's id and role, when the request is authenticated |

- Requests to `/health/...` are logged at Verbose, so health probes do not fill the files. Other requests are logged at Information, and responses with a 5xx status at Warning.
- The request event never carries headers, bodies or the query string.
- An unhandled exception is logged once, by the error handler, as the event `request.unhandled-exception` at Error, with the exception type, the method, the path, the correlation id and the stack trace. The request event for that request carries no exception.

## Correlation ids

- The API reads `X-Correlation-ID` from each request and accepts it only when it is 1 to 64 characters of letters, digits, `.`, `_` and `-`. Otherwise it uses the W3C trace id of the request, 32 hexadecimal characters.
- The id is attached to every event logged while the request runs, returned in the `X-Correlation-ID` response header, and included as `correlationId` in every Problem Details error body, next to `traceId`. An error response therefore carries the id that finds its log lines.
- Serilog also stamps the trace and span ids on each event.
- The technician app and the web dashboard send a new UUID in `X-Correlation-ID` with each request, so the API keeps the id the client chose. The technician app also writes it in its own `http.response` and `http.failure` lines.
- When a request changes a job, the job event that the API sends to the notification service carries the request's id, and the notification service logs it on every line of that event ([Following one job change across the services](#following-one-job-change-across-the-services)).

To follow one request, search both log folders for its id:

```
grep -r "<correlation id>" logs/
```

## API events

Besides the request log, the API writes named events. Each one carries the property `EventId`, with the event's number as `Id` and its name as `Name`; in a CLEF line it reads `"EventId":{"Id":4002,"Name":"auth.sign-in.failed"}`. To find every occurrence of one event:

```
grep -h '"Name":"auth.sign-in.failed"' logs/api/*.log
```

- Events written while a request runs also carry the request's `CorrelationId` and the trace and span ids. Startup events carry no request properties. The [real-time events](#real-time-events) are written outside requests and carry the `CorrelationId` of the request that made the change.
- `config.invalid` is logged at Critical, which Serilog writes as Fatal (`"@l":"Fatal"`).

### Startup and request events

| Event | Id | Level | Written when | Properties |
|---|---|---|---|---|
| `config.invalid` | 1001 | Fatal | At startup, once for each setting that breaks its rule ([Startup checks](configuration.md#startup-checks)), before the API stops. In Development and Staging also when the database has pending migrations | `Key`, the setting's name, and `Rule`, the rule it breaks. For pending migrations, `Key` is `ConnectionStrings:FieldService` and `Rule` names the migrations and the command that applies them. Never the value |
| `seed.demo-data.completed` | 3002 | Information | At startup, after the demo data was inserted into an empty database | `Users`, `Technicians`, `Jobs`, `HistoryEntries`, `Inspections`, `Answers` and `Readings`, the number of rows of each kind, and `NextJobNumber`, the number the next created job receives |
| `seed.demo-data.skipped` | 3001 | Information | At startup, when no demo data is inserted | `Reason`, one of `Seed:DemoData is false`, `the environment is Production`, `Seed:DemoPassword has fewer than 12 characters` and `demo data present` (the database already holds users) |
| `request.unhandled-exception` | 2001 | Error | An exception that neither the concurrency handler nor the bad-request handler answers; the response is 500 ([API request log](#api-request-log)) | `ExceptionType`, `RequestMethod`, `RequestPath` without the query string, `CorrelationId`, and the exception with its message and stack trace in `@x` |

Entity Framework's own Error and Fatal events are dropped, so a database exception is never logged twice.

### Security events

| Event | Id | Level | Written when | Properties |
|---|---|---|---|---|
| `auth.sign-in.succeeded` | 4001 | Information | A sign-in creates a session | `UserId`, `Role`, `Client`, `SessionId` |
| `auth.sign-in.failed` | 4002 | Warning | A sign-in is refused | `Reason`; `UserId`, only when the account exists; `Client`; `ClientIp` |
| `auth.refresh.grace-retry` | 4003 | Information | A refresh token used less than 30 seconds earlier is presented again while its replacement is still unused, and the API issues a new token instead of treating it as reuse | `UserId`, `SessionId`, `Client` |
| `auth.refresh.reuse-detected` | 4004 | Warning | A refresh token that was already used is presented again outside that grace, and the API revokes the session | `UserId`, `SessionId`, `Client`, `SecondsSinceConsumed`, `ClientIp` |
| `auth.refresh.rejected` | 4005 | Information | A refresh is refused for one of the reasons below. A refresh without any credential writes no event | `Reason`; `SessionId`, only when the token matched a session |
| `auth.sign-out` | 4006 | Information | A sign-out revokes a session. A missing or unknown credential, a credential of the other client and an already revoked session write no event | `UserId`, `SessionId` |
| `auth.socket-ticket.issued` | 4009 | Information | `POST /api/auth/socket-tickets` issues a socket ticket to an administrator whose session is still live. A refusal writes no event of this name: a 401 appears only in the request line, a 403 as `authz.forbidden` and a 429 as `rate-limit.rejected` | `UserId`, `TicketId` |
| `authz.forbidden` | 4007 | Information | An authorization policy refuses a signed-in user, and the response is 403. A request without a valid access token gets 401 and writes no event | `Policy`, `UserId`, `Role`, `RoutePattern` |
| `rate-limit.rejected` | 4008 | Warning | A rate limit refuses a request, and the response is 429. Written at the first refusal of a partition, then not again for that partition until its window ends | `Policy`, `PartitionKind`, `UserId` or `ClientIp`, `RoutePattern` |
| `client-error.reported` | 4010 | Warning | `POST /api/client-errors` accepts an error report from the web dashboard, and the response is 202. A refused report writes no event of this name ([Client error reports](#client-error-reports)) | `Source`, `UserId`, `CorrelationId`, `ErrorSource`, `Route`, `OccurredAt`, `AppVersion`, `ReportedCorrelationId`, `ErrorMessage`, `ErrorStack` |

| Property | Content |
|---|---|
| `UserId`, `SessionId` | The ids of the user and of the session |
| `TicketId` | The socket ticket's `jti`, a UUID. The notification service logs the same value as `ticketId` when it refuses that ticket |
| `Role` | `Administrator` or `Technician` |
| `Client` | `Web` for the web dashboard, `Mobile` for the technician app |
| `Reason` | A reason code from the table below |
| `SecondsSinceConsumed` | Whole seconds between the token's first use and its reuse |
| `ClientIp` | The client's IP address as the API sees it, after the forwarded headers of a trusted proxy apply; `unknown` when the connection has none |
| `Policy` | On `authz.forbidden`, the endpoint's authorization policies, separated by commas, or `fallback` when the endpoint names none. On `rate-limit.rejected`, `sign-in`, `refresh`, `socket-tickets`, `client-errors` or `global` |
| `PartitionKind` | `user` when the `global`, `socket-tickets` or `client-errors` limit counts a signed-in user's requests, logged with `UserId`; `ip` when a limit counts the requests of one address, logged with `ClientIp`. The other of the two properties is null |
| `RoutePattern` | The route template of the endpoint that matched, never the request path with its values or the query string. On `rate-limit.rejected` it is null when the request matched no endpoint |

| Event | Reason | Meaning |
|---|---|---|
| `auth.sign-in.failed` | `unknown-account` | No account has the email address; `UserId` is null |
| `auth.sign-in.failed` | `wrong-password` | The password does not match |
| `auth.sign-in.failed` | `inactive-account` | The account is deactivated |
| `auth.sign-in.failed` | `wrong-application` | The account's role does not match the client: the web dashboard accepts administrators only, the technician app technicians only |
| `auth.refresh.rejected` | `csrf-header-missing` | A refresh sent the `fs_refresh` cookie without the `X-FieldService-Csrf` header |
| `auth.refresh.rejected` | `unknown-token` | The token is malformed or matches no stored token |
| `auth.refresh.rejected` | `client-mismatch` | The token belongs to a session of the other client |
| `auth.refresh.rejected` | `session-revoked` | The session was signed out or revoked earlier |
| `auth.refresh.rejected` | `session-expired` | The session passed its idle or absolute expiry |
| `auth.refresh.rejected` | `user-inactive` | The user was deactivated; the API also revokes the session |

For each event the checks run in the order listed, and the first one that applies is logged.

**What the events carry.** The security, configuration and seeding events follow the rules of [Redaction](#redaction). Apart from `client-error.reported`, they carry identifiers only: user, session and ticket ids, role and client names, reason codes, counts, policy names, route templates and setting names. None of them carries a password, a token, a ticket or its hash, a cookie, a header, a body, a query string, an email address, a name, a phone number or a postal address. `client-error.reported` carries the same kind of identifiers plus the text of a browser error report, which the API scrubs before it writes the event ([Client error reports](#client-error-reports)). `ClientIp` appears only on `auth.sign-in.failed`, `auth.refresh.reuse-detected` and `rate-limit.rejected`, and `config.invalid` names the key and the rule, never the value.

### Client error reports

The web dashboard writes no log files and never writes to the server's log folders. When it meets an error that no page handles (one caught by the Vue error handler, an unhandled promise rejection or an error during navigation), it shows an error toast and, while an administrator is signed in, sends a report to `POST /api/client-errors` ([api.md](api.md#reportclienterror-post-apiclient-errors)). The API writes each accepted report as one `client-error.reported` event, with the `SourceContext` `FieldService.Api.Features.Diagnostics.ReportClientError.ReportClientErrorEndpoint`, through its usual sinks ([Destinations](#destinations)): on a developer machine the file `logs/api/api-YYYYMMDD.log` and the console; in Staging `/app/logs`, bind-mounted from the host's `logs/api/`, and the JSON console. The events follow the API's file rotation and 14-file retention.

| Property | Content |
|---|---|
| `Source` | Always `web`, the application that reported the error |
| `UserId` | The id of the administrator whose session sent the report |
| `CorrelationId` | The report request's own correlation id, as on every event of a request; the API also returns it in the 202 response's `X-Correlation-ID` header |
| `ErrorSource` | Where the dashboard caught the error, scrubbed; the dashboard sends `app.errorHandler`, `window.unhandledrejection` or `router.onError` |
| `Route` | The dashboard's route path when the error occurred, without a query string or fragment, scrubbed; null when the report has none |
| `OccurredAt` | When the error occurred, by the browser's clock, in UTC truncated to milliseconds; CLEF writes it in ISO 8601 with `+00:00`. `@t` is when the API wrote the event |
| `AppVersion` | The dashboard's version, scrubbed; null when the report has none |
| `ReportedCorrelationId` | The correlation id of the failed API call behind the error, scrubbed; null when the error did not come from an API call |
| `ErrorMessage` | The error message, scrubbed |
| `ErrorStack` | The stack trace, scrubbed; null when the report has none |

Every text is trimmed, and a `route`, `correlationId`, `stack` or `appVersion` that is empty after trimming is logged as null.

**Line breaks.** `ErrorMessage` and `ErrorStack` can hold line breaks. The log files and the JSON console write them as `\n` and `\r`, so each report stays one line there. The text console (`Logs:ConsoleFormat` `Text`, used in Development and Testing) prints them as line breaks, so one report can span several console lines. The message template puts `ErrorMessage` and `ErrorStack` last: the event's first console line starts with the time, the level and the identifiers, and any further lines are report text, which can look like other log lines. Read reports in the log files when the line boundaries matter.

**Scrubbing.** Before the event is written, the API runs each of the six text members, `message`, `source`, `stack`, `route`, `correlationId` and `appVersion`, through one scrubber. It replaces these values, in this order:

| Value | Written as |
|---|---|
| JWT-shaped strings, such as access tokens and socket tickets: dot-separated base64url segments that start with `eyJ` | `[redacted]` |
| Bearer values: the word `bearer` in any case, white space and the value after it | `[redacted]` |
| Webhook secrets: `whsec_` and the value after it | `[redacted]` |
| Email addresses | `[redacted]` |
| Refresh-token-shaped values: a separate run of 43 letters, digits, `_` or `-` | `[redacted]` |
| Query strings: a `?` and everything after it up to white space or `)` | `?[redacted]` |

The exact patterns are listed in [api.md](api.md#reportclienterror-post-apiclient-errors). A message of the form `Request to /api/jobs?<query> failed for <email address>`, with a real query and address, is logged as `Request to /api/jobs?[redacted] failed for [redacted]`. Ordinary error text, route paths with ids, UUIDs and version numbers pass unchanged. Before it sends a report, the dashboard strips the same kinds of values and applies rules of its own: it describes a failed API call by its status and error code only, never by the rest of the response, and puts no form value or other user-entered text into a report; it replaces quoted text in a message or stack with `[redacted]`; it sends a stack as its frames only, without the lines that repeat the message; and it sends a fixed message with no stack for a JSON parse failure, such as an API response that cannot be read, and for a thrown value that is not an `Error`. The API scrubs again because it does not rely on the client, and its scrubber finds only the shaped values in the table above: a name, a phone number or a postal address written in free text has no such shape, so the API's scrubber cannot find it.

**What is never logged.**

- Nothing in the report reaches the log unscrubbed: the API reads only the seven members of the request contract (`message`, `source`, `route`, `correlationId`, `stack`, `occurredAt` and `appVersion`), writes `occurredAt` as a parsed time, and writes the six text members only after trimming and scrubbing, as the properties above.
- A member that the contract does not define is ignored and never logged, at any level. The request body as sent and its headers are never logged.
- The 202 response has no body and echoes nothing back.
- The report's request line carries the method, the path, the status and the duration, as for every request ([API request log](#api-request-log)).

**Refused reports.** A refused report writes no `client-error.reported` event. A 400 (a member breaks its rule) and a 413 (a body over 8 KB) appear only in the request line, as does a 401; a 403, such as a technician's request, appears as `authz.forbidden`; and a 429 appears as `rate-limit.rejected` with the `Policy` `client-errors`, the `PartitionKind` `user` and the `UserId`. The `client-errors` limit counts 10 reports a minute per user, invalid reports included.

**Finding a browser error by its reference.** The dashboard's error toast shows "Reference" and an id, which is one of two:

- When the error came from a failed API call, the toast shows that call's correlation id at once. In the API log it is the `CorrelationId` of that call's request line and events, and the `ReportedCorrelationId` of the `client-error.reported` event when the dashboard sent a report.
- Otherwise, the toast shows the report's own `CorrelationId`, and only after the API answered 202, so that event is in the log.

When a report of the second kind is not sent or not accepted (no administrator is signed in, the dashboard's own limit of 10 a minute is reached, or the report fails), the toast shows no reference, so a toast never shows a reference that the log does not hold. A repeat of an error the dashboard has already reported adds no toast and no report.

One search finds either kind, because it matches the id in any property:

```
grep -h "<reference>" logs/api/*.log
```

The result holds the `client-error.reported` line when a report was accepted and, for a failed API call, that call's request line and events. To list every report, or the reports of one administrator:

```
grep -h '"Name":"client-error.reported"' logs/api/*.log
grep -h '"Name":"client-error.reported"' logs/api/*.log | grep '"UserId":"<user id>"'
```

To find the reports refused by the limit:

```
grep -h '"Name":"rate-limit.rejected"' logs/api/*.log | grep '"Policy":"client-errors"'
```

### Real-time events

After a create, an assignment or a status change is committed, the API queues a job event, and a background dispatcher posts it to the notification service ([events.md](contracts/events.md)). The dispatcher writes these events. They appear only when `Notifications:BaseUrl` is set; without it, the API queues no events and writes none of them.

| Event | Id | Level | Written when | Properties |
|---|---|---|---|---|
| `job-event.dispatched` | 5001 | Information | The notification service accepted the event with a 2xx status | `JobEventId`, `Type`, `JobId`, `CorrelationId`, `StatusCode` |
| `job-event.dispatch-failed` | 5002 | Warning | The event was not delivered, for one of the reasons below. The job change itself stays saved | `JobEventId`, `Type`, `JobId`, `CorrelationId`, `Reason`; `StatusCode`, null unless the service answered |
| `job-event.dropped` | 5003 | Warning | The event left the queue without a delivery attempt | `JobEventId`, `Type`, `JobId`, `CorrelationId`, `Reason` |
| `job-event.drain-incomplete` | 5004 | Warning | At shutdown, the 5-second drain ended with events still queued | `Remaining`, the number of events not delivered |

| Property | Content |
|---|---|
| `JobEventId` | The event's id, also sent as the `webhook-id` header; the notification service logs it as `eventId`. The API names it `JobEventId` because every API event already carries an `EventId` |
| `Type` | `job.created`, `job.assigned` or `job.status-changed` |
| `JobId` | The id of the job that changed |
| `CorrelationId` | The id of the request that made the change |
| `StatusCode` | The HTTP status of the notification service's last answer |

| Event | Reason | Meaning |
|---|---|---|
| `job-event.dispatch-failed` | `rejected` | The service answered with a status that a retry cannot change, such as 400, 401, 404, 413 or 415, so the event was not retried |
| `job-event.dispatch-failed` | `unavailable` | The service still answered 408, 429 or a 5xx status after the retries |
| `job-event.dispatch-failed` | `timeout` | The attempts timed out, or the 10-second budget ran out |
| `job-event.dispatch-failed` | `circuit-open` | The circuit breaker was open after earlier failures, so no attempt was made |
| `job-event.dispatch-failed` | `unreachable` | No connection could be made, for example because the service is not running |
| `job-event.dispatch-failed` | `shutdown` | The API stopped before the delivery finished |
| `job-event.dispatch-failed` | `failed` | Any other error |
| `job-event.dropped` | `queue-full` | The queue already held 1,000 events, so its oldest event was dropped to make room |
| `job-event.dropped` | `queue-stopped` | A change was committed after the API began to shut down |

- Each event is signed once and every retry sends the same bytes, so a retried delivery is logged once, as `job-event.dispatched` or `job-event.dispatch-failed`.
- The resilience pipeline behind the retries writes its own entries, with a `SourceContext` that starts with `Polly`, for retried attempts, timeouts and circuit-breaker changes. They carry the event's `CorrelationId`, and no header or body.
- The API's client for the notification service writes no HTTP client log entries, so the signature header never reaches a log, whatever the level.

## Notification service events

The notification service writes each named event as the `msg` of a line, with its properties as members of the same line ([Formats](#formats)). To find every occurrence of one event:

```
grep -h '"msg":"job-event.rejected"' logs/notifications/*.log
```

### Service events

| Event | Level | When |
|---|---|---|
| `server.listening` | info | The service accepts connections; with `host` and `port` |
| `server.stopping` | info | A stop signal arrived; with `signal` |
| `server.stopped` | info | The server closed |
| `server.stop-timeout` | error | The server did not close within 10 seconds |
| `server.listen-failed` | fatal | The service could not listen; with `err`, the error's type, message, stack trace and code only, or `errType` when the failure value is not an Error. The process exits with code 1 |
| `request.failed` | error | A request failed with an unexpected error, and the response is 500; with `err`, the error's type, message, stack trace and code only, or, when the failure value is not an Error, only `errType` |
| `process.uncaught-exception`, `process.unhandled-rejection` | fatal | A process failure; with `err`, the error's type, message, stack trace and code only, or, when the failure value is not an Error, only `errType`. The process exits with code 1 |

An Error is logged as `err` with four fields only: `type`, `message`, `stack` and, when it is a string or a number, `code`. Any further property of the error is never written. A chain of causes appears only inside `message` and `stack`, as `: <cause message>` and `caused by: <cause stack>`. A failure value that is not an Error is never logged as given: the line carries only `errType`, its type, which is `string`, `number`, `bigint`, `boolean`, `symbol`, `undefined`, `function`, `object`, `null` or `array`.

An invalid configuration is reported before the logger exists, as one line on stderr: `config.invalid: <variable> <rule>` ([configuration.md](configuration.md#notification-service-srcfieldservicenotifications)).

### Job events

The service writes these for each `POST /internal/job-events` from the API. The checks run in the order of the reason table below, and the first that fails answers the request.

| Event | Level | When | Properties |
|---|---|---|---|
| `job-event.received` | info | The event passed the content type, size, timestamp, signature and schema checks, and its `id` equals its `webhook-id` | `eventId`; `correlationId`, from the envelope; `type`; `version`; `jobId`, absent for an unknown type |
| `job-event.duplicate` | info | The same `webhook-id` was accepted before, within the retention window. The response is 202 with the outcome `duplicate`, and nothing is broadcast | As `job-event.received` |
| `job-event.ignored` | warn | The type or the version is unknown. The response is 202 with the outcome `ignored` | As `job-event.received` |
| `job-event.broadcast` | info | The event was sent to the `administrators` room. The response is 202 with the outcome `broadcast` | As `job-event.received`, plus `room`, which is `administrators`, and `recipients`, the number of administrator sockets connected at that moment |
| `job-event.rejected` | error | A check failed. The response is the status below, as Problem Details | `reason`; `status`; `eventId`, the claimed `webhook-id`, only when it is a lower-case UUID; `correlationId`, from the `X-Correlation-ID` header, only when that is valid |

| Reason | Status | Meaning |
|---|---|---|
| `unsupported-media-type` | 415 | The content type is not `application/json` |
| `too-large` | 413 | The body is larger than 16 KB |
| `missing-headers` | 401 | `webhook-id`, `webhook-timestamp` or `webhook-signature` is absent or empty |
| `timestamp-out-of-range` | 401 | `webhook-timestamp` is not 1 to 12 digits, or is further from the service's clock than `WEBHOOK_TOLERANCE_SECONDS` |
| `signature-mismatch` | 401 | No `v1` signature matches under any entry of `WEBHOOK_SECRETS`, or the signature header is longer than 1,024 characters |
| `invalid-json` | 400 | The body is not valid UTF-8 JSON, or it starts with a byte order mark |
| `schema` | 400 | The envelope or its data does not match the event contract |
| `id-mismatch` | 400 | The envelope's `id` differs from `webhook-id` |

- A request to another path, or with another method, gets 404 or 405 and writes no `job-event` line.
- When the request carries a valid `X-Correlation-ID`, the service returns it in the `X-Correlation-ID` response header of every 202 and every problem response, and in the problem body as `correlationId`.
- The body, the signature header and the secrets are never logged.

### Socket events

| Event | Level | When | Properties |
|---|---|---|---|
| `socket.connected` | info | An administrator's socket passed the ticket check and joined the `administrators` room | `socketId`, `userId` |
| `socket.disconnected` | info | A connected socket closed | `socketId`, `userId`, `reason`, and `durationMs`, the time since it connected |
| `socket.rejected` | warn | A connection was refused, for one of the reasons below | `reason`; `code`, the code the client receives; `socketId`; `userId` and `ticketId`, the ticket's `jti`, once the ticket has been read far enough to know them |
| `socket.rejected` | error | The ticket check failed with an unexpected error; the client receives `ticket_invalid` | `reason`, which is `verification-error`; `code`; `socketId`; `err`, the error's type, message, stack trace and code only, or `errType` when the failure value is not an Error |

| Reason | Code the client receives | Meaning |
|---|---|---|
| `origin-not-allowed` | None: the WebSocket upgrade is refused before a socket exists | The browser's `Origin` is not listed in `SOCKET_ALLOWED_ORIGINS`. The line has no `code` and no `socketId`, and the refused origin is not logged |
| `missing` | `ticket_missing` | The handshake carries no ticket |
| `signature` | `ticket_invalid` | The signature does not verify under `SOCKET_TICKET_KEY` |
| `expired` | `ticket_invalid` | The ticket has expired, or was issued more than 60 seconds ago; 5 seconds of clock difference are allowed |
| `claims` | `ticket_invalid` | The issuer or the audience differs, a required claim is missing, or the ticket is not valid yet |
| `algorithm` | `ticket_invalid` | The ticket is not signed with HS256 |
| `malformed` | `ticket_invalid` | The ticket is not a well-formed signed token |
| `subject` | `ticket_invalid` | The `sub` claim is not a UUID |
| `ticket-id` | `ticket_invalid` | The `jti` claim is missing, empty or longer than 128 characters |
| `role` | `forbidden` | The ticket's role is not `Administrator` |
| `reused` | `ticket_invalid` | The ticket was used before; each ticket opens one connection |
| `ticket-cache-full` | `ticket_invalid` | The record of used ticket ids is full of ids that are still needed |

Common `reason` values of `socket.disconnected`, as Socket.IO reports them:

| Reason | Meaning |
|---|---|
| `client namespace disconnect` | The dashboard closed the connection, for example at sign-out |
| `transport close` | The tab closed, or the network connection was lost |
| `ping timeout` | The client stopped answering the heartbeat |
| `server namespace disconnect` | The service ended the connection after `SOCKET_SESSION_MAX_SECONDS`, 900 seconds by default; the dashboard reconnects with a new ticket |
| `server shutting down` | The service stopped |

## Following one job change across the services

One correlation id follows a job change from the request that made it to the broadcast that tells the dashboards:

1. The technician app sends the request, for example `PUT /api/jobs/{jobId}/status`, with a new UUID in `X-Correlation-ID`, and writes the id in its own `http.response` line.
2. The API takes the id as the request's `CorrelationId`. Its request line, and every event written during the request, carry it.
3. After the change is committed, the API queues a job event whose `correlationId` is that id. The dispatcher posts the event with the id in `X-Correlation-ID`, and writes `job-event.dispatched`, or `job-event.dispatch-failed`, with the same `CorrelationId` and the event's `JobEventId`.
4. The notification service writes `job-event.received` and `job-event.broadcast` with the same `correlationId`, and with `eventId` equal to the API's `JobEventId`. A repeated delivery of the same event writes `job-event.duplicate` instead of a second broadcast.
5. The dashboards receive the event without the correlation id, because the socket payload carries ids only, and refetch the job. Those requests carry ids of their own.

To check one change, find its id in the API's delivery lines, which also name the job:

```
grep -h '"Name":"job-event.dispatched"' logs/api/*.log
```

Then search both folders for that id:

```
grep -h "<correlation id>" logs/api/*.log logs/notifications/*.log
```

The result holds the API's request line and the events of that request, `job-event.dispatched`, and the notification service's `job-event.received` and `job-event.broadcast`, each with the same id. Both services write UTC time stamps, `@t` in the API's lines and `time` in the notification service's, so the lines can be put in order across the two files.

## Redaction

Rules for every application:

- Never log passwords, tokens, socket tickets, signatures, `Authorization` or `Cookie` headers, request or response bodies, query strings, email addresses, names, phone numbers or postal addresses. Log identifiers only.
- A failed sign-in logs the user's id when the account exists, and "unknown account" otherwise.
- The client IP address may appear only on three security events, `auth.sign-in.failed`, `auth.refresh.reuse-detected` and `rate-limit.rejected`, never in the request log, and it follows the same 14-file retention.
- Configuration errors name the setting and the rule, never the value.

How the applications keep to them:

- Entity Framework sensitive-data logging is off everywhere, and the API refuses to start when its connection string enables Npgsql's `Include Error Detail`.
- The API's request log leaves out the query string, and the framework's own request lines, which contain it, stay below the configured level.
- The API never logs the message of a request-binding failure, because that message quotes the submitted value.
- The API's client for the notification service writes no HTTP client log entries, so the `webhook-signature` header is never logged, whatever the level. The `job-event.*` events carry ids only, and `auth.socket-ticket.issued` carries the ticket's id, never the ticket.
- The API writes a browser error report as `client-error.reported` only after trimming and scrubbing each of its text members: token-shaped values, webhook secrets and email addresses become `[redacted]`, and query strings `?[redacted]`. A member outside the request contract, the body as sent and its headers are never logged. The web dashboard writes no logs of its own, never sends form values, and strips the same kinds of values before it sends a report ([Client error reports](#client-error-reports)).
- The notification service's logger replaces the `authorization`, `cookie` and `webhook-signature` headers, the socket handshake's `auth` object, and any `auth`, `ticket`, `token`, `password`, `secret`, `signature`, `webhookSecrets` or `socketTicketKey` member, at the top level of a line or one level down, with `[redacted]`.
- The notification service writes an Error with `type`, `message`, `stack` and a string or number `code` only, so properties attached to an error never reach a log, and writes a failure value that is not an Error as `errType` only ([Service events](#service-events)).
- The notification service's job-event and socket lines carry ids and reason codes only. The event body, the signature, the ticket and a refused origin are never logged, and a ticket verification error, whose details can quote the ticket's claims, becomes a reason code instead of a logged error.

## Finding and reading logs

| Need | Command |
|---|---|
| Follow today's API log on macOS | `tail -f logs/api/api-$(date +%Y%m%d).log` |
| Follow today's API log on Windows | `Get-Content logs\api\api-$(Get-Date -Format yyyyMMdd).log -Wait` |
| Follow the notification service's newest file | `ls -t logs/notifications/ \| head -1`, then `tail -f` on that file |
| One request across the services | `grep -r "<correlation id>" logs/` |
| One job change across the services | `grep -h '"Name":"job-event.dispatched"' logs/api/*.log` for the ids, then `grep -h "<correlation id>" logs/api/*.log logs/notifications/*.log` ([Following one job change across the services](#following-one-job-change-across-the-services)) |
| Job events the API could not deliver | `grep -h '"Name":"job-event.dispatch-failed"' logs/api/*.log` |
| Errors the web dashboard reported | `grep -h '"Name":"client-error.reported"' logs/api/*.log` |
| One dashboard error by its toast reference | `grep -h "<reference>" logs/api/*.log` ([Client error reports](#client-error-reports)) |
| Events and sockets the notification service refused | `grep -h -E '"msg":"(job-event\|socket)\.rejected"' logs/notifications/*.log` |
| Change the API level without editing files | `Serilog__MinimumLevel__Default=Debug` in the process or Compose environment |
| Change the notification service's level | `LOG_LEVEL=debug` in its `.env` file or the Compose environment |
| Container console | `docker compose -f infrastructure/docker-compose.yml --profile full logs -f api`; name `notifications`, `web`, `migrate` or `db` for the others |
| nginx access log of one request | `docker compose -f infrastructure/docker-compose.yml --profile full logs --no-log-prefix web \| grep "<correlation id>"` |
| Why the API container does not start | `docker compose -f infrastructure/docker-compose.yml --profile full logs api \| grep config.invalid` |
| Technician app on Android (Debug builds) | `adb shell run-as io.github.ranielquirante.fieldservice.tech ls files/logs`, then `adb exec-out run-as io.github.ranielquirante.fieldservice.tech cat files/logs/mobile-YYYYMMDD.log > mobile.log` |
| Technician app on Windows | The `Data\logs` folder of the unpackaged app under `%LOCALAPPDATA%` |
| Empty the local log folders | Delete the files inside `logs/api/` and `logs/notifications/`, and keep the folders |

## Logs and the repository

Only `logs/.gitkeep` is tracked; `.gitignore` excludes `logs/*` and every `*.log` file, and `check-tracked-files.mjs` fails if a log file is ever tracked. Diagnostics uploaded by CI hold only throwaway credentials and fictional data.
