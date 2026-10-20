# Testing

FieldService is tested at four levels: unit tests and integration tests inside each application, contract tests that hold the applications to their shared contracts, and end-to-end tests of the dashboard in a browser. This page lists every suite, what it covers, how to run it on a workstation and how the CI workflow runs it.

Setting up the workstation, including the local database and the local secrets, is described in [development.md](development.md); its [Tests](development.md#tests) section has the short form of the commands below. Run every command from the repository root unless a step says otherwise.

## Suites

| Suite | Folder | Runner | Needs | CI job |
|---|---|---|---|---|
| API | `tests/FieldService.Api.Tests` | xUnit v3 on Microsoft.Testing.Platform | PostgreSQL 17, `psql` 15 or later on `PATH`, and the test settings below | `api` |
| Technician app | `tests/FieldService.Mobile.Tests` | xUnit v3 on Microsoft.Testing.Platform | The .NET MAUI Android workload; no device, emulator, JDK or Android SDK | `mobile-android` |
| Notification service | `tests/notification-tests` | Node's test runner (`node --test`) | Node 26.12.0, with both packages installed | `notifications` |
| Web dashboard, unit | `src/fieldservice-web/src/**/__tests__/*.spec.ts` | Vitest with jsdom | Node 26.12.0 | `web` |
| Web dashboard, end to end | `src/fieldservice-web/e2e` | Playwright with Chromium | A running stack | None yet |

Before the first run, restore the local tools and install the three npm packages from their lock files:

```
dotnet tool restore
npm ci --ignore-scripts --prefix src/fieldservice-notifications
npm ci --ignore-scripts --prefix tests/notification-tests
npm ci --ignore-scripts --prefix src/fieldservice-web
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
| `Contracts/` | The OpenAPI document served by the API equals the committed snapshot; no schema has a `format` without a `type`; the shape of the 500 Problem Details response |
| `Security/` | Every endpoint has an authorization policy or is on the anonymous list, and no endpoint exposes a domain type; the OpenAPI document is served only in Development and Testing, and `/swagger` is never served; no exception detail reaches a response outside Development; one request writes a CLEF line to the daily log file, with the configured retention limits |
| `Persistence/` | What `fieldservice_app` may and may not do, statement by statement; role settings; the lock on submitted inspections; the applied migration history, the reference data and its fixed ids, the indexes, constraints, ownership and grants; migration files without comments |
| `Shared/` | The mapping of concurrency and lock failures to 409 responses; the health endpoints |
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
| `Shared/Http/` | The API address guard: every configuration other than Debug accepts only `https`; a missing, relative or rooted address and any other scheme are refused; Debug accepts the development `http` addresses; an app assembly stamped with an `http` address, or with none, fails at startup |
| `Spikes/` | That the `net10.0` target is testable: a view model reads secure storage and connectivity through substitutes, and the `net10.0` assembly leaves out the platform code |

## Notification service

The `notifications` job runs these steps in this order, each in the folder named:

| Folder | Command |
|---|---|
| `src/fieldservice-notifications` | `npm ci --ignore-scripts` |
| `tests/notification-tests` | `npm ci --ignore-scripts` |
| `src/fieldservice-notifications` | `npm run lint` |
| `src/fieldservice-notifications` | `npm run format:check` |
| `src/fieldservice-notifications` | `npm run typecheck` |
| `tests/notification-tests` | `npm run typecheck` |
| `src/fieldservice-notifications` | `npm run build` |
| `src/fieldservice-notifications` | `npm test` |

From the repository root, `npm --prefix <folder> run <script>` runs the same script in that folder, for example `npm --prefix src/fieldservice-notifications test`. Between these steps the job also verifies the registry signatures of both packages and audits their dependencies.

- `npm test` in the service runs the test package's `test` script, `node --test` over `unit/`, `integration/` and `contracts/`.
- `format:check` in the service also checks `tests/notification-tests`, which has no formatter configuration of its own.
- The integration tests start the service on free ports chosen by the system, and the entry-point test writes its log files into a temporary folder under the system temp folder (`TMPDIR` on macOS and Linux) and removes it.

Coverage is informational and is not collected in CI:

```
npm --prefix tests/notification-tests run test:coverage
```

| Folder | Covers |
|---|---|
| `unit/` | Configuration: defaults, each required variable, numeric bounds, enumerations and hosts, webhook secrets, the socket ticket key, published and patterned keys, and allowed origins. An invalid value stops the service with exit code 1 and a message that names the variable and never its value |
| `integration/` | The health routes answer 200 `Healthy` as `text/plain`; an unknown path gets 404 and a wrong method 405, both as Problem Details; a well-formed `X-Correlation-ID` is echoed and a malformed one dropped. The entry point listens, writes the daily log file and exits with code 0 on `SIGTERM` |

## Web dashboard unit tests

The `web` job runs these steps in `src/fieldservice-web`:

```
cd src/fieldservice-web
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
| `src/app/__tests__/` | Global error handling: errors thrown in components, unhandled promise rejections and failed navigations are reported with the message, source, route path, stack and time; a rejection reason that is not an `Error` is described; reporting stops after cleanup |

## End-to-end tests

The Playwright suite drives the built dashboard in Chromium against a running stack. Tests find elements by role, label and visible text only.

| Project | Stack under test | Base URL |
|---|---|---|
| `host` | The applications running on the workstation, with the dashboard served by `vite preview` (production security headers, the Content Security Policy included) or by the Vite dev server (no security headers) | `http://127.0.0.1:5173` |
| `compose` | The Compose `full` profile, with nginx serving the built dashboard | `http://127.0.0.1:8081` |

| Script, in `src/fieldservice-web` | Runs |
|---|---|
| `npm run test:e2e:host` | The `host` project, without tests tagged `@compose-only` or `@quarantine` |
| `npm run test:e2e` | The `compose` project, without tests tagged `@quarantine` |

- Install the browser and start the stack as [development.md](development.md#tests) describes. A run against the Vite dev server is useful for debugging, but only a run against `vite preview` checks the production headers.
- The demo account password comes from `src/fieldservice-web/e2e/.env`, which `create-dev-env.mjs` writes; a value already set in `E2E_DEMO_PASSWORD` takes precedence, so no password is typed or prompted for. `E2E_BASE_URL` overrides the base URL of both projects.
- Tests run one at a time against one shared stack. Under CI a failed test is retried once, and a test that passes only on the retry fails the run.
- The suite has no specs yet; they are written together with the dashboard features they cover. Until the first spec exists, a run reports "No tests found" and exits with code 1. To check the configuration alone, list the tests of each project:

```
cd src/fieldservice-web
npx playwright test --list --project=host --pass-with-no-tests --reporter=list
npx playwright test --list --project=compose --pass-with-no-tests --reporter=list
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
| Web unit coverage | `src/fieldservice-web/coverage/` | Ignored |
| Playwright reports, traces, screenshots and videos | `src/fieldservice-web/playwright-report/` and `src/fieldservice-web/test-results/` | Ignored |
| API log files written by tests | A folder per test host under `fieldservice-api-tests` in the system temp folder; the tests that write log files remove their folder | Outside the repository |

`check-tracked-files.mjs` fails if a file in any of these folders is ever tracked.

## Quarantined tests

A test that both passes and fails on the same code is a defect. While its cause is being fixed, it can be quarantined for at most five working days:

- **.NET:** `[Trait("Category", "Quarantine")]`, with `[Trait("Issue", "<number>")]` naming the tracking issue. The blocking CI steps exclude the trait, and the non-blocking steps run the quarantined tests in their own results folders.
- **Playwright:** the tag `@quarantine`, which both scripts exclude.
- **Vitest and Node:** no quarantine; the test is fixed or removed the same day.

## Verification summary

Every command on this page was run on 2026-10-20 on macOS 26.5 (Apple Silicon) with .NET SDK 10.0.402, workload set 10.0.402, Node 26.12.0 and PostgreSQL 17.11, with the results and coverage folders pointed outside the repository.

| Suite | Result |
|---|---|
| API | 112 of 112 passed, in Debug and in the Release form with coverage, and with the admin connection taken from the environment; the quarantine step ran no test; the filter examples ran 16 and 21 tests |
| Technician app | 29 of 29 passed, in Debug and in the Release form with coverage; the quarantine step ran no test; the Android Debug build succeeded with no warning |
| Notification service | 93 of 93 passed, with and without coverage; lint, formatting, both type-checks and the build passed |
| Web dashboard, unit | 6 of 6 passed, with and without coverage; creating the API client, lint, formatting, the type-check and the build passed |
| Web dashboard, end to end | Both projects list 0 tests in 0 files; no spec exists yet |
| Repository checks | Both scripts passed |

Not run yet: the Windows commands, because no Windows host was available; the `compose` project and the Compose database, because no container runtime was available; and end-to-end runs, because no spec exists yet.
