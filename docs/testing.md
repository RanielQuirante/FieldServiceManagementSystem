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
| Web dashboard, end to end | `src/FieldService.Web/e2e` | Playwright with Chromium | A running stack: the applications on the workstation, or the Compose `full` profile with Docker | `e2e` |

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
| `Contracts/` | The OpenAPI document served by the API equals the committed snapshot; the document's conventions, including a 400 response on every write operation that takes a body or a parameter, and the `uuid` format on every id except the client error report's `correlationId`, which the contract defines as a pattern string; no schema has a `format` without a `type`; every error status (400, 401, 403, 404, 409, 413, 429 and 500) is a Problem Details response with its type, title, code, `traceId` and `correlationId`, and an `instance` without the query string; each 409 code (`job.not-assignable`, `technician.unavailable`, `job.invalid-transition`, `concurrency.conflict`, `inspection.incomplete`, `inspection.locked`) has its registered title, and each error of the status-change, cancellation, dashboard and client error endpoints has the registered shape and repeats nothing from the request. The event contract: the API's serializer writes each internal fixture in `docs/contracts/events/internal/` byte for byte from the fixture's own values; the webhook signer reproduces `docs/contracts/events/signature-test-vector.json`; the socket-ticket issuer reproduces the token of `docs/contracts/events/socket-ticket-test-vector.json`, which validates under the API's own decoding of the vector key and is refused with one changed character or under another key; and the API's startup rules refuse both published vector keys |
| `Domain/` | The job lifecycle: the transition matrix for every role, the assignment rules, the affordances and the inspection rules |
| `Features/` | One folder per feature (`Auth`, `ChecklistTemplates`, `Dashboard`, `Diagnostics`, `Inspections`, `Jobs`, `Technicians`): validation, responses, stored rows, history entries and recorded events of each endpoint, including two assignments, and two status changes, of one job sent at the same time, and a cancellation racing an assignment, a status change, a submission or another cancellation; the dashboard summary's figures for a time window; client error reports, logged once with every scrubbing pattern applied; the socket ticket endpoint; each committed create, assignment and status change posted once, signed, to a stub notification service |
| `Security/` | Every endpoint has an authorization policy or is on the anonymous list, and no endpoint exposes a domain type; every route answers each role and an anonymous caller with the expected status, and the administrator-only routes (the dashboard, job cancellation, client error reports) refuse other callers with 401 or 403 before they validate the request; a technician never sees or changes another technician's job, in a job read, a status change or the assigned-jobs list, and gets a 404 identical to a missing job's, with nothing written and no event recorded; a technician's cancellation gets the same 403 for their own job, another technician's job and a missing job; access-token validation; socket tickets: signed with the socket-ticket key and never the access-token key, exactly the documented claims with a 60-second lifetime and a new ticket id each time, and refused with 401 once the session has ended (sign-out, refresh-token reuse, revocation, idle or absolute expiry, a deactivated user); the refresh cookie attributes and the CSRF header; rate limits, including 10 client error reports a minute per user, with a refused report never logged; security headers, `Cache-Control: no-store`, CORS and forwarded headers; startup validation of keys and settings, including the trusted proxy address that every environment apart from Development and Testing must list, as one host's address; the OpenAPI document is served only in Development and Testing, and `/swagger` is never served; no exception detail reaches a response outside Development; no password, token, socket ticket, key, webhook signature or personal detail reaches a log event or a log file, and no token, secret, email address or query string sent in a client error report reaches them either, at every log level; one request writes a CLEF line to the daily log file, with the configured retention limits |
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
| `Features/SignIn/` | The sign-in screen: field errors, the error banners, the message for an administrator account, the notice after a session ended, and the start route after sign-in. While the device is offline, Sign in shows the unreachable message without sending a request, and it signs in once the connection comes back |
| `Features/Jobs/` | My jobs and Job details: the active and completed lists grouped by day, refresh, the loading, empty, error and offline states, the details of each status, and the Navigate and Call actions of an assigned job. A reload that fails after a load keeps the jobs or the job on screen under a banner that names when they were loaded. Status changes: Start job on an assigned job and the Update status sheet offer only the changes the API lists for the job. The hold reason is required, holds at most 500 characters with a count line from 400, is sent trimmed, and is kept as a draft until the hold succeeds. Every response has its outcome: a 200 re-renders the job and is announced, a 400 on the reason shows on the field, other failures show a banner with the reference, a 404 returns to My jobs, and both conflicts reload the job and show the conflict banner. The actions are disabled while offline, and a completed or cancelled job has none. The On hold header shows the reason, and an answer that arrives after its session ended is dropped. A cancelled job shows the reason of its newest Cancelled entry under the status line, read with it as one description; it has no dock, and with no saved answer its checklist is an information row that opens nothing. The completed list orders jobs by the time they were completed or cancelled, newest first, and adds the date to a job's time when its scheduled day differs from the day it is listed under. The XAML checks cover the sheet's structure, each option row as one radio button for screen readers, the sheet's height with the keyboard open, and the banner styles. However the sheet closes (success, a conflict, Cancel, back or a tap outside), it lets go of the reason field, so the keyboard closes with it. Closing the Update status sheet or the account sheet without a change (Cancel, back or a tap outside) gives focus back to the button that opened it; signing out does not, and neither does a close that is refused while a status change is being sent. On Windows, closing the Update status sheet after Update status has become disabled, for example offline, moves focus to the job title; on Android focus returns to Update status, which TalkBack can still reach. Reopened after a change was chosen and the sheet cancelled, the two-row sheet's disabled confirm button shows no icon. In the two-pane layout the sheet is a centred panel with no handle, its title 24 below its top edge, and more room for its content than the phone sheet. On Windows, Call is never offered, even where the system reports that it can dial; the contact's phone number is selectable text instead, only on the line that holds it, and the row is still read as one description. Android keeps Call where the device can dial. In the two-pane layout a conflict also reloads the list: a job that is no longer active leaves Active while the pane keeps showing it with no card selected, until the segment changes, and a job that stays active keeps its card selected with its new status; on a pushed Job details, a conflict notifies My jobs as a success does. Both My jobs segments stay usable while a list loads: choosing the other segment shows it at once and drops the load in flight, and a second tap on the chosen segment sends nothing. Crossing 900 wide: with no sheet open, keyboard focus moves to the account button of the new layout, in both directions; an open account sheet or Update status sheet closes, sends nothing and drops a typed reason; a status change that is being sent keeps its sheet open until it ends, then the sheet closes, a success is announced, a failure shows no banner, and focus moves to the account button; on a pushed Job details the sheet closes the same way and focus moves to the in-page Back. While a sign-out runs, the account sheet stays open across the switch, Cancel, the overlay, Back and Esc do nothing, and a second press of Sign out sends nothing. A reload merges the lists job by job: a refresh with the same jobs changes nothing, a changed day group is updated in place with inserts, removals and moves and is never replaced or reset, and a job still shown keeps its card with the new values. A test model of the platform's grouped list adapter counts rows as the adapter does and checks every change against it, so the rows on screen never fall out of step with the list, also when a submitted job leaves My jobs; a connectivity change raised on another thread reaches the list only on the screen's thread. In the two-pane layout the selected card stays selected when its job changes on a reload or through the pane. Widening past 900 with a phone Job details open takes it off without animation and hands its loaded job to the two panes: an open status sheet closes, sends nothing and drops a typed reason; a change being sent keeps its sheet until the answer, which is announced, while a failure shows no banner; narrowed again before the answer, the job stays on the phone page with focus on its Back. The panes show the handed job at once, select its card, bring it into view and focus the account button; a job no longer in the chosen segment shows in the pane with no card selected, also after a reload drops its card; offline, the pane shows the job as of its load time |
| `Features/Inspection/` | The checklist and Review and submit: saved answers, counts and items left on opening; Pass, Fail with its required note, and N/A, which hides the readings; reading values, their problems and their display; leaving the checklist saves only the changed complete answers, and Review saves first; field errors placed on their answers; the conflict reload that keeps the technician's changes; a locked save, a failed save, an offline checklist and a failed reload, each with its banner; the submission with its trimmed notes, the site-safety confirmation and its error, and time on site. The answer options stay in one row while each option is at least its widest label plus 4 wide, and otherwise stack as full-width rows with their icons. Choosing Fail shows the fault field before it asks for the field's focus, and a field's focus request brings the whole field into view before its input takes focus; on Windows the reveal waits for a layout pass, so a field shown by the same action already has its height, and keeps 16 clear around it. Review and submit's result chips wrap like the checklist's count pills, 8 apart. With the on-screen keyboard open, both pages hide the action bar and its spacer, shrink above the keyboard on Android (the app resizes only while such a page is shown) and bring the focused field into view whole, with its label and every line, again whenever the visible area shrinks; crossing 900 wide brings the focused control back into view without moving focus, and on Windows a focused control is revealed 16 clear |
| `Shared/Connectivity/` | When the device counts as online. On Windows only internet access, full or constrained, counts, so a released network address takes the app offline and a renewed one brings it back; on Android every state other than no network counts as online |
| `Shared/Http/` | The API address guard: every configuration other than Debug accepts only `https`; a missing, relative or rooted address and any other scheme are refused; Debug accepts the development `http` addresses; an app assembly stamped with an `http` address, or with none, fails at startup. Also the authorization handler, whose single refresh and retry stay with the session a request started under, so a request is never retried with another technician's token; the correlation id handler; HTTP log entries without tokens, headers, bodies or query strings; and the reading of Problem Details responses |
| `Shared/Formatting/` | Times keep their digits and AM or PM together, and short dates keep their weekday, day and month together, so neither breaks across lines |
| `Shared/Navigation/` | The app's Shell routes, resolved by a real Shell: a job opens Job details with its id, Back returns to My jobs, a job that is no longer assigned returns to My jobs with the notice, and start-up and sign-in switch between the sign-in screen and My jobs |
| `Shared/Session/` | Session restore at start-up, with the splash held until the first screen is shown; refresh, sign-out and expiry; the refresh token kept only in secure storage under its key, and cleared when it cannot be read. Every way a session ends goes through one clearing path. A session that a new sign-in replaces, or a refresh or restore overtaken by a sign-in or a sign-out, is signed out on the server with its own token, without delaying the new session |
| `Shared/Theme/` | XAML pages use only theme resources and give every control an accessible name and every page a heading; a job card is one button for screen readers; screens with a header run under the status bar, which is covered as soon as the content scrolls; line heights and the action bar follow the design; icon path data is drawn as specified; on Android every label is measured at the width it is drawn at, so wrapped text never loses its last line to pixel rounding. A button label that wraps is one start-aligned block with 8 above and below it; the Navigate and Call buttons stack as full-width rows when a label and its icon would come within 8 of a button's sides; and the header's priority moves under the status chip when the two do not fit on one row. Sheets: on Android, the page a sheet hid returns to the accessibility tree when the sheet closes, and an element is put back only when nothing else decides its place there; closing a sheet ends the input session of every field in it, even one that has already lost focus, so later key presses cannot reach a hidden field; a focus move skips a hidden control and moves keyboard focus as well as screen-reader focus; the Update status buttons and the account button take focus back in both layouts; and on a phone the action bar rises above an open keyboard, behind the sheet, so Update status can take focus while the sheet closes and the keyboard goes. Lists: the day groups of both job lists, the list header's date, title and sync line, the banners and the empty state are separate screen-reader stops, every heading inside a list is its own stop with the title as the screen heading, and a chosen segment keeps screen-reader focus while its list rebuilds during the next ten seconds. Icons: on Android an icon is drawn by a plain view, so a touch on the icon reaches the control under it; the icon is the app's only drawing view, and an icon drawn over a button adds no stop of its own; a button's icon follows its resource, and an empty icon key clears it. Shadows: no style, visual state, trigger or code turns a shadow off with an empty value, every shadow that a state or trigger changes has a base shadow, and disabled buttons use the invisible shadow, so a change in a button's enabled state never makes the platform rebuild the shadow's container, which crashed the app on Windows. Windows look: the check box is 20 with a 1.5 border on a white fill, centred in its row without the platform's inset, also when its template arrives after it has loaded; text fields lose the platform's side padding, so text starts 16 inside the border, or 48 after a leading icon, single-line text stays centred, and multi-line fields get 10 more top padding; the job lists draw no item background or selection bar in any state, list cards keep their stroke without a shadow (Android keeps the shadow), and the selected card's halo stays inside its list item. Layout: the job pane puts its information card and its details side by side, 300 wide and 20 apart, from a pane 620 wide, and stacks them 20 apart below that, while the phone layout keeps one column with 16 between cards; the checklist's count pills sit 8 apart and wrap to a second row 8 below, each measured against the row's width, so no pill is cut; both layouts ask for one new measure when they are arranged narrower than they were measured. Keyboard and screen readers: an opening sheet puts keyboard focus on the selected row, otherwise on the first row that can be chosen, otherwise on Cancel; Esc, the overlay, Cancel and Back reach the same close command, and a close that the sheet refuses closes nothing; on Windows the page behind a sheet leaves Narrator's tree while Tab cycles inside the sheet, a tap on the overlay takes no focus, and the job list's empty view is not a Tab stop, so a reload leaves focus where it was; on Android only an exclusion that starts after the page was shown keeps keyboard focus out, and the overlay is not a Tab stop. On Windows a focus move by code first scrolls its target wholly into view, 16 clear, without animation. On Windows the list is named "Jobs", Narrator reads each job card as one element named by its content and each day group as a heading, and the arrow keys move between cards without selecting them; the status options act on Space and Enter, and the site-safety confirmation on Space, and Narrator reads them as radio buttons in a "Change to" group and as a check box; a job title takes keyboard focus only when focus is moved there by code, and only until it loses it; pushed pages hide the title bar's Back on Windows and keep their own Back. Each screen marks the control where keyboard focus starts: the account button, the in-page Back, or Email on the sign-in page. On Windows, a screen that opens while keyboard focus is outside it, for example on the window's input site, moves focus there. Focus rings: on Windows a focused button keeps its normal look and shows one ring 2 wide and 2 outside the control, with concentric corners, in the colour of its focused state; on Android, in keyboard mode, option rows, check box rows and job cards draw the same ring instead of the platform's grey highlight, only under keyboard focus, and buttons keep their focused stroke; the check box inside its row is not a second stop, and lists, scroll containers and the containers around a page are never keyboard stops; a control that focus is moved to by code is never rebuilt by its disabled look, so the first key after the move reaches it. Banners: a banner is announced when it appears or when a new message replaces it, never while it clears; when a banner with Try again leaves while focus is inside it, focus moves to the first day group or the empty state once the My jobs list is back, or is dropped if the reload fails again, and moves to the job title on Job details; Try again stays available while it runs, and a second press sends no request. On Windows the day-group heading of the job list takes that focus as the list's header item, so Narrator reads it and the next Tab reaches the first card, and the list does not park focus on the account button meanwhile; where the platform has no header item, the heading label takes focus as before. Measure and arrange: the card, dock and button layouts measure their children only in the measure pass; arranged at the width they were measured at, they change no child's size and ask for no new measure, and arranged at another width they never measure a child without a width limit, which Windows rejected at larger text sizes and in narrow windows. Windows line heights: each theme line height maps back to the spec value it was rounded from, and Windows sets each label's line pitch from its rendered text size and updates it when the text size changes, so a failed item's note on Review takes one and a half lines at every text size, and a label without a theme line height keeps the font's own. The two-pane job actions wrap to a new row at large text sizes; each button is as wide as its icon and one-line label, is not measured again when arranged at that width, and wraps its label only when it is wider than the row. On Windows, when a focused action is hidden by a status change, the platform's own focus recovery away from it is held back until the app moves focus, and only that recovery is held |

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
| `src/app/__tests__/` | Global error handling: errors thrown in components, unhandled promise rejections and failed navigations show the error toast and, while an administrator is signed in, are reported to `POST /api/client-errors` with the source, route path, stack frames and time; reports are deduplicated, limited to 10 a minute, paused after a 429, stripped of query strings, tokens, email addresses and quoted values, and never sent signed out; the toast shows a reference only after the report is accepted; a rejection reason that is not an `Error` is described by its kind. Also the route guard with the dashboard at `/`, and the favicon |
| `src/app/styles/__tests__/` | Stylesheets use the design tokens, and the text and background pairs meet their contrast ratios |
| `src/features/auth/__tests__/` | The sign-in page, the session store and the redirect check after sign-in |
| `src/features/dashboard/__tests__/` | The dashboard store and page: the day window, the hero sentence and greeting, the six status tiles with their sublines, Needs a technician, Live activity with its New entries, the workload, each part's loading, empty and error forms, and the refetch on events and on reconnect |
| `src/features/jobs/__tests__/` | The jobs store, the create-job form with its technician select, the assign panel (technician order, availability, conflicts and errors), the inspection report, and job cancellation: the dialog, every CancelJob answer and the read-only cancelled job |
| `src/features/technicians/__tests__/` | The technicians page: the shift filters and their counts, the search, the filters in the address, the people list on narrow widths and its states |
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

- Install the browser once and start the stack as [development.md](development.md#tests) describes. A run against the Vite dev server is useful for debugging, but only a run against `vite preview` checks the production headers: the header test in `security.spec.ts` fails against the dev server, which sends none of them. A failed test restarts the worker, which signs in again, so on the dev server the tests that run after the header test (`technicians.spec.ts`) fail on the sign-in budget; run them on their own there.
- The demo account password comes from `src/FieldService.Web/e2e/.env`, which `create-dev-env.mjs` writes; a value already set in `E2E_DEMO_PASSWORD` takes precedence, so no password is typed or prompted for. The tests put it into the sign-in form with an input event instead of a typed fill, because Playwright names a fill step after the value it typed, and the HTML report lists every step. `E2E_BASE_URL` overrides the base URL of both projects.
- The stack is the same one a developer runs: the API, the notification service and the dashboard, started as [development.md](development.md#running-the-stack) describes. The notification service and the API must share their keys, which `create-dev-env.mjs` writes for both.
- Before the first test, a global setup waits up to 60 seconds for the dashboard at `/`, for `GET /api/checklist-templates` without a token to answer 401 through the proxy, and for the notification service to answer at `/socket.io/` through the proxy (it refuses the polling transport with 400 and code 0), so a run against a stopped service fails at once. It also gives the run its id, `E2E_RUN_ID` unless that variable is set.
- After the last test, a global teardown searches the logs for the access tokens, refresh cookies and socket tickets seen during the run, the demo password and the personal details of the demo data. A match fails the run and names the file or container, the line and the kind of value, never the value.
  - It searches every file under `logs/api` and `logs/notifications` (or under `E2E_LOGS_DIR`); against Compose it also searches the consoles of the `web` (nginx), `api`, `notifications` and `db` containers since the run started, read with `docker compose -f infrastructure/docker-compose.yml --profile full logs`.
  - Before the search it sends its own seeds, through the stack and directly to the API on 5080 and the notification service on 5090: a marker in seven request headers (an invalid `X-Correlation-ID`, `User-Agent`, `Referer`, `Cookie`, `Authorization`, `X-Forwarded-For` and `X-Real-IP`) and a marker in the technicians search query. Both markers join the searched values.
  - Each source also gets a positive control that it must hold, or the run fails: a valid correlation id on an API request (the API files and console, and nginx's line for it), one on a request the notification service refuses (its files and console), one on a request nginx answers itself, and a database connection refused for an invalid startup parameter, which PostgreSQL logs with the marker. A folder with no log file also fails the run.
  - Then it deletes the run's files from `test-results/sentinels/`, so no recorded value stays on disk or reaches an uploaded artifact.
- After the HTML report (and in CI the JUnit file) is written, a report scan reads every file under `playwright-report/` and `test-results/`, including the report data embedded in `index.html` and the content of every zip file such as a trace, zip files nested inside it included, for the run's tokens, refresh cookies, socket tickets, the header marker and the demo password. Its positive control is the run id, which every test carries as an annotation and which the scan must find in the decoded report data.
  - A match fails the run. The scan deletes every file that holds a match: a trace, an error context, their copies in `playwright-report/data/`, and an archive that carries the value inside another archive. For each one it prints the file's path, its kind (for example `trace` or `trace in the report data`) and the kind of value it held, never the value.
  - Screenshots and videos stay. They hold pixels rather than the page's text, the dashboard never displays a token, and a password field shows only masked characters.
- The report scan runs only as one of the reporters in `playwright.config.ts`, so never override the reporters on a run that executes tests: no `--reporter`, `--ui` or `--debug`. `--reporter` replaces the configured reporters, the scan among them, and a failed test's trace and error context then stay in `test-results/` and `playwright-report/` with any token or demo password they hold. `--reporter=list` is only for `--list`, which runs no test. To keep a trace, run with the configured reporters, whose scan deletes a trace that holds a secret, or debug from the failed test's screenshot and video. If the reporters were overridden anyway, delete `test-results/` and `playwright-report/` right after reading them. A run whose reporters leave out the scan prints a warning in the global teardown and fails.
- Run the Compose suite from the command line with `npm run test:e2e`. When the Playwright extension for VS Code runs tests, keep its "Show trace viewer" setting off, run the global teardown from the extension afterwards, and delete `test-results/` and `playwright-report/` if the run reports a file deleted because it held a secret.
- Every session a run opens is signed out before the run ends: the browser contexts that still hold a refresh cookie, the administrator context and each technician session. Each sign-out must answer 204, and the teardown fails the run unless the number of sessions signed out equals the number of sign-ins that succeeded.
- Automatic checks fail a test on any Content Security Policy violation, any request to another origin, any uncaught page error, any console error that the test does not declare as expected, and any Socket.IO handshake URL whose query holds more than the `EIO` and `transport` keys, so a ticket can never travel in a URL. That failure names the query keys, never their values. A budget check keeps a run below the API's rate limits: at most 7 sign-ins per run, and in any 60 seconds at most 25 session refreshes, 25 socket ticket requests and 250 authenticated requests of the administrator (the API allows 30, 30 and 300). Before each test the check waits until the last 60 seconds leave room for one more test, so a full run takes about three and a half minutes, and the teardown prints the busiest minute of each budget and the pauses. To stay inside these budgets, tests move between pages with in-app navigation and change the window width on the open page; they load a page only when they need a fresh document.
- Tests create their own jobs, with titles that start with `E2E <run id>`, and only read the demo jobs, so no run needs a freshly seeded database. They assign their jobs only to Lena Park, Tom Becker and Priya Natarajan, and they cancel only jobs they created. Every run adds jobs to the development database; the VS Code task `db: reset` restores the demo data. Helpers that look for a demo job in a status read every page of the job list, so the jobs earlier runs left behind never hide the demo jobs.
- Technician steps run through API sessions of the mobile client, one per technician and run, for example to check that an assigned job appears in that technician's list and that another technician gets a 404 for it, or to start, hold and resume a job, save its inspection answers and submit it, which completes the job. Each status change first reads the job's current `version`. The dashboard's job detail then shows the new history entries after one reload, and the dashboard itself never sends a status change.
- `realtime.spec.ts` checks live updates. With the jobs page open, a technician's start, hold and resume must each reach the job's row within 5 seconds, with the event toast, the row's "Just now" flash and no reload. `page.routeWebSocket` drops the socket for at least 5 seconds: the pill reads Reconnecting at once, the banner appears after 3 seconds, a hold made while the socket is down is missed, and when connections pass again the page shows "Back online. Jobs refreshed at …", requests the job list again and shows the hold, without a toast. With every connection refused, "Live updates paused" appears after 30 seconds, and Refresh now reconnects and refetches. This test waits the 30 seconds in real time, because `page.clock` would apply to every page of the shared administrator context.
- `inspection.spec.ts` checks the inspection report.
  - Priya Natarajan, through her API session, saves 6 of the 10 answers of a "Rooftop HVAC service" job, and her submission is refused with `inspection.incomplete`.
  - With the job's page and its report open, she saves the rest and submits. Both pages show the completion within 5 seconds without a reload: the job page with the toast and an "Inspection report" link, the report with its new state.
  - The report then shows the result counts, the time on site (the difference between the whole minutes of the start and the completion), each reading with its value under its label, the notes, both lines of the technician notes and the status history, with no editable control. A later save is refused with `inspection.locked`.
  - A second test reads three reports and checks the not-found state of the report route. The submitted and not started reports belong to demo jobs. The not submitted report belongs to the lowest-numbered job in progress, outside the test runs, with saved answers that are not submitted: JOB-1042 in the demo data, until someone submits it. The test also opens a completed job's report from its title in the list.
- `accessibility.spec.ts` runs the axe checks for the WCAG 2.2 AA tags on each route and state at 1280 and 375 px. The states include the assign panel, a job on hold with its reason, the list filtered to In progress, a job's history after a start, a hold and a resume, and the live states: the Live pill with an event toast and a flashed row, the Reconnecting banner and Back online. They also include the inspection report: submitted, not submitted, not started, loading, failed to load and job not found. The spec also checks reflow at 320 px (a 500-character hold reason, the Reconnecting banner and the inspection report included), reduced motion, forced colours, reduced transparency, and creating and assigning a job by keyboard alone. The dashboard, technicians, cancellation and client error specs run the same axe check on their own pages and states, listed below; a run makes 81 audits.
- `dashboard.spec.ts` checks the dashboard at `/` against the two API answers the page itself received: the summary for the browser's local day and `GET /api/technicians`. The hero sentence and figures, the six status tiles with their link names and targets, Needs a technician with its Assign links and "View all", Live activity with its job links, and the workload table, or the people list below 640 px. A held request shows each part's loading form; a 500 shows each part's error form, with one Try again for the summary and one for the workload, focus moving as specified; an empty summary and an empty technician list show the empty forms. At 320 px no time or date breaks across lines, and a schedule that crosses midnight ends inside its column. A second test uses the real notification service: a technician's start adds a "New" activity entry, shown for 12 seconds, and updates the tiles without a reload; with the socket dropped, a hold is missed, and the reconnect requests the summary and the technicians again, shows the hold and marks nothing new.
- `technicians.spec.ts` checks `/technicians` against `GET /api/technicians`: every technician with email, trade, shift, current or next job and phone; the shift filters and a search by name, trade or three or more digits of a phone number, with the chip counts unchanged and the filters in the address; the people list with its `tel:` links at 768 and 375 px, and no email past its row at 320 px; "Inactive" for a technician whose API answer says inactive, on this page and on the dashboard; and the loading, failure and empty forms.
- `cancel.spec.ts` cancels only jobs the test creates. The dialog shows the job's status and technician and keeps a typed reason when it is dismissed with Keep job or Esc; a backdrop click does nothing; a blank reason is refused before any request. With the socket held closed so the page cannot refetch, the test reaches each answer of the API for real: 409 `concurrency.conflict` after a reassignment (the dialog stays open, refreshed, with the reason), 200 for a job cancelled meanwhile with another reason (the conflict banner above the cancelled notice, in view at 375 × 800), 409 `job.invalid-transition` for a job completed meanwhile, and 400 `reason` for a control character. A cancellation then leaves the job read-only with its notice, focus on the title and no toast for the user's own action, and the assigned technician's start gets 409 `job.invalid-transition`.
- `client-errors.spec.ts` raises an unhandled rejection in the signed-in dashboard. The error toast shows "Reference" and the id of the report's 202 answer, never the error text; the test then finds exactly one `client-error.reported` Warning with that `CorrelationId`, `Source` `web` and the administrator's user id in `logs/api/`, without the email address the error carried. The test asserts the number of matching entries and then one property at a time, so a failure reports a count or a single value and never prints a log entry. A repeat of the same error adds no toast and no report. On the sign-in page, signed out, the toast has no reference and nothing is sent.
- `responsive.spec.ts` checks every page at 375, 768 and 1280 px for horizontal page scroll: sign-in, the dashboard, jobs, job detail, job detail with the assign panel and with the cancel dialog open, a cancelled job, new job, technicians, the inspection report and not found. The jobs table scrolls inside its region at each width, the technicians and workload tables give way to their people lists on narrow widths, and on a cancelled job at 320 px no time or date breaks across lines.
- `security.spec.ts` checks, on both projects, that `/` carries the Content Security Policy and the six other server-level security headers with the values in `e2e/support/headers.ts`, that no `Server` header names a version, that Geist and Geist Mono load from the same origin and that `/api` answers carry `Cache-Control: no-store`. Its tests tagged `@compose-only` check nginx itself:
  - the seven headers on 200, 304 and 404 answers; `no-cache` on `index.html` and deep links, `public, max-age=31536000, immutable` on hashed assets (also on a 304), and `no-cache` on a missing asset;
  - the technicians search logged by nginx as `/index.html`, without its query;
  - a technician's `POST /api/auth/socket-tickets` refused with 403;
  - `/health` and `/internal` in 19 plain, percent-encoded and dot-segment forms, among them `/api/..%2Fhealth/ready` and `/socket.io/%2e%2e/internal/job-events`, sent without normalization: each gets nginx's own 404 page, nginx logs it under the decoded path, and no API or notification service line holds its correlation id, while direct requests to both services prove that such lines would be found. Two double-encoded forms reach the API or the notification service, which answer 404 and 400 without serving a health check or a job event, and a path that climbs above the web root gets nginx's own 400;
  - a Socket.IO handshake with a foreign `Origin` refused, and one with the dashboard's origin upgraded.
- `design-qa.spec.ts` captures the sign-in error, the dashboard, jobs with an error toast, a job with the cancel dialog open, new job with field errors, the inspection report, technicians, job not found and page not found at 375, 768, 1280 and 1440 px, on one page resized between captures, into `test-results/design-qa/` for design review. The toast and dialog states are captured at window size, because a full-page capture leaves the dialog's backdrop over the first screen only; the other states are captured full page. It checks nothing beyond the automatic checks and the state it waits for, and spends no sign-in of its own.
- Tests run one at a time against one shared stack. The `compose` project, which the CI workflow runs, never retries a failed test; the `host` project, which runs on the workstation, retries only when `CI` is set. A failed test restarts the worker, whose new sessions count against the sign-in budget, so after a failure the remaining tests fail on the budget, and a retry would fail the same way; the first failure in the list is the one to read.

The specs have their own TypeScript project. To type-check them and list the tests of each project without a running stack:

```
cd src/FieldService.Web
npx tsc -p e2e/tsconfig.json
npx playwright test --list --project=host --reporter=list
npx playwright test --list --project=compose --reporter=list
```

### Against the Compose stack

The `compose` project runs the same specs, and those tagged `@compose-only`, against the containers of the Compose `full` profile: nginx serving the built dashboard on 127.0.0.1:8081, the API in Staging with the demo data, the notification service and PostgreSQL. It needs Docker and the `infrastructure` env file that `create-dev-env.mjs` writes ([deployment.md](deployment.md)). Stop an API running on the workstation first, because the Compose `api` service publishes the same port, 5080.

```
docker compose -f infrastructure/docker-compose.yml --profile full up -d --build --wait
curl --fail --silent --retry 60 --retry-delay 2 --retry-all-errors http://127.0.0.1:5080/health/ready
cd src/FieldService.Web
npm run test:e2e
cd ../..
docker compose -f infrastructure/docker-compose.yml --profile full down
```

- `up --wait` can return while the API is still restarting after a refused start, so the readiness poll is the gate before the tests.
- The Compose database keeps its own demo data in the `fieldservice-pgdata` volume, seeded at its first start. Runs add their jobs to it as they do on the workstation; the specs need no fresh seed.
- Every client on the workstation reaches the containers through Docker's gateway, one address, so all sign-ins of a run share one rate-limit partition of 10 a minute. A run's 7 sign-ins happen in its first minutes, so consecutive runs stay inside it; the teardown prints the busiest minute.
- The project keeps a trace, a screenshot and a video only of failed tests, and Playwright writes an error context, `error-context.md`, for each of them. A failed test's trace can hold the run's access tokens, refresh cookies and socket tickets, and the demo password when the test entered it in the sign-in form or signed a technician in through the API. The page snapshot in an error context lists the value of every form field, the password field included. The report scan deletes each file that holds such a value and fails the run, naming each deleted file and its kind. Treat what remains in these folders as private and delete it after use. Every session of the run is signed out by the teardown.

The `e2e` job of the CI workflow runs `npm run test:e2e` on every pull request and every push to `main`, after the `api`, `notifications` and `web` jobs. It builds and starts the `full` profile from the commit with throwaway values from `create-dev-env.mjs --ci`, which also exports `E2E_DEMO_PASSWORD`, polls `/health/ready`, and sets `E2E_BASE_URL` to `http://127.0.0.1:8081` and `E2E_LOGS_DIR` to `../../logs`. The stack and its volume are removed at the end.

- After the main run, the job lists the tests tagged `@quarantine`. Only when the list holds at least one test does it run them, in a step that never fails the job; with none, the job log says that the quarantine run is skipped.
- The `e2e-diagnostics` artifact is uploaded only when a step fails, a failed quarantine run included, and is kept for 3 days. It holds `playwright-report/`, `test-results/` without `sentinels/`, the API and notification service log files under `logs/`, and `compose.log` with the console output of the `migrate`, `api`, `notifications` and `web` containers. After a failed quarantine run, the report folders are that run's.
- The `db` container's console never goes into the artifact. When a step before the quarantine run fails, the job log shows it, beside the last 100 lines of the other four containers; the job masks the run's secrets in its log.
- In the artifact, as on a workstation, a failed test's trace or error context can hold the run's demo password and tokens. The report scan, when it finishes, has deleted every such file before the upload, and the failed run's output names each one. The trace of a failed test that used a signed-in page records the page's requests with their access token, so it is among them. A file that the scan cannot read or parse is not deleted: the output names it as a file that could not be scanned and the run fails, so delete that file by hand, and in CI the artifact, before sharing anything from these folders. The screenshot, the video, an error context that holds no such value, the run's output and the logs remain to read.

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
- **Playwright:** the tag `@quarantine`, which both scripts exclude. The CI `e2e` job runs the tagged tests in a step of their own that never fails the job, and only when at least one test carries the tag.
- **Vitest and Node:** no quarantine; the test is fixed or removed the same day.

## Verification summary

The workstation commands on this page were run on 2026-10-23 and 2026-10-24, on macOS 26.5 (Apple Silicon) with .NET SDK 10.0.402, workload set 10.0.402, Node 26.12.0 and PostgreSQL 17.11, from the folders named above, with the results and coverage folders pointed outside the repository. Every row is from the final run of 2026-10-24, on the code that includes the dashboard, job cancellation and client error work and the last changes to the client error report and to the technician app's layouts.

| Suite | Result |
|---|---|
| API | 1974 of 1974 passed in Debug, and again limited to two processors; both `dotnet format` checks passed, the Release build of the API, rebuilt from scratch, had no warning, and the model has no pending change |
| Technician app | 997 of 997 passed in Debug; the Android Debug build, rebuilt from scratch, succeeded with no warning |
| Notification service | 464 of 464 passed in 50 suites after a clean install of both packages; lint, formatting, both type-checks and the build passed |
| Web dashboard, unit | 1357 of 1357 passed in 29 files, among them the error handling tests; creating the API client, lint, formatting, the type-check with the end-to-end specs, and the build passed |
| Web dashboard, end to end | The `host` project against `vite preview`, with the API and the notification service running: 34 of 34 passed in 11 files in each of three runs of about three and a half minutes. Each run had no Content Security Policy violation, no handshake URL with an extra query key, 81 axe audits without a violation, no match in the log search of the API and notification service log files, and all 5 sessions signed out. In each run the live status changes reached the open jobs page and dashboard without a reload, and the client error reference matched its log entry. Against the Vite dev server 32 of 34 passed, the header test failing there as described above and the technicians test after it on the sign-in budget; the technicians test passed on its own there. Both projects list 34 tests in 11 files |
| Repository checks | Both scripts passed |

Not run on that date: the Release forms with coverage of the other CI jobs and the quarantine steps; the Windows commands, because no Windows host was available; and the `compose` project and the Compose database, because no container runtime was available.

On 2026-10-25 the end-to-end suite ran against the Compose `full` profile (Docker Engine 29.9.2, images built from the working tree) and against `vite preview`, on the code with the Compose specs, the design QA captures, the container log search and the report scan. The first runs of that day found a defect: a navigation to the job page's own address reset its document title to the generic page title. The rows below are from the runs after its fix, with the dashboard image rebuilt to include it, using the commands on this page without any filter.

| Suite | Result |
|---|---|
| Web dashboard, end to end, `compose` | 40 of 40 passed in 12 files in each of three consecutive runs of 3.8 minutes. Each run had no Content Security Policy violation, no match in the search of the two log folders and the four container consoles (each with its positive control found), no correlation id outside the allowed form in nginx's log, and nothing secret in the report output. All 5 sessions were signed out, and the busiest minute held 6 sign-ins, 23 or 24 session refreshes, 22 or 23 socket tickets and at most 209 administrator requests. In the earlier runs of that day, live status changes reached the open jobs page in under a second, and with the notification service stopped and started, the dashboard showed "Back online" 5.0 to 5.5 seconds after the service was listening again |
| Web dashboard, end to end, `host` on `vite preview` | 36 of 36 passed in 12 files in 3.8 minutes, with the same checks clean for the two log folders |
| Web dashboard, unit | 1368 of 1368 passed in 29 files |
| Web dashboard, lint, formatting and type-check | All passed |

Later on 2026-10-25 the report scan was changed to delete the files that hold a match. Scratch tests run through the same reporter checked it: a planted password entered into a form and sent in an API sign-in, and a planted token in the response and in an attachment nested two archives deep. In one run that kept every trace and in one that kept them only for failed tests, the run failed, and the scan deleted exactly the 7 files that held a planted value: traces, an error context, their copies in the report data and the nested archive. An independent search with `unzip`, made on copies taken just before the scan, found the same 7 files. All 8 screenshots and videos stayed. A control run without a planted value passed and deleted nothing. The `compose` project then passed 40 of 40 in 3.8 minutes, with no Content Security Policy violation, every log source clean with its positive control found, and nothing deleted by the report scan; lint, formatting and the type-check passed.

The global teardown then gained its check of the reporters. Against the Compose stack, a run that replaced the reporters and selected no test printed the warning and failed, and one that named the report scan among its replacement reporters passed. The `compose` project then passed 40 of 40 in 3.8 minutes, with the report scan among the reporters, no Content Security Policy violation, every log source clean with its positive control found, and nothing deleted by the report scan; lint, formatting and the type-check passed.

Later on 2026-10-25, on the code with the technician app's screen-reader and keyboard changes, the technician app passed 1024 of 1024 in Debug, and the Android Debug build, rebuilt from scratch, succeeded with no warning. A manual check on an Android 16 emulator with TalkBack, signed in against the Compose stack, confirmed what these tests describe:
- the page behind a closed sheet is read again;
- focus returns to the button that opened the sheet, with the on-screen keyboard open or closed;
- a touch on an icon inside a control reads that control;
- the My jobs title and day groups are read as headings.

The log search of that session found no password, token, ticket or personal data.

On 2026-10-26, on the code with the technician app's Windows crash fix and its keyboard, Narrator, layout and focus changes, the technician app passed 1168 of 1168 in Debug. The Android Debug build, rebuilt from scratch, and the Windows Debug build both succeeded with no warning. Manual checks in a Windows 11 virtual machine and on an Android 16 emulator, each signed in against the Compose stack, confirmed:
- the Windows app no longer crashes when a button's enabled state changes, and every status change is sent;
- a job runs from Start job to a submitted inspection on both platforms, on Windows at phone width and in the two-pane layout, and the dashboard and the job page show each change live without a reload;
- on Windows, an open sheet keeps keyboard and Narrator focus inside it and Esc closes it, Narrator reads the job cards and day groups, and keyboard focus shows as one ring outside the control;
- on Windows, crossing 900 wide closes an open sheet without sending a change and moves keyboard focus to the account button;
- on Windows, the app shows its offline state while Windows reports no internet access;
- on Android, both My jobs segments stay usable while a list loads, a clearing banner is not read again, keyboard mode shows one ring on rows and cards, and the account sheet keeps Tab inside it and stays open while a sign-out runs.

The log search of those sessions found no password, token, ticket or personal data.

One defect found in these checks was still open on that date: on Windows, the app crashed in the two-pane layout when the text size was 125 % or larger, and in phone-layout windows about 410 px wide or narrower. Some smaller focus and spacing differences found in the same checks were also still open.

On 2026-10-27, on the code with the fix for that crash and for the remaining list, focus and layout defects of the technician app, every suite on this page ran again with the commands above, without filters:

| Suite | Result |
|---|---|
| API | 2126 of 2126 passed in Debug, and again limited to two processors; the contract tests alone passed 106 of 106 and the security tests alone 458 of 458; both `dotnet format` checks passed, the Release build had no warning, and the model has no pending change |
| Technician app | 1265 of 1265 passed in Debug; both `dotnet format` checks passed; the Android Debug build and the Android Release build with an `https` API address, each rebuilt from scratch, succeeded with no warning |
| Notification service | 464 of 464 passed in 50 suites after a clean install of both packages; lint, formatting, both type-checks and the build passed |
| Web dashboard, unit | 1368 of 1368 passed in 29 files; creating the API client (with an unchanged result), lint, formatting, the type-check with the end-to-end specs, and the build passed |
| Web dashboard, end to end, `host` on `vite preview` | 36 of 36 passed in 12 files in 3.8 minutes, with no Content Security Policy violation, every axe audit without a violation, no match in the search of the two log folders, all 5 sessions signed out, and nothing secret in the report output |
| Web dashboard, end to end, `compose` | 40 of 40 passed in 12 files in 3.8 minutes against the `full` profile, with no Content Security Policy violation, no match in the search of the two log folders and the four container consoles (each with its positive control found), all 5 sessions signed out, and nothing secret in the report output |
| Dependencies | `npm audit --omit=dev --audit-level=moderate`, `npm audit --audit-level=high` and `npm audit signatures` passed in all three npm packages, with no vulnerability; `dotnet list package --vulnerable --include-transitive` found no vulnerable package in the four .NET projects |
| Repository checks | Both scripts passed |

The CI workflow passed all eight jobs, `e2e` included, on the same commit.

Manual checks on an Android 16 emulator and in a Windows 11 virtual machine, each signed in against the Compose stack, confirmed on the code with these fixes:
- the Windows app no longer crashes at text sizes from 125 % to 225 % in either layout, in windows down to the narrowest that Windows allows, or when crossing 900 wide;
- on Android, My jobs stays stable after a submission made from a list scrolled to its end, and after a job is cancelled elsewhere;
- on Windows, keyboard focus stays on Update status after each status change in the two-pane layout, and Narrator reads the day-group heading that takes focus after a banner leaves;
- widening the window past 900 with a job open at phone width shows that job in the two panes, with its card selected and in view;
- at large text sizes the two-pane job actions wrap to a second row instead of running past the panel.

The log search of those sessions found no password, token, ticket or personal data. The Windows crash described above is fixed. Some smaller spacing and focus differences found in these checks remain open.

On the same day, a technician account with no jobs was signed in on the Android emulator: both lists showed the empty state, and TalkBack read its title as a heading, then its message, then Refresh.
