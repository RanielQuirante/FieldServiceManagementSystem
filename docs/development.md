# Development

This guide sets up a workstation to build, run and test FieldService on macOS or Windows: the API, the notification service, the web dashboard and the technician app. Settings and environment variables are described in [configuration.md](configuration.md), log files in [logging.md](logging.md), and the database roles, the bootstrap script and the migrations in [database.md](database.md).

Run every command from the repository root unless a step says otherwise.

## How the applications run locally

| Application | Folder | Started with | Address |
|---|---|---|---|
| API | `src/FieldService.Api` | `dotnet watch`, or the VS Code `API` configuration; environment `Development` | http://localhost:5080 |
| Notification service | `src/FieldService.Notifications` | `npm run dev` (Node with `--watch`, reading the package's `.env` file) | http://127.0.0.1:5090 |
| Web dashboard | `src/FieldService.Web` | `npm run dev` (Vite dev server), or `vite preview` of the built app | http://localhost:5173 or http://127.0.0.1:5173 |
| Technician app | `src/FieldService.Mobile` | Debug build on the Android emulator or on Windows | Calls the API at http://10.0.2.2:5080 (Android emulator) or http://localhost:5080 (Windows) |
| PostgreSQL 17 | `infrastructure/` | The local server, or the Compose `db` service | 127.0.0.1:5432, or 127.0.0.1:5433 for Compose |

- The Vite dev server proxies `/api` to the API and `/socket.io` to the notification service, so the browser talks to one origin.
- After each committed create, assignment or status change, the API posts a signed job event to the notification service at http://127.0.0.1:5090/internal/job-events, the `Notifications:BaseUrl` of `appsettings.Development.json`. The notification service broadcasts it to the signed-in administrators' dashboards, which then refetch the job. The two services share the socket-ticket key and the webhook secret, which `create-dev-env.mjs` writes into the API's user-secrets store and the notification service's `.env` file alike ([configuration.md](configuration.md#values-shared-with-the-api)).
- `vite preview` serves the production build on the same address with the same proxies and the production security headers, including the Content Security Policy. The dev server sends none of them, because hot reload injects inline code.
- Each service uses the same host port wherever it runs. Run a service either on the host or in Compose, never both at once.

## Ports

Every published port binds to loopback; nothing listens on a LAN interface.

| Host port | Bound to | Service | Notes |
|---|---|---|---|
| 5432 | 127.0.0.1 | Local PostgreSQL 17 (Homebrew on macOS, the PostgreSQL installer on Windows) | Compose never publishes 5432 |
| 5433 | 127.0.0.1 | Compose `db` (container port 5432) | Set by `DB_HOST_PORT` |
| 5080 | `localhost` (127.0.0.1 and ::1) on the host; 127.0.0.1 in Compose (container port 8080) | API | The Android emulator uses `10.0.2.2:5080`, a USB device uses `adb reverse tcp:5080 tcp:5080`, and Windows uses `localhost:5080` |
| 5090 | 127.0.0.1 (container port 5090) | Notification service | Browsers reach it only through a proxy at `/socket.io`; `/internal/job-events` is called only by the API |
| 5173 | 127.0.0.1 | Vite dev server, or `vite preview` in its place | `strictPort` is on in both, so a busy port fails instead of moving to 5174, which would break the allowed origin lists. Only one of the two runs at a time |
| 8081 | 127.0.0.1 (container port 8080) | nginx web image of the Compose `full` profile | Base URL of the Playwright `compose` project |
| 8080 | Container network only | `api` and `web` containers | Never published as 8080 |
| 9323 | 127.0.0.1 | `npx playwright show-report` | Playwright's default, on demand |

**Ports to avoid.** 5000 and 7000 belong to the macOS AirPlay Receiver. Keep 5432 for the local PostgreSQL server.

**Windows.** Hyper-V and WSL can reserve dynamic port ranges. Check them with `netsh interface ipv4 show excludedportrange protocol=tcp`. If 5080, 5090 or 5173 falls inside a reserved range, move the whole set together: `--urls` on `dotnet run`, `PORT` in the notification service's `.env` and `REALTIME_PROXY_TARGET`, `API_PROXY_TARGET`, and `-p:ApiBaseUrl` for the technician app.

## Prerequisites

### macOS (Apple Silicon)

| Tool | Version | Install and notes |
|---|---|---|
| .NET SDK | 10.0.402, with runtime 10.0.13 | Microsoft's installer package `dotnet-sdk-10.0.402-osx-arm64.pkg`, or the Homebrew `dotnet-sdk` cask while it still ships SDK 10. Both install to `/usr/local/share/dotnet`. Leave `DOTNET_ROOT` unset. |
| .NET MAUI workload | Workload set 10.0.402 | `sudo dotnet workload install maui-android --version 10.0.402`. No iOS or Mac Catalyst workload is needed, and neither is Xcode. |
| JDK | Microsoft Build of OpenJDK 21 | `brew install --cask microsoft-openjdk@21`. Android builds fail with `XA0030` on a newer JDK. |
| Android SDK | API level 36 | Under `$HOME/Library/Android/sdk`: `cmdline-tools;latest`, `platform-tools`, `platforms;android-36`, `build-tools;36.0.0`, `emulator` and `system-images;android-36;google_apis;arm64-v8a` |
| Android emulator | One AVD on API 36 | For example `avdmanager create avd -n fsm-api36 -k "system-images;android-36;google_apis;arm64-v8a" -d pixel_9` |
| Node.js | 26.12.0 with npm 11.20.0 | `.nvmrc` holds the version. Each package sets `engine-strict`, so older versions are refused. |
| PostgreSQL | 17, server and `psql` | `brew install postgresql@17`, then `brew services start postgresql@17`. It listens on 127.0.0.1:5432, your macOS account is its superuser, and loopback connections use trust authentication. The bootstrap script needs `psql` 15 or later. |
| Docker Desktop | 4.95.0 with Compose 5.6.0 | Optional. Needed only for the Compose database and the Compose profiles. |
| VS Code | Current release | With the recommended extensions of `.vscode/extensions.json` |
| Git | Current release | `.gitattributes` keeps LF line endings in the working tree |

**Shell profile (`~/.zshrc`).** Remove any line that points `DOTNET_ROOT` into `/opt/homebrew/Cellar/dotnet/`, then add:

```
export JAVA_HOME="$(/usr/libexec/java_home -v 21)"
export ANDROID_HOME="$HOME/Library/Android/sdk"
export PATH="$PATH:$ANDROID_HOME/cmdline-tools/latest/bin:$ANDROID_HOME/platform-tools:$ANDROID_HOME/emulator"
```

**Keeping the .NET SDK on 10.0.4xx.** `global.json` asks for SDK 10.0.402 with `rollForward` set to `latestPatch`, so only a 10.0.4xx SDK at or above 10.0.402 builds the repository. The Homebrew `dotnet-sdk` cask follows the newest .NET release: once it moves to .NET 11, expected in November 2026, upgrading it removes SDK 10, and the cask does not declare `auto_updates`, so a bare `brew upgrade` upgrades it as well. From then on:

- never run `brew upgrade --cask dotnet-sdk` or a bare `brew upgrade`; upgrade named formulae only;
- take 10.0.4xx patches, and any reinstall, from Microsoft's versioned package `dotnet-sdk-10.0.4xx-osx-arm64.pkg` at `https://builds.dotnet.microsoft.com/dotnet/Sdk/<version>/`, which installs side by side in `/usr/local/share/dotnet`;
- after any SDK install, check the workloads again (see [Verify the toolchain](#verify-the-toolchain)).

**No global dotnet-ef.** `dotnet tool list -g` should list no `dotnet-ef`. The repository pins dotnet-ef 10.0.13 as a local tool in `.config/dotnet-tools.json`, and `dotnet tool restore` installs it.

### Windows 11 (x64 or ARM64)

Run these commands in PowerShell.

| Tool | Install |
|---|---|
| .NET SDK 10.0.402 | `winget install Microsoft.DotNet.SDK.10 --version 10.0.402`, which keeps the SDK in the 10.0.4xx band that `global.json` requires. Then, in an elevated terminal: `dotnet workload install maui-windows maui-android --version 10.0.402` |
| Node.js 26.12.0 | The installer from nodejs.org, or `winget install OpenJS.NodeJS --version 26.12.0` |
| PostgreSQL 17 | `winget install PostgreSQL.PostgreSQL.17`. Keep port 5432, note the `postgres` password, add `C:\Program Files\PostgreSQL\17\bin` to `PATH`, and set `listen_addresses = 'localhost'` |
| Microsoft OpenJDK 21 | `winget install Microsoft.OpenJDK.21`, then set `JAVA_HOME` to its folder |
| Git | `winget install Git.Git`. `.gitattributes` keeps LF line endings whatever `core.autocrlf` is set to |
| VS Code | `winget install Microsoft.VisualStudioCode`, plus the recommended extensions |
| Docker Desktop (optional) | `winget install Docker.DockerDesktop`, with the WSL 2 backend |

**Long paths.** Android builds create long paths. Clone to a short path such as `C:\src\fieldservice`, run `git config --global core.longpaths true`, and enable long paths once in an elevated PowerShell:

```
New-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem" -Name LongPathsEnabled -Value 1 -PropertyType DWORD -Force
```

**Android SDK and emulator** (optional on Windows):

```
dotnet build src/FieldService.Mobile/FieldService.Mobile.csproj -t:InstallAndroidDependencies -f net10.0-android -p:AndroidSdkDirectory="$env:LOCALAPPDATA\Android\Sdk" -p:JavaSdkDirectory="$env:JAVA_HOME" -p:AcceptAndroidSDKLicenses=True
```

- Set `ANDROID_HOME` to `%LOCALAPPDATA%\Android\Sdk`.
- Install the `emulator` package and `system-images;android-36;google_apis;x86_64` with the SDK's `sdkmanager`; use the `arm64-v8a` image on ARM64 hardware.
- Turn on the Windows Hypervisor Platform.

### Verify the toolchain

Open a new terminal, so that the profile changes apply, and check:

| Command | Expected |
|---|---|
| `dotnet --version` | `10.0.402`, or a later 10.0.4xx patch |
| `dotnet workload list` | Workload version `10.0.402`, with `maui-android` (and `maui-windows` on Windows) |
| `dotnet tool list -g` | No `dotnet-ef` |
| `node --version` and `npm --version` | `v26.12.0` and `11.20.0` |
| `psql --version` | `psql (PostgreSQL) 17.x` |
| `pg_isready -h 127.0.0.1 -p 5432` | `accepting connections` |
| `java -version` | OpenJDK 21, Microsoft build |
| `adb version` | Android Debug Bridge |
| `emulator -list-avds` | The AVD you created |

### .NET MAUI workload versions

The technician app builds against the .NET MAUI workload from workload set 10.0.402. Installing with `--version 10.0.402` selects that set, and the set fixes the version of every workload manifest it contains, so every machine and build runner that installs it builds with the same packs. `global.json` sets no `workloadVersion`.

| Item | Version |
|---|---|
| Workload set | 10.0.402 |
| .NET MAUI manifest (`maui-android`, `maui-windows`) | 10.0.110 |
| .NET MAUI SDK pack (`Microsoft.Maui.Sdk`) | 10.0.110 |
| Android manifest and SDK pack (`Microsoft.Android.Sdk.<host>`, where the host is `Darwin`, `Linux` or `Windows`) | 36.1.118 |
| Android platform of the `net10.0-android` target | API level 36 (`TargetPlatformVersion` 36.0) |
| `Microsoft.Maui.Controls` and the `Microsoft.Maui.*` packages it brings | 10.0.110, from `Directory.Packages.props` |

- `dotnet workload --info` prints the workload version and each installed manifest. A detailed restore of the app, `dotnet restore src/FieldService.Mobile/FieldService.Mobile.csproj -v d`, names the packs it resolves, under `packs/Microsoft.Maui.Sdk/10.0.110` and `packs/Microsoft.Android.Sdk.<host>/36.1.118`.
- If a restore names other pack versions, the installed set has changed: run `dotnet workload update --version 10.0.402`, with `sudo` on macOS, then check again.
- Install, update or repair workloads only with `--version 10.0.402`, so that the set stays the same: for example `sudo dotnet workload install maui-android --version 10.0.402` on macOS.
- If `dotnet workload list` no longer shows `maui-android` after an SDK install, run the same command again.
- The API, the notification service and the web dashboard need no workload.

## First-time setup

### 1. Restore tools and packages

```
dotnet tool restore
npm ci --ignore-scripts --prefix src/FieldService.Notifications
npm ci --ignore-scripts --prefix tests/FieldService.Notifications.Tests
npm ci --ignore-scripts --prefix src/FieldService.Web
```

`dotnet tool restore` installs the pinned dotnet-ef. Each npm package has its own lock file and an `.npmrc` with `engine-strict=true` and `ignore-scripts=true`.

### 2. Create the local environment and bootstrap the database

On macOS:

```
node infrastructure/scripts/create-dev-env.mjs --bootstrap
```

On Windows, give psql the `postgres` password through `PGPASSWORD` for this terminal only. The script then also writes the test admin connection with that password, and never prints it:

```
$env:PGPASSWORD = "<postgres password>"
node infrastructure/scripts/create-dev-env.mjs --bootstrap --admin-user postgres
Remove-Item Env:PGPASSWORD
```

Without `PGPASSWORD`, psql asks for the password, and the script prints the `dotnet user-secrets set` command that stores the test admin connection instead of writing it.

The script creates every local secret once and writes them where each application reads them. It prints the paths, store ids and key names it writes, and never a value.

| Output | Contents |
|---|---|
| `infrastructure/.env` | The ten Compose variables |
| `src/FieldService.Notifications/.env` | The thirteen notification service variables, with the development values |
| `src/FieldService.Web/e2e/.env` | `E2E_DEMO_PASSWORD` only, which Playwright reads |
| User-secrets store `fieldservice-api` | The two connection strings, both signing keys, the webhook secret and the demo password |
| User-secrets store `fieldservice-api-tests` | The test admin connection and both database role passwords |
| `logs/api/` and `logs/notifications/` | The log folders |

- The env files are created with mode 0600 on macOS and Linux. Git ignores all three, and `check-tracked-files.mjs` fails if one is ever tracked.
- `--bootstrap` then runs `infrastructure/postgres/bootstrap.sql` on 127.0.0.1:5432 through `psql`. The script creates the roles `fieldservice_migrator` and `fieldservice_app`, or sets their passwords again when they exist, and creates the `fieldservice` database when it is missing. It needs `psql` on `PATH`.
- Running the script again without flags creates nothing new. It reads the existing values first, reports each output as `unchanged`, and ends with `create-dev-env: no changes; every output already holds its values`. If one output has drifted from the others, the same run writes it back in step.

**What the API needs from it.** In Development the API refuses to start unless it finds five values, each valid by the rules in [configuration.md](configuration.md#startup-checks). Locally it reads them from the user-secrets store `fieldservice-api`:

| Key | Why Development needs it |
|---|---|
| `ConnectionStrings:FieldService` | Every environment needs it |
| `Auth:AccessTokenKey` and `Auth:SocketTicketKey` | Every environment needs both signing keys, also when the notification service is not running |
| `Notifications:WebhookSecret` | `appsettings.Development.json` sets `Notifications:BaseUrl` |
| `Seed:DemoPassword` | `appsettings.Development.json` turns on `Seed:DemoData` |

The script writes all five, plus `ConnectionStrings:Migrations` for `dotnet ef`. It creates the two keys and the webhook secret from fresh random bytes, distinct from each other, so none can be a published test key or a patterned key. Never type or paste one of these values by hand. `node infrastructure/scripts/start-dev.mjs --check` confirms that the store holds every key, by name and without printing a value.

### 3. Apply the migrations

Run the VS Code task `db: migrate`, or:

```
dotnet ef database update --project src/FieldService.Api
```

`dotnet ef` connects as `fieldservice_migrator` through `ConnectionStrings:Migrations` in the API's user-secrets store. The API itself refuses to start in Development while a migration is pending, and its message names the command above.

### 4. Start the stack

Use the VS Code Run and Debug configuration "Full stack", or:

```
node infrastructure/scripts/start-dev.mjs
```

Both are described in [Running the stack](#running-the-stack).

At its first start on the new database, the API seeds the demo data, after its configuration and migration checks and before it listens: one administrator, six technicians, 20 jobs, JOB-1036 to JOB-1055, and three inspections ([database.md](database.md#demo-data)). It logs `seed.demo-data.completed` with row counts and the next job number, so the first job created afterwards is JOB-1056. Later starts find the users already there, write nothing and log `seed.demo-data.skipped` with the reason `demo data present`. To start again from the demo data, reset the database ([Resets](#resets)).

### 5. Check that it runs

```
curl -s http://localhost:5080/health/ready
curl -s http://127.0.0.1:5090/health/ready
```

Both print `Healthy`. Then open http://localhost:5173, which leads to the sign-in page. After you sign in, the live indicator in the dashboard's top bar reads "Live" once the dashboard's socket to the notification service is open ([Checking the running stack](#checking-the-running-stack)).

**Signing in.** Sign in as the demo administrator, `dana.whitfield@fieldservice.example`, with the demo password. Every demo account shares that password, and the dashboard admits administrators only: a technician account gets the message that technicians sign in from the FieldService Tech app. No setup step prints the password, and no tracked file holds it. To see it, run this yourself, in your own terminal:

```
node infrastructure/scripts/create-dev-env.mjs --show-demo-password
```

It prints the password and nothing else. Keep it out of shared terminals, screen recordings and issue reports.

In Development the API serves its OpenAPI document at http://localhost:5080/openapi/v1.json; open it in any OpenAPI viewer. The API ships no API explorer page, so `/swagger` answers 404 ([ADR 0017](decisions/0017-api-explorer-in-development.md)).

### 6. Run the technician app

On macOS, start the emulator with `emulator -avd fsm-api36` (or your AVD's name), then run the app from the .NET MAUI extension with the Android target, or:

```
dotnet build src/FieldService.Mobile/FieldService.Mobile.csproj -t:Run -f net10.0-android
```

The Debug build reaches the API at `http://10.0.2.2:5080`.

On Windows, the Windows head uses `http://localhost:5080`:

```
dotnet build src/FieldService.Mobile/FieldService.Mobile.csproj -t:Run -f net10.0-windows10.0.19041.0
```

On ARM64 hardware, add `-p:RuntimeIdentifierOverride=win-arm64`.

### 7. One-time VS Code steps

- Run "TypeScript: Select TypeScript Version" and choose "Use Workspace Version", so Vue files use the workspace's TypeScript 6.0.x. This writes `.vscode/settings.json`, which stays local and ignored.
- Run ".NET MAUI: Configure Android" and point it at the Android SDK and JDK.

## Running the stack

### VS Code: "Full stack"

The compound launch configuration "Full stack" first runs the task `env: check` (`start-dev.mjs --check`). When the check finds a problem, it lists each one with its fix and exits 1, and VS Code reports the failed task before any application starts. Otherwise the compound starts three configurations together:

| Configuration | What it runs | Ready when |
|---|---|---|
| `API` | Builds the API with the task `build: api`, then debugs `src/FieldService.Api/bin/Debug/net10.0/FieldService.Api.dll` with the launch profile `http` (http://localhost:5080, environment `Development`). Its working directory is the project folder, so `Logs:Directory` (`../../logs/api`) resolves to the repository's `logs/api`. It reads its keys from the user-secrets store `fieldservice-api` | The Debug Console shows `Now listening on: http://localhost:5080` |
| `Notifications` | Debugs `src/FieldService.Notifications/src/main.ts` with Node's `--env-file-if-exists=.env`, in the package folder, so it reads the thirteen development variables of `src/FieldService.Notifications/.env`, the same flag that `npm run dev` uses. Node runs the TypeScript directly; type stripping keeps line and column positions, so breakpoints work without source maps | Its terminal shows `server.listening` with `host: "127.0.0.1"` and `port: 5090` |
| `Web dev server` | `npm run dev` in `src/FieldService.Web`, in a terminal: it writes the API client types from `docs/contracts/openapi.v1.json`, then starts Vite | The terminal shows Vite's `ready in` line and `http://127.0.0.1:5173/` |

**Stopping.** The compound sets `stopAll`, so stopping any one of the three sessions, or the compound itself with Shift+F5, stops all three. The API first spends up to 5 seconds delivering the job events still queued, and the notification service closes its sockets before it exits. Afterwards nothing listens on 5080, 5090 or 5173; check with `lsof -nP -iTCP:5080 -sTCP:LISTEN`, and the same for 5090 and 5173, on macOS.

Two more configurations attach a browser debugger to the dashboard once the dev server runs: `Web in Chrome` and `Web in Edge`, both on http://localhost:5173.

**Without debuggers.** The task `dev: full stack` starts the three `dev:` tasks in parallel, with file watching and hot reload: `dotnet watch` for the API, and `npm run dev` for the notification service and the web dashboard. Each runs in its own terminal and counts as ready at the line in the table above. Stop them with "Tasks: Terminate Task" and "All Running Tasks", or with the trash icon of each terminal.

The technician app needs no launch configuration: pick the target in the .NET MAUI extension's device picker in the status bar, then use Run and Debug.

### Terminal: `start-dev.mjs`

| Command | Effect |
|---|---|
| `node infrastructure/scripts/start-dev.mjs` | Runs the checks, then starts `dotnet watch --project src/FieldService.Api` and `npm run dev` in `src/FieldService.Notifications` and `src/FieldService.Web`. Output lines are prefixed `[api]`, `[notifications]` and `[web]`. Ctrl+C stops all three; if one exits by itself, the script stops the others and exits 1 |
| `node infrastructure/scripts/start-dev.mjs --check` | Runs the checks only. It prints `start-dev: all checks passed for api, notifications, web` and exits 0, or lists each problem with its fix and exits 1 |
| `node infrastructure/scripts/start-dev.mjs --only web` | Checks and starts a subset, for example the web dev server alone while the other services run elsewhere. `--only` takes a comma-separated list of `api`, `notifications` and `web` |

The checks, each reported with its fix:

- the `node` major version matches `.nvmrc`, and `dotnet --version` is 10.0.402 or a later 10.0.4xx patch;
- `node_modules` exists in the web dashboard, the notification service and `tests/FieldService.Notifications.Tests`;
- the notification service's `.env` file and both user-secrets stores hold every required key; keys are checked by name, and no value is printed;
- the database named in `ConnectionStrings:FieldService` accepts TCP connections;
- ports 5080, 5090 and 5173 are free, except for a service that `--only` leaves out;
- `logs/api` and `logs/notifications` exist.

The script starts `npm` through the shell on Windows and stops each application's whole process tree, so `dotnet watch` leaves nothing holding port 5080.

### Terminal: one application at a time

```
dotnet watch --project src/FieldService.Api
```

```
cd src/FieldService.Notifications
npm run dev
```

```
cd src/FieldService.Web
npm run dev
```

### Checking the running stack

| Check | Expected |
|---|---|
| `curl -s http://localhost:5080/health/ready` | `Healthy`: the API is up and reaches its database |
| `curl -s http://127.0.0.1:5090/health/ready` | `Healthy`: the notification service accepts connections |
| http://localhost:5173 in a browser | The sign-in page |
| Jobs, signed in as the demo administrator | The live indicator in the top bar reads "Live" |
| A status change from the technician app while Jobs is open | Within a few seconds, without a reload, the dashboard shows a notice for the job and highlights its row |

**Without the notification service.** The API and the dashboard keep working. Each change is saved, and after at most its 10-second delivery budget the API logs `job-event.dispatch-failed` for the change, with the reason `unreachable`, or `circuit-open` while its circuit breaker is open. The dashboard shows that it is reconnecting, after 30 seconds that live updates are paused, and refreshes the jobs it shows when the connection returns.

### Logs of the running stack

| Application | Console | Files |
|---|---|---|
| API | The Debug Console of the `API` configuration, or the terminal of `dotnet watch`: text lines at Debug and above, including the executed SQL without parameter values | `logs/api/api-YYYYMMDD.log`, one JSON event per line |
| Notification service | Its terminal: readable lines through pino-pretty | `logs/notifications/notifications.YYYYMMDD.N.log`, one JSON object per line |
| Web dashboard | The dev server's terminal, and the browser's developer tools | None |

- `YYYYMMDD` is the local date; the time stamps inside the files are UTC.
- `start-dev.mjs` shows all three consoles in one terminal, with the prefixes `[api]`, `[notifications]` and `[web]`.
- [logging.md](logging.md) lists every event both services write. To follow one job change from the technician's request to the broadcast, search both folders for its correlation id ([Following one job change across the services](logging.md#following-one-job-change-across-the-services)):

```
grep -h "<correlation id>" logs/api/*.log logs/notifications/*.log
```

## Databases

### Local server

The default database is `fieldservice` on the local server at 127.0.0.1:5432, with the roles `fieldservice_migrator`, which owns the schema and applies migrations, and `fieldservice_app`, which the running API uses. `create-dev-env.mjs --bootstrap` and the task `db: bootstrap local server` run the bootstrap script; running it again re-applies both role passwords and leaves everything else as it is. API test runs create and drop their own `fieldservice_test_<run>` databases on the same server. [database.md](database.md) describes the roles, the script and the migration policy.

### Compose database (optional)

With Docker Desktop installed and after the first `create-dev-env.mjs` run, the Compose `db` service can replace the local server:

```
docker compose -f infrastructure/docker-compose.yml up -d --wait db
node infrastructure/scripts/create-dev-env.mjs --database compose
dotnet ef database update --project src/FieldService.Api
```

- The container runs `postgres:17.11-trixie` on 127.0.0.1:5433 (`DB_HOST_PORT`) with its data in the volume `fieldservice-pgdata`.
- On its first start with an empty volume, the container runs the bootstrap script with the role passwords from `infrastructure/.env`.
- `--database compose` points both API connection strings and the test admin connection at the container. To switch back, run `node infrastructure/scripts/create-dev-env.mjs --database local`.
- The task `db: start container` runs the first command.

### Resets

| Goal | Steps |
|---|---|
| Fresh local database, or the demo data back as seeded | The VS Code task `db: reset`, which runs `db: drop local database`, `db: bootstrap local server` and `db: migrate` in sequence. The terminal equivalent is below. Restart the API afterwards: its startup checks run against the new database, it seeds the demo data again, and the next created job is JOB-1056 again |
| Fresh Compose database | `docker compose -f infrastructure/docker-compose.yml down -v`, then `docker compose -f infrastructure/docker-compose.yml up -d --wait db`, then `db: migrate`. The next API start seeds the demo data |
| New secrets | `node infrastructure/scripts/create-dev-env.mjs --force`, then the steps it prints: bootstrap the local server again, reset the Compose database, and reset the local database. The reset matters for signing in: the seeder never rewrites existing password hashes, so the demo accounts take the new demo password only when the API seeds an empty database |
| Empty log folders | Delete the files inside `logs/api/` and `logs/notifications/`, and keep the folders |

The terminal equivalent of `db: reset` on macOS:

```
dropdb -h 127.0.0.1 --force fieldservice
node infrastructure/scripts/create-dev-env.mjs --bootstrap
dotnet tool restore
dotnet ef database update --project src/FieldService.Api
```

- `--force` ends open sessions, such as the running API's connection pool, which would otherwise block the drop.
- On Windows the drop connects as `postgres`: `dropdb -h 127.0.0.1 -U postgres --force fieldservice`. It uses `PGPASSWORD` or asks for the password.
- `dropdb` fails when the `fieldservice` database does not exist. In that case run the last three commands, or the tasks `db: bootstrap local server` and `db: migrate`.

`--force` and `--show-demo-password` are deliberate steps for a person at a terminal, and no task, script or CI step runs them:

- `--force` replaces every secret, then prints the follow-up steps. It prints no value.
- `--show-demo-password` prints the demo account password from `infrastructure/.env` and nothing else, for signing in by hand ([step 5](#5-check-that-it-runs)). Nothing automated needs it: the API and the tests read the user-secrets stores, the notification service reads its `.env` file, and Playwright reads `src/FieldService.Web/e2e/.env`.

## Tests

| Suite | Command | Needs |
|---|---|---|
| API | `dotnet test --project tests/FieldService.Api.Tests/FieldService.Api.Tests.csproj` | The local server, `psql` 15 or later on `PATH`, and the test user-secrets store, which `create-dev-env.mjs` fills. Each run creates and drops its own `fieldservice_test_<run>` database. The test host creates its own signing keys and webhook secret per run and seeds no demo data, so the API's user-secrets store plays no part |
| API, domain only | `dotnet test --project tests/FieldService.Api.Tests/FieldService.Api.Tests.csproj --filter-namespace FieldService.Api.Tests.Domain` | Nothing: no database |
| Technician app | `dotnet test --project tests/FieldService.Mobile.Tests/FieldService.Mobile.Tests.csproj` | No device: the tests build the app's plain `net10.0` target |
| Notification service | `npm test --prefix src/FieldService.Notifications` | Runs the suites in `tests/FieldService.Notifications.Tests` |
| Web dashboard | `npm test --prefix src/FieldService.Web` | Vitest |

**End-to-end tests against the host stack.** With "Full stack" running, use the VS Code task `test: e2e (host)`, or run the following; `npx playwright install chromium` is needed once:

```
cd src/FieldService.Web
npx playwright install chromium
npm run test:e2e:host
```

Playwright reads the demo password from `src/FieldService.Web/e2e/.env`, which `create-dev-env.mjs` wrote, so no password is typed, exported or prompted for. A value already set in `E2E_DEMO_PASSWORD` takes precedence over the file.

**Production headers before containers.** To run the same tests under the production Content Security Policy, stop the "Web dev server" configuration and serve the built app with `vite preview` on the same address, then run `npm run test:e2e:host` from a second terminal:

```
cd src/FieldService.Web
npm run build
npm run preview
```

**End-to-end tests against Compose.** `npm run test:e2e` in `src/FieldService.Web`, or the task `test: e2e (compose)`, runs the Playwright `compose` project against the nginx web image of the Compose `full` profile on http://127.0.0.1:8081.

## VS Code workspace

Only `.vscode/extensions.json`, `.vscode/launch.json` and `.vscode/tasks.json` are committed; every other file in `.vscode/` stays local and ignored.

### Recommended extensions

| Extension id | Purpose |
|---|---|
| `ms-dotnettools.csdevkit` | C# Dev Kit |
| `ms-dotnettools.csharp` | C# |
| `ms-dotnettools.dotnet-maui` | .NET MAUI |
| `Vue.volar` | Vue - Official |
| `dbaeumer.vscode-eslint` | ESLint |
| `esbenp.prettier-vscode` | Prettier |
| `vitest.explorer` | Vitest |
| `ms-playwright.playwright` | Playwright Test |
| `ms-azuretools.vscode-containers` | Container Tools, useful once Docker Desktop is installed |

### Tasks

`.vscode/tasks.json` defines 26 tasks. None of them prompts for input. Tasks that run `npm` use the shell, because on Windows `npm` is `npm.cmd` and cannot start as a bare process.

| Task | Runs | Notes |
|---|---|---|
| `env: create` | `node infrastructure/scripts/create-dev-env.mjs` | Creates missing local values; changes nothing when every output already holds its values |
| `env: check` | `node infrastructure/scripts/start-dev.mjs --check` | The pre-launch check of "Full stack" |
| `db: bootstrap local server` | `node infrastructure/scripts/create-dev-env.mjs --bootstrap` | Runs the bootstrap script on 127.0.0.1:5432 |
| `db: start container` | `docker compose -f infrastructure/docker-compose.yml up -d --wait db` | Needs Docker Desktop |
| `tools: restore` | `dotnet tool restore` | Installs the pinned dotnet-ef |
| `db: migrate` | `dotnet ef database update --project src/FieldService.Api` | Runs `tools: restore` first |
| `db: drop local database` | `dropdb -h 127.0.0.1 --force fieldservice`; on Windows with `-U postgres` | Ends open sessions first |
| `db: reset` | `db: drop local database`, `db: bootstrap local server` and `db: migrate`, in sequence | Local server only; see [Resets](#resets) |
| `db: migration script` | `dotnet ef migrations script --idempotent --project src/FieldService.Api --output artifacts/migrations.sql` | Writes the idempotent SQL script into the ignored `artifacts/` folder |
| `build: api` | `dotnet build src/FieldService.Api/FieldService.Api.csproj` | The default build task, and the pre-launch task of `API` |
| `dev: api` | `dotnet watch --project src/FieldService.Api` | Background task, ready at `Now listening on` |
| `dev: notifications` | `npm run dev` in `src/FieldService.Notifications` | Background task, ready at the `server.listening` log event |
| `dev: web` | `npm run dev` in `src/FieldService.Web` | Background task, ready at Vite's `ready in` line |
| `dev: full stack` | The three `dev:` tasks in parallel | File watching and hot reload, without debuggers |
| `test: api` | `dotnet test --project tests/FieldService.Api.Tests/FieldService.Api.Tests.csproj` | Needs the local server |
| `test: api (domain only)` | The same, with `--filter-namespace FieldService.Api.Tests.Domain` | No database |
| `test: mobile` | `dotnet test --project tests/FieldService.Mobile.Tests/FieldService.Mobile.Tests.csproj` | No device |
| `test: notifications` | `npm test` in `src/FieldService.Notifications` | |
| `test: web` | `npm test` in `src/FieldService.Web` | |
| `test: e2e (host)` | `npm run test:e2e:host` in `src/FieldService.Web` | Against "Full stack" or `vite preview` on 127.0.0.1:5173 |
| `test: e2e (compose)` | `npm run test:e2e` in `src/FieldService.Web` | Against the Compose `full` profile on 127.0.0.1:8081 |
| `check: comments` | `node infrastructure/scripts/check-no-comments.mjs` | See [Scripts](#scripts) |
| `check: tracked files` | `node infrastructure/scripts/check-tracked-files.mjs` | See [Scripts](#scripts) |
| `compose: backend up` | `docker compose -f infrastructure/docker-compose.yml --profile backend up -d --build --wait` | Starts the services of the `backend` profile and the database |
| `compose: full up` | `docker compose -f infrastructure/docker-compose.yml --profile full up -d --build --wait` | Starts the services of the `full` profile and the database |
| `compose: down` | `docker compose -f infrastructure/docker-compose.yml --profile full down` | Stops and removes the containers, and keeps the database volume |

Services without a profile always start, so while `infrastructure/docker-compose.yml` defines only the `db` service, the three `compose:` tasks start or stop the database container alone.

## Scripts

All four scripts are Node `.mjs` files in `infrastructure/scripts/`. They use only Node's built-in modules, so they run before any `npm ci`, and they work on macOS, Linux and Windows. Each exits 0 on success, 1 on failure and 2 on a usage error, such as an unknown flag, and then prints its usage.

### `create-dev-env.mjs`

| Invocation | Effect |
|---|---|
| `node infrastructure/scripts/create-dev-env.mjs` | Creates every missing value and writes all outputs for the local server |
| `--bootstrap` | Also runs `infrastructure/postgres/bootstrap.sql` on the local server with `psql -X -h 127.0.0.1 -p 5432 -U <admin> -d postgres -v ON_ERROR_STOP=1`, with both role passwords in the child process environment |
| `--database local` or `--database compose` | Points both API connection strings and the test admin connection at the local server (127.0.0.1:5432) or at the Compose `db` (127.0.0.1 on `DB_HOST_PORT`, admin `postgres`) |
| `--admin-user <name>` | The superuser for `--bootstrap` and the test admin connection. The default is your account on macOS and Linux, and `postgres` on Windows |
| `--only compose,notifications,api,e2e` | Writes only the outputs listed. `--only compose` serves a machine that runs everything in Docker |
| `--force` | Replaces every secret, then prints the follow-up steps |
| `--show-demo-password` | Prints the demo password and nothing else |
| `--ci` | For CI runners only: writes the Compose output, hands each secret to the runner's log masking, and appends the test and end-to-end values to `$GITHUB_ENV`. It fails when `GITHUB_ENV` is unset |

- It reads existing values first and creates only the missing ones; one value per secret feeds every output, for example the socket-ticket key of the API and of the notification service.
- It writes the user-secrets stores through `dotnet user-secrets`, addressed by id (`fieldservice-api` and `fieldservice-api-tests`), so the stores can be filled before a project exists. If a project declares a different `UserSecretsId`, it stops with a message.
- The values it creates contain no `$`, `#`, quotes, spaces or `;`, so they are safe in env files, in Compose interpolation and inside connection strings.

### `start-dev.mjs`

Described in [Running the stack](#terminal-start-devmjs).

### `check-no-comments.mjs`

Source and configuration files in this repository carry no comments ([ADR 0014](decisions/0014-comment-free-source-files.md)), and this script enforces it.

- With no arguments it scans the tracked files plus the untracked files that no ignore rule excludes. `--staged` scans the staged content. File paths as arguments limit the scan to those files.
- It prints one line per finding, `path:line:column kind`, where `kind` is `line-comment`, `block-comment`, `doc-comment`, `markup-comment`, `region-directive`, `hashbang` or `unclassified-file-type`.
- It scans each file type with a small lexer that skips string and regular-expression literals. There is no way to suppress a finding: restructure the code instead, for example `new RegExp('…')` in place of a regular-expression literal.

### `check-tracked-files.mjs`

It fails when a tracked path must never be committed, and when a tracked file holds something shaped like a secret.

- **Paths.** Log files and anything under `logs/` except `logs/.gitkeep`; real env files (every `.env` or `.env.*` except `.env.example`); build and dependency output; the client types produced from the API contract; key material; IDE user state, with `.vscode/` limited to the three shared files; shell scripts; packaged binaries; and `.txt` files other than licence texts.
- **Content.** Every tracked text file is searched for webhook secrets (`whsec_` values), three-part JSON Web Tokens and private-key headers. Only the published test values of the vector files in `docs/contracts/events/` pass.
- It prints `path kind` for a path and `path:line kind` for content, never the matched text.

## Dependencies

- **.NET.** Package versions live in `Directory.Packages.props`; project files reference packages without versions. Transitive pinning is on, so a `PackageVersion` entry that no project references directly sets the version of a dependency that another package brings: Polly.Core, Polly.Extensions and Polly.RateLimiting, which `Microsoft.Extensions.Http.Resilience` brings, are pinned to 8.8.0 this way. The API and both test projects commit a `packages.lock.json`. When `CI` is `true`, `Directory.Build.props` turns on locked restores, and NuGet audit warnings for high and critical advisories (`NU1903`, `NU1904`) are errors. `nuget.config` makes nuget.org the only package source.
- **npm.** Each package has its own `package-lock.json`; install with `npm ci`. A package added or updated must have been published at least 7 days before the install, so install with `--before` set to a time at least 7 days in the past, for example `npm install --before=<UTC time 7 days ago> <package>@<version>`, and commit the changed lock file.

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| "A compatible .NET SDK was not found" (requested 10.0.402) | No 10.0.4xx SDK at or above 10.0.402 is installed, for example after a cask upgrade removed SDK 10 | Install `dotnet-sdk-10.0.4xx-osx-arm64.pkg` from `https://builds.dotnet.microsoft.com/dotnet/Sdk/<version>/`. Never run `brew upgrade --cask dotnet-sdk` or a bare `brew upgrade` after .NET 11 is released |
| `npm error code EBADENGINE` | Node is older than 26.12.0 | Install Node 26.12.0 |
| The API stops at startup with "pending migrations" | The database is behind the model | Task `db: migrate` |
| The API stops at startup with `config.invalid` and `must be set` for `Auth:AccessTokenKey`, `Auth:SocketTicketKey`, `Notifications:WebhookSecret` or `Seed:DemoPassword` | The user-secrets store `fieldservice-api` lacks the key | Run `node infrastructure/scripts/create-dev-env.mjs` without flags. It fills the missing keys and keeps every existing value; `start-dev.mjs --check` then confirms the store |
| The API stops at startup with `config.invalid` and `must not be one of the published test keys`, `must not be a patterned key …`, `must be standard base64 …` or `must differ from …` | A key was typed by hand, or copied from documentation, a test or another key | Run `create-dev-env.mjs` without flags: it takes each value from `infrastructure/.env` first and writes it back into the store. If `infrastructure/.env` holds the same value, replace every secret with `--force` and follow the steps it prints |
| The API stops at startup with `config.invalid` for `Cors:AllowedOrigins` | An origin with a path or a trailing `/`, or `*` | Write each origin as `scheme://host:port`, for example `http://localhost:5173` |
| Sign-in shows "The email or password is incorrect." (401 `auth.invalid-credentials`) for every demo account, with the password that `--show-demo-password` prints | The database was seeded with an earlier demo password, for example before a `--force` run | Reset the local database with the task `db: reset` and restart the API, which seeds the demo data with the current password |
| `28P01 password authentication failed` (Windows or Compose) | The role passwords differ from the user-secrets store | Run `create-dev-env.mjs --bootstrap` again. For Compose, run `down -v`, then `up -d --wait db` |
| `3D000 database "fieldservice" does not exist` | The bootstrap has not run | `create-dev-env.mjs --bootstrap` |
| `42501 permission denied` during a migration | The migration ran with the app connection | `dotnet ef` must use `ConnectionStrings:Migrations`, the migrator role |
| The notification service exits at start with `config.invalid: <variable> <rule>` | Its `.env` file lacks a value, or holds a value copied from documentation or a test vector, or typed by hand | Run `node infrastructure/scripts/create-dev-env.mjs` without flags: it writes missing and drifted values back from `infrastructure/.env`. If the rule is `must not be a published test key` or `must not be a patterned key` and the run reports `unchanged`, replace every secret with `--force` and follow the steps it prints. The rules are in [configuration.md](configuration.md#notification-service-srcfieldservicenotifications) |
| The dashboard stays on "Reconnecting…" or shows that live updates are paused | The notification service is not running, or refuses the socket | Start it, or look in `logs/notifications/` for `socket.rejected` and its reason ([logging.md](logging.md#socket-events)) |
| `socket.rejected` with the reason `origin-not-allowed` | The dashboard was opened at an address that `SOCKET_ALLOWED_ORIGINS` does not list, such as a LAN host name | Open http://localhost:5173 or http://127.0.0.1:5173 |
| `socket.rejected` with the reason `signature` for every connection, or `job-event.rejected` with the reason `signature-mismatch` for every event | The API and the notification service hold different socket-ticket keys or webhook secrets, for example after one store was edited by hand | Run `create-dev-env.mjs` without flags, which writes one value into both, then restart both services |
| The API logs `job-event.dispatch-failed` with the reason `unreachable` or `circuit-open` | The notification service is not running on 5090 | Start it. The changes are saved, and open dashboards refresh their jobs when they reconnect |
| `EADDRINUSE` or "address already in use" on 5080, 5090 or 5173 | Another process holds the port | `lsof -nP -iTCP:5080 -sTCP:LISTEN` (macOS) or `Get-NetTCPConnection -LocalPort 5080` (Windows), then stop the duplicate |
| Windows: "An attempt was made to access a socket in a way forbidden by its access permissions" | A Hyper-V excluded port range | See the Windows note under [Ports](#ports) |
| Vite: "Port 5173 is already in use" | The dev server and `vite preview` share the port | Stop the other one, or free the port; moving it would break the allowed origin lists |
| "required variable POSTGRES_PASSWORD is missing a value" | `infrastructure/.env` does not exist | Run `create-dev-env.mjs` |
| Docker: "Pool overlaps with other one on this address space" | The Compose subnet is already in use | Set `NETWORK_SUBNET`, and a `WEB_IPV4_ADDRESS` inside it, in `infrastructure/.env` |
| The API answers "Bad Request - Invalid Hostname" (400) | The client used a host name that `AllowedHosts` does not list | Use `localhost`, `127.0.0.1` or `10.0.2.2`, or add the host for one session with `--AllowedHosts` on `dotnet run` |
| Emulator: "Cleartext HTTP traffic not permitted" | A Release build, or a host other than `10.0.2.2` | Use a Debug build with `http://10.0.2.2:5080` |
| Android build `XA0030` | A JDK newer than 21 | Point `JAVA_HOME` at JDK 21 |
| `NETSDK1147` (workload missing) | The .NET MAUI workload is not installed | `dotnet workload install maui-android --version 10.0.402`, plus `maui-windows` on Windows |
| Playwright fails at sign-in with no password | `src/FieldService.Web/e2e/.env` is missing and `E2E_DEMO_PASSWORD` is unset | Run `create-dev-env.mjs` without `--force`; it writes the missing file from the existing demo password |
| `check-tracked-files.mjs` reports `webhook-secret`, `jwt` or `private-key` | A tracked file holds a secret-shaped value that is not a published test value | Remove the value and build it at run time. If it was ever pushed, rotate the secret |
| `check-no-comments.mjs` reports a regular-expression literal | A limit of the lexer | Use `new RegExp('…')` |
