# Testing

FieldService is tested at four levels: unit tests and integration tests inside each application, contract tests that hold the applications to their shared contracts, and end-to-end tests of the dashboard in a browser. This page lists every suite, what it covers, how to run it on a workstation and how the CI workflow runs it.

Setting up the workstation, including the local database and the local secrets, is described in [development.md](development.md); its [Tests](development.md#tests) section has the short form of the commands below. Run every command from the repository root unless a step says otherwise.

## Suites

| Suite | Folder | Runner | Needs | CI job |
|---|---|---|---|---|
| API | `tests/FieldService.Api.Tests` | xUnit v3 on Microsoft.Testing.Platform | PostgreSQL 17, `psql` 15 or later on `PATH`, and the test settings below | `api` |
| Technician app | `tests/FieldService.Mobile.Tests` | xUnit v3 on Microsoft.Testing.Platform | The .NET MAUI Android workload; no device, emulator, JDK or Android SDK | `mobile-android` |
| Notification service | `tests/FieldService.Notifications.Tests` | Node's test runner (`node --test`) | Node 26.12.0, with both packages installed | `notifications` |
| Web dashboard, unit | `src/FieldService.Web/src/**/__tests__/*.spec.ts` | Vitest with jsdom | Node 26.12.0 | `web` |
| Web dashboard, end to end | `src/FieldService.Web/e2e` | Playwright with Chromium | A running stack: the API, the notification service and the dashboard | None yet |

Before the first run, restore the local tools and install the three npm packages from their lock files:

```
dotnet tool restore
npm ci --ignore-scripts --prefix src/FieldService.Notifications
npm ci --ignore-scripts --prefix tests/FieldService.Notifications.Tests
npm ci --ignore-scripts --prefix src/FieldService.Web
```

The API tests also need the local database and the test settings that `create-dev-env.mjs` writes; see [step 2 of the first-time setup](development.md#2-create-the-local-environment-and-bootstrap-the-database).

## API

### What the tests need

- **PostgreSQL 17**, reached with a superuser connection: the local server on 127.0.0.1:5432 on a workstation, and the Compose `db` service on 127.0.0.1:5433 in CI. The tests stop with a clear message on any other major version.
- **`psql` 15 or later on `PATH`.** The fixture runs `infrastructure/postgres/bootstrap.sql` through it.
- **Three settings**, read from the user-secrets store `fieldservice-api-tests`. An environment variable with the same name takes precedence over the stored value.

| Setting | Content |
|---|---|
| `FIELDSERVICE_TEST_ADMIN_CONNECTION` | A superuser connection string to the `postgres` database |
| `FIELDSERVICE_MIGRATOR_PASSWORD` | The password of the `fieldservice_migrator` role, at least 16 characters |
| `FIELDSERVICE_APP_PASSWORD` | The password of the `fieldservice_app` role, at least 16 characters |

`create-dev-env.mjs` writes all three into the store, so a workstation set up as [development.md](development.md) describes needs no exported variable. Database roles belong to the whole server, and each test run applies these two passwords to them, so they must be the same values the development stores hold; the script writes the same values to both.

To take the admin connection from the environment instead, export it before the run. On macOS, with Homebrew's trust authentication on loopback:

```
export FIELDSERVICE_TEST_ADMIN_CONNECTION="Host=127.0.0.1;Port=5432;Database=postgres;Username=$USER"
```

In PowerShell on Windows, where the `postgres` superuser has a password:

```
$env:FIELDSERVICE_TEST_ADMIN_CONNECTION = "Host=127.0.0.1;Port=5432;Database=postgres;Username=postgres;Password=<placeholder>"
```

### How a run uses the database

1. The fixture checks the server version, then drops any `fieldservice_test_*` database older than 24 hours, left behind by an interrupted run.
2. It creates a database for this run, named `fieldservice_test_<yyyyMMddHHmmss>_<8 hex digits>`, by running the bootstrap script with that name.
3. It applies the migrations as `fieldservice_migrator`, and the tests then connect as `fieldservice_app`, the role the running API uses, so a missing or excessive grant fails a test.
4. Between tests, data is reset with Respawn; the reference tables keep their rows.
5. At the end of the run the database is dropped.

A test run never touches the data of the `fieldservice` development database. The roles, the bootstrap script and the grants are described in [database.md](database.md).

### Commands

The whole suite, and one class or one namespace:

```
dotnet test --project tests/FieldService.Api.Tests
dotnet test --project tests/FieldService.Api.Tests --filter-class FieldService.Api.Tests.Contracts.OpenApiSnapshotTests
dotnet test --project tests/FieldService.Api.Tests --filter-namespace FieldService.Api.Tests.Security
```

The `api` job runs these steps, in this order, after creating throwaway values for the three settings and starting the Compose `db` service:

```
dotnet tool restore
dotnet restore tests/FieldService.Api.Tests/FieldService.Api.Tests.csproj --locked-mode -p:Configuration=Release
dotnet format src/FieldService.Api/FieldService.Api.csproj --verify-no-changes --no-restore
dotnet format tests/FieldService.Api.Tests/FieldService.Api.Tests.csproj --verify-no-changes --no-restore
dotnet build tests/FieldService.Api.Tests/FieldService.Api.Tests.csproj --configuration Release --no-restore
dotnet test --project tests/FieldService.Api.Tests --configuration Release --no-build --results-directory artifacts/test-results/api --coverlet --coverlet-output-format cobertura --filter-not-trait "Category=Quarantine" --timeout 20m
dotnet test --project tests/FieldService.Api.Tests --configuration Release --no-build --results-directory artifacts/test-results/api-quarantine --filter-trait "Category=Quarantine" --ignore-exit-code 8 --timeout 20m
```

- The same lines run on a workstation. In CI, `CI` is `true`, which turns on locked restores and makes high and critical NuGet audit warnings errors.
- `--coverlet --coverlet-output-format cobertura` writes a Cobertura coverage file into the results folder.
- The last step runs only quarantined tests and never fails the job; exit code 8 means that no test ran.

When an endpoint changes, `OpenApiSnapshotTests` fails, shows the first differing lines and prints the command that rewrites `docs/contracts/openapi.v1.json`. The procedure for a contract change is in [contracts/README.md](contracts/README.md#changing-it); CI never rewrites the file.

### What the suite covers

| Folder | Covers |
|---|---|
| `Contracts/` | The OpenAPI document served by the API equals the committed snapshot; the document's conventions, including a 400 response on every write operation that takes a body or a parameter; no schema has a `format` without a `type`; every error status (400, 401, 403, 404, 409, 429 and 500) is a Problem Details response with its type, title, code, `traceId` and `correlationId`, and an `instance` without the query string; each 409 code (`job.not-assignable`, `technician.unavailable`, `job.invalid-transition`, `concurrency.conflict`) has its registered title, and each error of the status-change endpoint has the registered shape. The event contract: the API's serializer writes each internal fixture in `docs/contracts/events/internal/` byte for byte from the fixture's own values; the webhook signer reproduces `docs/contracts/events/signature-test-vector.json`; the socket-ticket issuer reproduces the token of `docs/contracts/events/socket-ticket-test-vector.json`, which validates under the API's own decoding of the vector key and is refused with one changed character or under another key; and the API's startup rules refuse both published vector keys |
| `Domain/` | The job lifecycle: the transition matrix for every role, the assignment rules, the affordances and the inspection rules |
| `Features/` | One folder per feature (`Auth`, `ChecklistTemplates`, `Jobs`, `Technicians`): validation, responses, stored rows, history entries and recorded events of each endpoint, including two assignments, and two status changes, of one job sent at the same time; the socket ticket endpoint; each committed create, assignment and status change posted once, signed, to a stub notification service |
| `Security/` | Every endpoint has an authorization policy or is on the anonymous list, and no endpoint exposes a domain type; every route answers each role and an anonymous caller with the expected status; a technician never sees or changes another technician's job, in a job read, a status change or the assigned-jobs list, and gets a 404 identical to a missing job's, with nothing written and no event recorded; access-token validation; socket tickets: signed with the socket-ticket key and never the access-token key, exactly the documented claims with a 60-second lifetime and a new ticket id each time, and refused with 401 once the session has ended (sign-out, refresh-token reuse, revocation, idle or absolute expiry, a deactivated user); the refresh cookie attributes and the CSRF header; rate limits; security headers, `Cache-Control: no-store`, CORS and forwarded headers; startup validation of keys and settings; the OpenAPI document is served only in Development and Testing, and `/swagger` is never served; no exception detail reaches a response outside Development; no password, token, socket ticket, key, webhook signature or personal detail reaches a log event or a log file; one request writes a CLEF line to the daily log file, with the configured retention limits |
| `Persistence/` | What `fieldservice_app` may and may not do, statement by statement; role settings; the lock on submitted inspections; the applied migration history, the reference data and its fixed ids, the indexes, constraints, ownership and grants; migration files without comments |
| `Shared/` | The mapping of concurrency and lock failures to 409 responses; the health endpoints; the real-time path: the job event queue, the webhook signer, the socket-ticket issuer, and the dispatcher's two-second attempts, retries, 10-second budget, circuit breaker and shutdown drain, with one Warning and nothing rolled back when the notification service is down |
| `Infrastructure/` | Test fixtures only: the per-run database, the API test host, log capture and test data |

## Technician app

### The .NET MAUI workload

The test project references the app, and restoring the app evaluates every target framework the app declares, `net10.0-android` included. The tests themselves run on the app's plain `net10.0` target, so they need no JDK, Android SDK, device or emulator, but the Android workload must be installed, or the restore fails with `NETSDK1147`. On Windows the app also declares a Windows target, which needs the `maui-windows` workload as well. Install the workload set as [development.md](development.md#net-maui-workload-versions) describes, then check it:

```
dotnet workload list
```

It should report workload version `10.0.402` with `maui-android`.

### Commands

```
dotnet test --project tests/FieldService.Mobile.Tests
dotnet build src/FieldService.Mobile -f net10.0-android
```

The second line builds the Android app in Debug; it is a build check, not a test. The `mobile-android` job installs the workload set, then runs:

```
dotnet workload list
dotnet restore tests/FieldService.Mobile.Tests/FieldService.Mobile.Tests.csproj
dotnet format src/FieldService.Mobile/FieldService.Mobile.csproj --verify-no-changes --no-restore
dotnet format tests/FieldService.Mobile.Tests/FieldService.Mobile.Tests.csproj --verify-no-changes --no-restore
dotnet test --project tests/FieldService.Mobile.Tests --configuration Release -p:ApiBaseUrl=https://fieldservice.example --results-directory artifacts/test-results/mobile --coverlet --coverlet-output-format cobertura --filter-not-trait "Category=Quarantine" --timeout 15m
dotnet test --project tests/FieldService.Mobile.Tests --configuration Release -p:ApiBaseUrl=https://fieldservice.example --no-build --results-directory artifacts/test-results/mobile-quarantine --filter-trait "Category=Quarantine" --ignore-exit-code 8 --timeout 15m
```

A Release build of the app requires an `https` API address. The plain `net10.0` target is exempt from that check, and `-p:ApiBaseUrl=https://fieldservice.example` keeps the Release test build valid either way; the address is never called.

### What the suite covers

| Folder | Covers |
|---|---|
| `Build/` | The Android resource designer of design-time builds stays in its own folder, so a later install build is not affected; the splash and launcher icon artwork use the brand mark on navy |
| `Contracts/` | The app's request and response types match `docs/contracts/openapi.v1.json`: member names, types, formats, nullability and enumerations, each enumeration with an `Unknown` member for values the app does not know yet. The status command is a PUT of its request schema, answered with the job details, its note limited to 500 characters |
| `Features/SignIn/` | The sign-in screen: field errors, the error banners, the message for an administrator account, the notice after a session ended, and the start route after sign-in |
| `Features/Jobs/` | My jobs and Job details: the active and completed lists grouped by day, refresh, the loading, empty, error and offline states, the details of each status, and the Navigate and Call actions of an assigned job. A reload that fails after a load keeps the jobs or the job on screen under a banner that names when they were loaded. Status changes: Start job on an assigned job and the Update status sheet offer only the changes the API lists for the job. The hold reason is required, holds at most 500 characters with a count line from 400, is sent trimmed, and is kept as a draft until the hold succeeds. Every response has its outcome: a 200 re-renders the job and is announced, a 400 on the reason shows on the field, other failures show a banner with the reference, a 404 returns to My jobs, and both conflicts reload the job and show the conflict banner. The actions are disabled while offline, and a completed or cancelled job has none. The On hold header shows the reason, and an answer that arrives after its session ended is dropped. The XAML checks cover the sheet's structure, each option row as one radio button for screen readers, the sheet's height with the keyboard open, and the banner styles. However the sheet closes (success, a conflict, Cancel, back or a tap outside), it lets go of the reason field, so the keyboard closes with it |
| `Shared/Http/` | The API address guard: every configuration other than Debug accepts only `https`; a missing, relative or rooted address and any other scheme are refused; Debug accepts the development `http` addresses; an app assembly stamped with an `http` address, or with none, fails at startup. Also the authorization handler, whose single refresh and retry stay with the session a request started under, so a request is never retried with another technician's token; the correlation id handler; HTTP log entries without tokens, headers, bodies or query strings; and the reading of Problem Details responses |
| `Shared/Formatting/` | Times keep their digits and AM or PM together, and short dates keep their weekday, day and month together, so neither breaks across lines |
| `Shared/Navigation/` | The app's Shell routes, resolved by a real Shell: a job opens Job details with its id, Back returns to My jobs, a job that is no longer assigned returns to My jobs with the notice, and start-up and sign-in switch between the sign-in screen and My jobs |
| `Shared/Session/` | Session restore at start-up, with the splash held until the first screen is shown; refresh, sign-out and expiry; the refresh token kept only in secure storage under its key, and cleared when it cannot be read. Every way a session ends goes through one clearing path. A session that a new sign-in replaces, or a refresh or restore overtaken by a sign-in or a sign-out, is signed out on the server with its own token, without delaying the new session |
| `Shared/Theme/` | XAML pages use only theme resources and give every control an accessible name and every page a heading; a job card is one button for screen readers; screens with a header run under the status bar, which is covered as soon as the content scrolls; line heights and the action bar follow the design; icon path data is drawn as specified; on Android every label is measured at the width it is drawn at, so wrapped text never loses its last line to pixel rounding |

## Notification service

The `notifications` job runs these steps in this order, each in the folder named:

| Folder | Command |
|---|---|
| `src/FieldService.Notifications` | `npm ci --ignore-scripts` |
| `tests/FieldService.Notifications.Tests` | `npm ci --ignore-scripts` |
| `src/FieldService.Notifications` | `npm run lint` |
| `src/FieldService.Notifications` | `npm run format:check` |
| `src/FieldService.Notifications` | `npm run typecheck` |
| `tests/FieldService.Notifications.Tests` | `npm run typecheck` |
| `src/FieldService.Notifications` | `npm run build` |
| `src/FieldService.Notifications` | `npm test` |

From the repository root, `npm --prefix <folder> run <script>` runs the same script in that folder, for example `npm --prefix src/FieldService.Notifications test`. Between these steps the job also verifies the registry signatures of both packages and audits their dependencies.

- `npm test` in the service runs the test package's `test` script, `node --test` over `unit/`, `integration/` and `contracts/`.
- `format:check` in the service also checks `tests/FieldService.Notifications.Tests`, which has no formatter configuration of its own.
- The integration tests start the service on free ports chosen by the system, and the entry-point test writes its log files into a temporary folder under the system temp folder (`TMPDIR` on macOS and Linux) and removes it.

Coverage is informational and is not collected in CI:

```
npm --prefix tests/FieldService.Notifications.Tests run test:coverage
```

| Folder | Covers |
|---|---|
| `unit/` | Configuration: defaults, each required variable, numeric bounds, enumerations and hosts, webhook secrets, the socket ticket key, published and patterned keys (the values of both vector files included), a webhook secret equal to the socket ticket key, and allowed origins. An invalid value stops the service with exit code 1 and a message that names the variable and never its value. Webhook signatures, the event envelope schema, the recent event ids (kept for twice the tolerance plus 60 s, at most 10,000), the ticket verification and its single-use ids, and the mapping of each event to the dashboard payload |
| `integration/` | The health routes answer 200 `Healthy` as `text/plain`; an unknown path gets 404 and a wrong method 405, both as Problem Details; a well-formed `X-Correlation-ID` is echoed and a malformed one dropped. The job event route's checks in order: 415, 413, 401 for missing headers, a stale timestamp or a bad signature, 400 for a malformed event, 202 for a duplicate or an unknown type without a broadcast, and 202 with a broadcast to the administrators room. Administrator sockets receive each event type; missing, expired, wrong-audience, reused and technician tickets and foreign origins are refused. The session cap disconnects a socket and logs its duration. The log entries of every outcome, connect and disconnect, with tickets, signatures, secrets, keys and headers redacted. A failing route handler is logged at error, and an uncaught exception or unhandled rejection at fatal, each with its stack; a failure value that is not an Error is logged by its type only, and an Error by its type, message, stack and code only. Shutdown closes every socket. The entry point listens, writes the daily log file and exits with code 0 on `SIGTERM`, and with code 1 after a process failure or when its port is taken |
| `contracts/` | Every internal fixture parses, mutated copies are refused, and each one posted with a valid signature reaches an administrator socket as its client fixture; the verifier accepts the signature vector and refuses it after any one-character change; the ticket verifier accepts the socket-ticket vector at its issue time and refuses it after a one-character change, under another key or too late |

## Web dashboard unit tests

The `web` job runs these steps in `src/FieldService.Web`:

```
cd src/FieldService.Web
npm ci --ignore-scripts
npm run generate:api
npm run lint
npm run format:check
npm run type-check
npm test -- --coverage
npm run build
```

- `generate:api` creates the typed API client in `src/shared/api/schema.gen.ts` from `docs/contracts/openapi.v1.json`. The file is ignored by git, and `dev`, `build`, `type-check` and `test` run `generate:api` first, so the client always follows the committed contract.
- `npm test` runs Vitest once. `-- --coverage` adds a V8 coverage report in `coverage/`; add `--coverage.reportsDirectory=<folder>` to write it elsewhere.

| Folder | Covers |
|---|---|
| `src/app/__tests__/` | Global error handling: errors thrown in components, unhandled promise rejections and failed navigations are reported with the message, source, route path, stack and time; a rejection reason that is not an `Error` is described; reporting stops after cleanup. Also the route guard and the favicon |
| `src/app/styles/__tests__/` | Stylesheets use the design tokens, and the text and background pairs meet their contrast ratios |
| `src/features/auth/__tests__/` | The sign-in page, the session store and the redirect check after sign-in |
| `src/features/jobs/__tests__/` | The jobs store, the create-job form with its technician select, and the assign panel: technician order, availability, conflicts and errors |
| `src/features/realtime/__tests__/` | The live updates client: WebSocket only, a new ticket for every connection attempt, the reconnect rules and back-off, the connection states (Reconnecting after 3 s, Live updates paused after 30 s, Back online), the refetch rules for events and reconnects, and the event payload check against `docs/contracts/events/client/` |
| `src/shared/api/__tests__/` | The authenticated fetch and the reading of API errors |
| `src/shared/format/__tests__/` | Dates, times and labels |
| `src/shared/testing/__tests__/` | Isolation between tests |
| `src/shared/ui/__tests__/` | Accessibility, states and behaviour of the shared components |

## End-to-end tests

The Playwright suite drives the dashboard in Chromium against a running stack. Tests find elements by role, label and visible text only.

| Project | Stack under test | Base URL |
|---|---|---|
| `host` | The applications running on the workstation, with the dashboard served by `vite preview` (production security headers, the Content Security Policy included) or by the Vite dev server (no security headers) | `http://127.0.0.1:5173` |
| `compose` | The Compose `full` profile, with nginx serving the built dashboard | `http://127.0.0.1:8081` |

| Script, in `src/FieldService.Web` | Runs |
|---|---|
| `npm run test:e2e:host` | The `host` project, without tests tagged `@compose-only` or `@quarantine` |
| `npm run test:e2e` | The `compose` project, without tests tagged `@quarantine` |

- Install the browser once and start the stack as [development.md](development.md#tests) describes. A run against the Vite dev server is useful for debugging, but only a run against `vite preview` checks the production headers: the header test in `security.spec.ts` fails against the dev server, which sends none of them.
- The demo account password comes from `src/FieldService.Web/e2e/.env`, which `create-dev-env.mjs` writes; a value already set in `E2E_DEMO_PASSWORD` takes precedence, so no password is typed or prompted for. `E2E_BASE_URL` overrides the base URL of both projects.
- The stack is the same one a developer runs: the API, the notification service and the dashboard, started as [development.md](development.md#running-the-stack) describes. The notification service and the API must share their keys, which `create-dev-env.mjs` writes for both.
- Before the first test, a global setup waits up to 60 seconds for the dashboard at `/`, for `GET /api/checklist-templates` without a token to answer 401 through the proxy, and for the notification service to answer at `/socket.io/` through the proxy (it refuses the polling transport with 400 and code 0), so a run against a stopped service fails at once. It also gives the run its id, `E2E_RUN_ID` unless that variable is set.
- After the last test, a global teardown searches every file under `logs/api` and `logs/notifications` (or under `E2E_LOGS_DIR`) for the access tokens, refresh cookies and socket tickets seen during the run, the demo password and the personal details of the demo data. A match fails the run and names the file, the line and the kind of value, never the value. A folder with no log file also fails the run.
- Every session a run opens is signed out before the run ends: the browser contexts that still hold a refresh cookie, the administrator context and each technician session. Each sign-out must answer 204, and the teardown fails the run unless the number of sessions signed out equals the number of sign-ins that succeeded.
- Automatic checks fail a test on any Content Security Policy violation, any request to another origin, any uncaught page error, any console error that the test does not declare as expected, and any Socket.IO handshake URL whose query holds more than the `EIO` and `transport` keys, so a ticket can never travel in a URL. That failure names the query keys, never their values. A budget check keeps a run below the API's rate limits: at most 7 sign-ins per run, and in any 60 seconds at most 25 session refreshes, 25 socket ticket requests and 250 authenticated requests of the administrator (the API allows 30, 30 and 300). Before each test the check waits until the last 60 seconds leave room for one more test, so a full run takes about two minutes, and the teardown prints the busiest minute of each budget and the pauses.
- Tests create their own jobs, with titles that start with `E2E <run id>`, and only read the demo jobs, so no run needs a freshly seeded database. They assign their jobs only to Lena Park and Tom Becker. Every run adds jobs to the development database; the VS Code task `db: reset` restores the demo data.
- Technician steps run through API sessions of the mobile client, one per technician and run, for example to check that an assigned job appears in that technician's list and that another technician gets a 404 for it, or to start, hold and resume a job. Each status change first reads the job's current `version`. The dashboard's job detail then shows the new history entries after one reload, and the dashboard itself never sends a status change.
- `realtime.spec.ts` checks live updates. With the jobs page open, a technician's start, hold and resume must each reach the job's row within 5 seconds, with the event toast, the row's "Just now" flash and no reload. `page.routeWebSocket` drops the socket for at least 5 seconds: the pill reads Reconnecting at once, the banner appears after 3 seconds, a hold made while the socket is down is missed, and when connections pass again the page shows "Back online. Jobs refreshed at …", requests the job list again and shows the hold, without a toast. With every connection refused, "Live updates paused" appears after 30 seconds, and Refresh now reconnects and refetches. This test waits the 30 seconds in real time, because `page.clock` would apply to every page of the shared administrator context.
- `accessibility.spec.ts` runs the axe checks for the WCAG 2.2 AA tags on each route and state at 1280 and 375 px. The states include the assign panel, a job on hold with its reason, the list filtered to In progress, a job's history after a start, a hold and a resume, and the live states: the Live pill with an event toast and a flashed row, the Reconnecting banner and Back online. The spec also checks reflow at 320 px (a 500-character hold reason and the Reconnecting banner included), reduced motion, forced colours, and creating and assigning a job by keyboard alone.
- Tests run one at a time against one shared stack. Under CI a failed test is retried once, and a test that passes only on the retry fails the run.

The specs have their own TypeScript project. To type-check them and list the tests of each project without a running stack:

```
cd src/FieldService.Web
npx tsc -p e2e/tsconfig.json
npx playwright test --list --project=host --reporter=list
npx playwright test --list --project=compose --reporter=list
```

The `compose` project needs Docker Desktop and the application images of the Compose `full` profile; the Compose file defines only the database service so far. The CI workflow has no end-to-end job yet.

## Repository checks

The `hygiene` job runs two scripts that need no installed package:

```
node infrastructure/scripts/check-no-comments.mjs
node infrastructure/scripts/check-tracked-files.mjs
```

The first fails on any comment in source and configuration files, test files and fixtures included. The second fails on files that must never be committed and on secret-shaped content. Both are described in [development.md](development.md#scripts).

## Results and coverage files

Test results never belong in the repository.

| Output | Location | In git |
|---|---|---|
| .NET test results and coverage | The `--results-directory` folder; CI uses `artifacts/test-results/<suite>` | `artifacts/` and `TestResults/` are ignored |
| Web unit coverage | `src/FieldService.Web/coverage/` | Ignored |
| Playwright reports, traces, screenshots and videos | `src/FieldService.Web/playwright-report/` and `src/FieldService.Web/test-results/` | Ignored |
| API log files written by tests | A folder per test host under `fieldservice-api-tests` in the system temp folder; the tests that write log files remove their folder | Outside the repository |

`check-tracked-files.mjs` fails if a file in any of these folders is ever tracked.

## Quarantined tests

A test that both passes and fails on the same code is a defect. While its cause is being fixed, it can be quarantined for at most five working days:

- **.NET:** `[Trait("Category", "Quarantine")]`, with `[Trait("Issue", "<number>")]` naming the tracking issue. The blocking CI steps exclude the trait, and the non-blocking steps run the quarantined tests in their own results folders.
- **Playwright:** the tag `@quarantine`, which both scripts exclude.
- **Vitest and Node:** no quarantine; the test is fixed or removed the same day.

## Verification summary

The workstation commands on this page were run on 2026-10-23 on macOS 26.5 (Apple Silicon) with .NET SDK 10.0.402, workload set 10.0.402, Node 26.12.0 and PostgreSQL 17.11, from the folders named above, with the results and coverage folders pointed outside the repository.

| Suite | Result |
|---|---|
| API | 1401 of 1401 passed in Debug; both `dotnet format` checks passed; the Release build of the API had no warning, and the model has no pending change |
| Technician app | 523 of 523 passed in Debug; the Android Debug build succeeded with no warning |
| Notification service | 464 of 464 passed in 50 suites; lint, formatting, both type-checks and the build passed |
| Web dashboard, unit | 848 of 848 passed in 22 files; creating the API client, lint, formatting, the type-check with the end-to-end specs, and the build passed |
| Web dashboard, end to end | The `host` project against `vite preview`, with the API and the notification service running: 26 of 26 passed in 6 files in each of three runs of about two minutes, with no Content Security Policy violation, no handshake URL with an extra query key, 45 axe audits without a violation, no match in the log search of 4 API and 4 notification service log files, and all 5 sessions signed out. In each run every live status change reached the dashboard in under a second. Against the Vite dev server 25 of 26 passed, the header test failing there as described above; both projects list 26 tests in 6 files |
| Repository checks | Both scripts passed |

Not run on that date: the Release forms with coverage and the quarantine steps of the CI jobs; the Windows commands, because no Windows host was available; and the `compose` project and the Compose database, because no container runtime was available.
