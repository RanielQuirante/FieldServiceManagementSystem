# Logging

This document describes where each FieldService application writes its logs, how files are named, rolled and kept, which levels apply per environment, how one request is traced through its correlation id, which named events the API and the notification service write, and what is never logged. The settings named here are listed with their defaults in [configuration.md](configuration.md); the file naming decision is recorded in [ADR 0012](decisions/0012-log-file-naming-and-retention.md).

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
| Web dashboard | All | No log files | — |
| nginx | Staging (Compose) | JSON access log to stdout, without query strings or client addresses; errors to stderr at `warn` | — |
| PostgreSQL | Compose | Container stdout | Server default |
| PostgreSQL | Homebrew on macOS | `/opt/homebrew/var/log/postgresql@17.log` | Server default |

## File names, rotation and retention

| Application | File | Rotation | Retention |
|---|---|---|---|
| API | `logs/api/api-YYYYMMDD.log`; a file that reaches the size limit continues in `api-YYYYMMDD_001.log`, `api-YYYYMMDD_002.log` and so on | Daily, and at 50 MB | 14 files, the current one included |
| Notification service | `logs/notifications/notifications.YYYYMMDD.N.log`, where `N` counts the files of the day | Daily, and at 50 MB | 14 rotated files plus the active one; older files are removed, also across restarts |
| Technician app | `<app data folder>/logs/mobile-YYYYMMDD.log` | Daily, and at 5 MB | 7 files |
| Docker console logs (Compose) | Docker's `json-file` driver, per service | 10 MB per file | 3 files |
| PostgreSQL (Homebrew) | `/opt/homebrew/var/log/postgresql@17.log` | None | Truncate it by hand |

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

## Formats

- **API.** The file sink writes compact log event format (CLEF): one JSON object per line, with `@t` (time), `@mt` (message template), `@l` (level, omitted for Information) and the event's properties. The console writes text in Development and Testing, and CLEF when `Logs:ConsoleFormat` is `Json`.
- **Notification service.** pino writes one JSON object per line, with `level`, `time` and `msg`. The console output is readable text through pino-pretty only when `LOG_PRETTY` is `true` and `NODE_ENV` is `development`; otherwise it is the same JSON.
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

To follow one request, search both log folders for its id:

```
grep -r "<correlation id>" logs/
```

## API events

Besides the request log, the API writes named events. Each one carries the property `EventId`, with the event's number as `Id` and its name as `Name`; in a CLEF line it reads `"EventId":{"Id":4002,"Name":"auth.sign-in.failed"}`. To find every occurrence of one event:

```
grep -h '"Name":"auth.sign-in.failed"' logs/api/*.log
```

- Events written while a request runs also carry the request's `CorrelationId` and the trace and span ids. Startup events carry no request properties.
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
| `authz.forbidden` | 4007 | Information | An authorization policy refuses a signed-in user, and the response is 403. A request without a valid access token gets 401 and writes no event | `Policy`, `UserId`, `Role`, `RoutePattern` |
| `rate-limit.rejected` | 4008 | Warning | A rate limit refuses a request, and the response is 429. Written at the first refusal of a partition, then not again for that partition until its window ends | `Policy`, `PartitionKind`, `UserId` or `ClientIp`, `RoutePattern` |

| Property | Content |
|---|---|
| `UserId`, `SessionId` | The ids of the user and of the session |
| `Role` | `Administrator` or `Technician` |
| `Client` | `Web` for the web dashboard, `Mobile` for the technician app |
| `Reason` | A reason code from the table below |
| `SecondsSinceConsumed` | Whole seconds between the token's first use and its reuse |
| `ClientIp` | The client's IP address as the API sees it, after the forwarded headers of a trusted proxy apply; `unknown` when the connection has none |
| `Policy` | On `authz.forbidden`, the endpoint's authorization policies, separated by commas, or `fallback` when the endpoint names none. On `rate-limit.rejected`, `sign-in`, `refresh` or `global` |
| `PartitionKind` | `user` when the `global` limit counts a signed-in user's requests, logged with `UserId`; `ip` when a limit counts the requests of one address, logged with `ClientIp`. The other of the two properties is null |
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

**What the events carry.** The security, configuration and seeding events follow the rules of [Redaction](#redaction) and carry identifiers only: user and session ids, role and client names, reason codes, counts, policy names, route templates and setting names. None of them carries a password, a token or its hash, a cookie, a header, a body, a query string, an email address, a name, a phone number or a postal address. `ClientIp` appears only on `auth.sign-in.failed`, `auth.refresh.reuse-detected` and `rate-limit.rejected`, and `config.invalid` names the key and the rule, never the value.

## Notification service events

The notification service logs named events as the `msg` of each line:

| Event | Level | When |
|---|---|---|
| `server.listening` | info | The service accepts connections; with `host` and `port` |
| `server.stopping` | info | A stop signal arrived; with `signal` |
| `server.stopped` | info | The server closed |
| `server.stop-timeout` | error | The server did not close within 10 seconds |
| `server.listen-failed` | fatal | The service could not listen |
| `process.uncaught-exception`, `process.unhandled-rejection` | fatal | A process failure, with its stack trace |

An invalid configuration is reported before the logger exists, as one line on stderr: `config.invalid: <variable> <rule>`.

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
- The notification service's logger replaces the `authorization`, `cookie` and `webhook-signature` headers, the socket handshake's credentials and any `ticket`, `token`, `password`, `secret` or `signature` field with `[redacted]`.

## Finding and reading logs

| Need | Command |
|---|---|
| Follow today's API log on macOS | `tail -f logs/api/api-$(date +%Y%m%d).log` |
| Follow today's API log on Windows | `Get-Content logs\api\api-$(Get-Date -Format yyyyMMdd).log -Wait` |
| Follow the notification service's newest file | `ls -t logs/notifications/ \| head -1`, then `tail -f` on that file |
| One request across the services | `grep -r "<correlation id>" logs/` |
| Change the API level without editing files | `Serilog__MinimumLevel__Default=Debug` in the process or Compose environment |
| Change the notification service's level | `LOG_LEVEL=debug` in its `.env` file or the Compose environment |
| Container console | `docker compose -f infrastructure/docker-compose.yml --profile full logs -f api` |
| Technician app on Android (Debug builds) | `adb shell run-as io.github.ranielquirante.fieldservice.tech ls files/logs`, then `adb exec-out run-as io.github.ranielquirante.fieldservice.tech cat files/logs/mobile-YYYYMMDD.log > mobile.log` |
| Technician app on Windows | The `Data\logs` folder of the unpackaged app under `%LOCALAPPDATA%` |
| Empty the local log folders | Delete the files inside `logs/api/` and `logs/notifications/`, and keep the folders |

## Logs and the repository

Only `logs/.gitkeep` is tracked; `.gitignore` excludes `logs/*` and every `*.log` file, and `check-tracked-files.mjs` fails if a log file is ever tracked. Diagnostics uploaded by CI hold only throwaway credentials and fictional data.
