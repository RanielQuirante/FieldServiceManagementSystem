# Configuration

This document lists every setting and environment variable of FieldService: the API, the notification service, the web dashboard and its end-to-end tests, the technician app, the Compose file, the database bootstrap and the test runs. For each one it gives the purpose, the value per environment and where the value comes from. Secrets are named here, never shown: the `.env.example` files list names with empty values, and `infrastructure/scripts/create-dev-env.mjs` creates the local values ([development.md](development.md)).

## Environments

| Aspect | Development | Testing | Staging | CI | Production |
|---|---|---|---|---|---|
| Purpose | Inner loop on a developer machine | .NET integration tests on an in-process host | Production-like local stack and end-to-end target | Checks of pull requests and pushes | A real deployment |
| Where it runs | Host processes on macOS or Windows | The `dotnet test` process | Docker Compose, profiles `backend` or `full` | GitHub-hosted runners | A container platform |
| `ASPNETCORE_ENVIRONMENT` | `Development`, from `launchSettings.json` | `Testing`, set by the test host | `Staging` | `Testing` for the API tests; `Staging` for end-to-end runs | `Production` |
| `NODE_ENV` | `development` | `test` | `production` | `test` for the Node tests; `production` for end-to-end runs | `production` |
| Database | Local PostgreSQL 17 on 127.0.0.1:5432, database `fieldservice`; optionally the Compose `db` on 127.0.0.1:5433 | The same server, with one `fieldservice_test_<run>` database per run | Compose `db` | Compose `db` on the runner, 127.0.0.1:5433 | Managed PostgreSQL 17 with TLS |
| Migrations applied by | `dotnet ef database update`, as `fieldservice_migrator` | The test fixture, as `fieldservice_migrator` | The `migrate` service | The test fixture; the `migrate` service for end-to-end runs | A release step, as `fieldservice_migrator` |
| Secrets come from | API user-secrets store `fieldservice-api`; `src/FieldService.Notifications/.env`; `src/FieldService.Web/e2e/.env` for Playwright | Random values per run, in memory | `infrastructure/.env` | Throwaway values from `create-dev-env.mjs --ci` | The platform's secret store, as environment variables |
| OpenAPI document | http://localhost:5080/openapi/v1.json | In process | Not mapped | In process | Not mapped |

Staging runs the Production code paths: Secure cookies, JSON console logs and no OpenAPI document. It differs from Production in two ways only: it allows demo data, and it binds only to loopback.

## Sources and precedence

Later sources override earlier ones.

| Consumer | Sources, from lowest to highest precedence | Where development secrets live |
|---|---|---|
| API | `appsettings.json`, then `appsettings.{Environment}.json`, then user-secrets (Development only, id `fieldservice-api`), then environment variables, then the command line | User-secrets |
| Design-time database context and migrations bundle | User-secrets `fieldservice-api` (optional), then environment variables | User-secrets |
| API tests | The test host sets its configuration in memory. The test fixture reads environment variables first, then user-secrets `fieldservice-api-tests` | Test user-secrets |
| Notification service | Built-in defaults, then the package's `.env` file (loaded only through Node's `--env-file-if-exists` flag, by `npm run dev` and the VS Code launch configuration), then real environment variables, which always win. An empty value counts as unset. The image sets `NODE_ENV=production` | `src/FieldService.Notifications/.env` |
| Web dashboard | `vite.config.ts` reads two proxy targets from the environment. Nothing reaches the browser bundle: there are no `VITE_` variables, and Vite reads no env file | None |
| End-to-end tests | `src/FieldService.Web/e2e/.env`, which `playwright.config.ts` loads only when the file exists and `E2E_DEMO_PASSWORD` is unset; then real environment variables, which always win | `src/FieldService.Web/e2e/.env` |
| Technician app | MSBuild properties at build time, embedded as assembly metadata | None |
| Compose | `infrastructure/.env`, loaded from the Compose project folder `infrastructure/`; shell environment variables override it. Fixed values live in `docker-compose.yml` | `infrastructure/.env` |
| `bootstrap.sql` | psql `\getenv` from the process environment | The environment that `create-dev-env.mjs`, the Compose `db` service or the test fixture sets |

## Naming rules

- **API keys** are hierarchical, `Section:Key`. As environment variables they use a double underscore, `Auth__AccessTokenKey`, and array items take an index, `Cors__AllowedOrigins__0`. Serilog override keys contain dots, for example `Serilog__MinimumLevel__Override__Microsoft.AspNetCore`; process and Compose environments accept these names, but a shell `export` does not.
- **Compose variables** are UPPER_SNAKE_CASE. `docker-compose.yml` maps each one explicitly to an API key or a Node variable, and uses no `env_file`, so a service receives only the values it names.
- **Node variables** are UPPER_SNAKE_CASE. Lists are space-separated.
- **Test switches** start with `FIELDSERVICE_`, and Playwright inputs start with `E2E_`.

## Values fixed in code

These values are deliberately not configurable; changing one is a code change with tests, so no setting exists for it:

- the access-token lifetime of 15 minutes;
- session lifetimes: a Web session ends after 60 minutes without a refresh or 12 hours after sign-in, and a Mobile session after 7 days without a refresh or 30 days after sign-in;
- the 30-second refresh grace window, and the 30-second clock skew allowed when the API validates an access token;
- the issuer and the access-token audience, both `fieldservice-api`, with HS256 as the only accepted algorithm;
- the refresh cookie `fs_refresh`, limited to the path `/api/auth`;
- password hashing with PBKDF2 at 210,000 iterations;
- the rate limits, in fixed 60-second windows: 10 sign-ins and 30 refreshes per minute per client address, and 30 socket tickets and 10 client error reports per minute per signed-in user; on top of these, every `/api` request counts against 300 per minute per signed-in user, or 60 per minute per client address for a request without a valid token. Health checks and the OpenAPI document are not limited;
- the request body limits of 16 KB for sign-in, refresh and sign-out, and 8 KB for client error reports;
- the job event queue: 1,000 events, the oldest dropped first when it is full;
- the delivery budget of each job event: attempts of 2 seconds, at most 3 retries with jitter after a 1-second base delay, and 10 seconds in total; a circuit breaker that stops deliveries for 15 seconds when at least half of 4 or more attempts within 30 seconds fail; and, at shutdown, 5 seconds to deliver the events still queued;
- the 16 KB webhook body limit;
- the socket ticket: HS256, valid for 60 seconds, with the issuer `fieldservice-api` and the audience `fieldservice-notifications`, and usable once;
- the notification service's event-id retention for duplicate detection: 2 × `WEBHOOK_TOLERANCE_SECONDS` + 60 seconds (660 seconds at the default tolerance), in a map of at most 10,000 ids that evicts the oldest first, so it always outlasts the window in which an event's timestamp is accepted;
- the notification service's record of used ticket ids: each id is kept for 120 seconds, in a set of at most 10,000 ids; a full set forgets only an id whose ticket can no longer pass verification, and otherwise refuses the new ticket;
- the notification service's Socket.IO settings: the path `/socket.io`, the WebSocket transport only, messages of at most 4 KB and 10 seconds to complete a connection;
- the technician app's HTTP timeout of 15 seconds;
- OpenAPI exposure (Development and Testing only);
- the refusal to seed demo data in Production;
- the notification service's log rotation (daily, 50 MB, 14 files) and the technician app's (daily, 5 MB, 7 files).

## API (`src/FieldService.Api`)

### Settings

The default is the value in `appsettings.json`, or the framework's own when that file has none. "None" means that no committed file holds a value, so it must come from a secret store, the test host or the environment. [Values per environment](#values-per-environment) gives an example of every key, with secrets as placeholders, and the startup rule of each key is in [Startup checks](#startup-checks).

| Key (environment variable) | Type | Default | Purpose | Secret | Set in |
|---|---|---|---|---|---|
| `ConnectionStrings:FieldService` (`ConnectionStrings__FieldService`) | String: a PostgreSQL connection string | None; required in every environment | Connection of the running API, as `fieldservice_app`. See [Connection strings](#connection-strings) | Yes | User-secrets; test host; Compose; secret store |
| `ConnectionStrings:Migrations` (`ConnectionStrings__Migrations`) | String: a PostgreSQL connection string | None | Connection as `fieldservice_migrator`, read only by the design-time database context and the migrations bundle, never by the running API | Yes | User-secrets; Compose `migrate` service; release step |
| `Auth:AccessTokenKey` (`Auth__AccessTokenKey`) | String: standard base64 of 32 bytes or more | None; required in every environment | HS256 signing key of access tokens | Yes | User-secrets; test host; Compose; secret store |
| `Auth:SocketTicketKey` (`Auth__SocketTicketKey`) | String: standard base64 of 32 bytes or more | None; required in every environment, also when no notification service runs | HS256 key that signs the 60-second socket tickets the API issues to administrators. The notification service verifies them with `SOCKET_TICKET_KEY`, which must hold the same value: both services use the decoded bytes. It must differ from `Auth:AccessTokenKey` | Yes | As above |
| `Notifications:BaseUrl` (`Notifications__BaseUrl`) | String: an absolute `http` or `https` URL, or empty | Empty | Base URL of the notification service. After each committed create, assignment or status change, the API posts a signed job event to `<base URL>/internal/job-events`; a trailing `/` is ignored. When it is empty, the API queues and sends no job events and runs on its own | No | `appsettings.Development.json`; Compose; platform |
| `Notifications:WebhookSecret` (`Notifications__WebhookSecret`) | String: `whsec_` followed by standard base64 of 32 bytes or more | None; required when `Notifications:BaseUrl` is set | Secret that signs each job event in the Standard Webhooks format. The notification service must list the same value in `WEBHOOK_SECRETS`. It must differ from both `Auth` keys | Yes | User-secrets; test host; Compose; secret store |
| `Cors:AllowedOrigins` (`Cors__AllowedOrigins__0` and up) | Array of origins, each `scheme://host` or `scheme://host:port` | Empty | Browser origins allowed by the CORS policy. Traffic through the Vite proxy or nginx is same-origin and does not need them | No | `appsettings.Development.json`; Compose; platform |
| `ForwardedHeaders:KnownProxies` (`ForwardedHeaders__KnownProxies__0` and up) | Array of IP addresses | Empty: the built-in loopback defaults apply in Development and Testing; required in every other environment | Proxy addresses trusted for `X-Forwarded-For` and `X-Forwarded-Proto`, with a forward limit of 1; `X-Forwarded-Host` is never used. When either forwarded-header list has an entry, the two lists replace the built-in loopback defaults | No | Compose; platform |
| `ForwardedHeaders:KnownNetworks` (`ForwardedHeaders__KnownNetworks__0` and up) | Array of networks in CIDR notation | Empty | Proxy networks. Empty in every environment: no network range is trusted, and outside Development and Testing the API refuses an entry | No | `appsettings.json` |
| `Seed:DemoData` (`Seed__DemoData`) | Boolean | `false` | Lets the API seed the demo data at startup. Refused in Production | No | `appsettings.Development.json`; Compose |
| `Seed:DemoPassword` (`Seed__DemoPassword`) | String of 12 characters or more | None; required when `Seed:DemoData` is `true` | Password of every demo account | Yes, local only | User-secrets; Compose |
| `Logs:Directory` (`Logs__Directory`) | String: a folder path | `logs` | Log folder, resolved against the content root | No | `appsettings*.json`; Compose |
| `Logs:FileEnabled` (`Logs__FileEnabled`) | Boolean | `true` | Turns the file sink on or off | No | `appsettings*.json` |
| `Logs:RetainedFileCountLimit` (`Logs__RetainedFileCountLimit`) | Integer, 1 or more | `14` | Number of log files kept, the current one included | No | `appsettings.json` |
| `Logs:FileSizeLimitBytes` (`Logs__FileSizeLimitBytes`) | Integer in bytes, 1 or more | `52428800` | Size at which a log file rolls over to a numbered file | No | `appsettings.json` |
| `Logs:ConsoleFormat` (`Logs__ConsoleFormat`) | `Text` or `Json` | `Json` | `Text`, or `Json` for compact JSON events | No | `appsettings*.json`; Compose |
| `Serilog:MinimumLevel:Default` (`Serilog__MinimumLevel__Default`) | Serilog level | `Information` | Minimum log level | No | `appsettings*.json` |
| `Serilog:MinimumLevel:Override:<namespace>` | Serilog level per namespace | `Warning` for four namespaces | Minimum level per namespace. `Microsoft.AspNetCore.Hosting.Diagnostics` stays at `Warning` everywhere, because its request lines contain query strings | No | `appsettings*.json` |
| `Kestrel:Limits:MaxRequestBodySize` | Integer in bytes | `1048576` | Global request body limit. Individual endpoints lower it | No | `appsettings.json` |
| `AllowedHosts` | String: host names separated by `;` | `*` | Host filtering: a request whose `Host` header is not listed gets 400. On loopback this blocks DNS rebinding, which a loopback binding alone does not stop. Production refuses `*`, so a deployment must list its host names | No | `appsettings.json`; `appsettings.Development.json`; Compose; platform |
| `ASPNETCORE_ENVIRONMENT` (environment only) | String | `Production` when unset | Selects the environment | No | `launchSettings.json`; test host; Compose; platform |
| `ASPNETCORE_HTTP_PORTS` (environment only) | Port list | Unset on a host; `8080` in the base image | Listen port inside a container | No | The base image |

### Values per environment

| Key | Development | Testing | Staging (Compose) | Production |
|---|---|---|---|---|
| `ConnectionStrings:FieldService` | Local server, app role | The run's test database, app role | `db` service, app role | Managed server, app role, TLS |
| `ConnectionStrings:Migrations` | Local server, migrator role | Not set: the fixture migrates by itself | `migrate` service only | Release step only |
| `Auth:AccessTokenKey`, `Auth:SocketTicketKey` | Random values from `create-dev-env.mjs` | Random per run | `${AUTH_ACCESS_TOKEN_KEY}`, `${AUTH_SOCKET_TICKET_KEY}` | Secret store |
| `Notifications:BaseUrl` | `http://127.0.0.1:5090` | Empty | `http://notifications:5090` | Private `https` URL of the service |
| `Notifications:WebhookSecret` | Random value from `create-dev-env.mjs` | Random per run | `${NOTIFICATIONS_WEBHOOK_SECRET}` | Secret store |
| `Cors:AllowedOrigins` | `http://localhost:5173`, `http://127.0.0.1:5173` | Set by the tests that need it | `http://localhost:8081`, `http://127.0.0.1:8081`, `http://localhost:5173`, `http://127.0.0.1:5173` | The public web origin, `https` only |
| `ForwardedHeaders:KnownProxies` | Empty: the loopback defaults cover the Vite proxy and `vite preview` | Empty | `${WEB_IPV4_ADDRESS:-172.30.80.10}`, nginx's fixed address | nginx's address, one entry per instance; never the TLS proxy. Required, because the API refuses an empty list outside Development and Testing |
| `ForwardedHeaders:KnownNetworks` | Empty | Empty | Empty | Empty |
| `Seed:DemoData` | `true` | `false` | `true` | `false` |
| `Seed:DemoPassword` | Random value from `create-dev-env.mjs` | Not set | `${SEED_DEMO_PASSWORD}` | Not set |
| `Logs:Directory` | `../../logs/api` | A temporary folder, in the file-logging test only | `/app/logs` | `/app/logs`, on a volume |
| `Logs:FileEnabled` | `true` | `false` | `true` | `true` |
| `Logs:RetainedFileCountLimit` | `14` | Not used | `14` | `14` |
| `Logs:FileSizeLimitBytes` | `52428800` | Not used | `52428800` | `52428800` |
| `Logs:ConsoleFormat` | `Text` | `Text` | `Json` | `Json` |
| `Serilog:MinimumLevel:Default` | `Debug` | `Warning` | `Information` | `Information` |
| `Serilog:MinimumLevel:Override` | `Microsoft`, `Microsoft.AspNetCore`, `Microsoft.EntityFrameworkCore` and `System.Net.Http` at `Information`; `Microsoft.AspNetCore.Hosting.Diagnostics` at `Warning` | The base file's overrides | `Microsoft.AspNetCore`, `Microsoft.AspNetCore.Hosting.Diagnostics`, `Microsoft.EntityFrameworkCore` and `System.Net.Http` at `Warning` | As Staging |
| `Kestrel:Limits:MaxRequestBodySize` | `1048576` | `1048576` | `1048576` | `1048576` |
| `AllowedHosts` | `localhost;127.0.0.1;10.0.2.2` | `*` (the test server) | `localhost;127.0.0.1;10.0.2.2` | The public host name; required, because the API refuses the base file's `*` in Production |
| `ASPNETCORE_ENVIRONMENT` | `Development` | `Testing` | `Staging` | `Production` |
| `ASPNETCORE_HTTP_PORTS` | Not set: `launchSettings.json` binds http://localhost:5080 | Not set | `8080` | `8080` |

### Connection strings

| Connection | Development | Staging (Compose) | Production |
|---|---|---|---|
| App, `ConnectionStrings:FieldService` | `Host=127.0.0.1;Port=5432;Database=fieldservice;Username=fieldservice_app;Password=…;Maximum Pool Size=20;Application Name=fieldservice-api` | `Host=db;Port=5432;Database=fieldservice;Username=fieldservice_app;Password=…;Maximum Pool Size=20;Application Name=fieldservice-api;GSS Encryption Mode=Disable` | The managed host, with `Maximum Pool Size=20;Application Name=fieldservice-api` and `SSL Mode=VerifyFull;Root Certificate=<CA path>` |
| Migrator, `ConnectionStrings:Migrations` | `Host=127.0.0.1;Port=5432;Database=fieldservice;Username=fieldservice_migrator;Password=…;Application Name=fieldservice-migrator` | `Host=db;Port=5432;Database=fieldservice;Username=fieldservice_migrator;Password=…;Application Name=fieldservice-migrator;GSS Encryption Mode=Disable` | The managed host, with `Application Name=fieldservice-migrator` and TLS |

- `create-dev-env.mjs --database compose` points both Development strings at the Compose `db` on 127.0.0.1:5433; `--database local` points them back at 127.0.0.1:5432.
- `Maximum Pool Size=20` keeps the API's pool below the app role's connection limit of 30; Npgsql's default of 100 would exhaust it under load.
- `Application Name` names the sessions in `pg_stat_activity` and in the server log.
- `Include Error Detail` must stay off, so that database errors never carry row values into logs.

### Settings files

The API has exactly three settings files:

| File | Holds |
|---|---|
| `appsettings.json` | The production-safe defaults, which Staging and Production use: `AllowedHosts` `*`, the request body limit, the Serilog levels, the `Logs` section with JSON console output, empty CORS and forwarded-header lists, an empty `Notifications:BaseUrl` and `Seed:DemoData` `false` |
| `appsettings.Development.json` | The Development host list, the Debug level with its overrides, `../../logs/api` with text console output, the two `:5173` origins, the local notification service URL and `Seed:DemoData` `true` |
| `appsettings.Testing.json` | The `Warning` level, the file sink off and text console output |

- **No Staging or Production file.** Staging values come from the Compose environment ([Staging in Compose](#staging-in-compose)) and Production values from the platform, so each value has one source. The same image runs in both environments, and a committed Production file could not hold the real values, such as the public host name.
- **Only the base file in the image.** The API image's build context leaves out `appsettings.Development.json` and `appsettings.Testing.json`, so a container has `appsettings.json` alone, whatever its environment.
- **No secrets.** No settings file holds a connection string, a key, the webhook secret or the demo password. They come from the user-secrets store in Development, from the test host in Testing, from `infrastructure/.env` through Compose in Staging, and from the secret store in Production.
- **Repeated namespaces.** The Development file repeats the base file's namespaces, because configuration merges key by key: a broader `Microsoft` override alone would leave the base file's `Warning` overrides in force.
- **SQL in Development logs.** `Microsoft.EntityFrameworkCore` at `Information` shows the executed SQL commands. Parameter values stay hidden, because sensitive-data logging is off everywhere.
- **No query strings.** `Microsoft.AspNetCore.Hosting.Diagnostics` stays at `Warning` in every file: its "Request starting" and "Request finished" lines contain the query string, which may hold search terms. The API's own request log already records each request without its query string. Because the base file sets the same override, raising `Microsoft.AspNetCore` through an environment variable does not bring these lines back.
- **Allowed hosts.** `localhost` serves browsers, the Windows app and a USB device through `adb reverse`; `127.0.0.1` serves the Vite proxy, which rewrites `Host` to its target, plus scripts and Playwright; `10.0.2.2` serves the Android emulator. A session that serves another host name adds it on the command line, for example `dotnet run --project src/FieldService.Api -- --AllowedHosts "localhost;127.0.0.1;10.0.2.2;<extra host>"`. The base file's `*` suits the test server only: in Production the API refuses it, so the platform sets the public host names.

`Properties/launchSettings.json` has one profile, `http`: it binds http://localhost:5080 and sets `ASPNETCORE_ENVIRONMENT` to `Development`. `dotnet run`, `dotnet watch` and the VS Code `API` configuration all use it.

### OpenAPI document

The API serves its OpenAPI 3.1 document at `/openapi/v1.json` in Development and Testing only. No setting changes this. The API ships no API explorer page, and `/swagger` answers 404 in every environment ([ADR 0017](decisions/0017-api-explorer-in-development.md)).

### Design-time database context and migrations bundle

- The design-time database context builds its configuration from user-secrets `fieldservice-api` (optional) and environment variables, in that order, and reads only `ConnectionStrings:Migrations`.
- `dotnet ef migrations has-pending-model-changes` never opens a connection, so a password-free value is enough for it, for example `Host=127.0.0.1;Port=5433;Database=fieldservice;Username=fieldservice_migrator`.
- The migrations bundle uses the same factory, so the Compose `migrate` service sets `ConnectionStrings__Migrations`.

### Startup checks

The API validates its configuration after it builds the host and before it touches the database or listens. It runs every check before it stops, so one start lists each invalid key. Each failed rule logs the event `config.invalid` at the fatal level, as `Configuration key <key> breaks the rule: <rule>`, naming the key and the rule and never the value; the API then logs `The API stopped unexpectedly` and exits with a non-zero code.

| Key | Rule | Environments |
|---|---|---|
| `ConnectionStrings:FieldService` | Set, a valid PostgreSQL connection string, and `Include Error Detail` not enabled | All |
| `Auth:AccessTokenKey` | Set; standard base64 that decodes to 32 bytes or more; not a published test key; not a patterned key | All |
| `Auth:SocketTicketKey` | The rules of `Auth:AccessTokenKey`, and its decoded bytes differ from that key's | All |
| `Notifications:BaseUrl` | When set: an absolute `http` or `https` URL | All |
| `Notifications:BaseUrl` | When set: `https` | Production |
| `Notifications:WebhookSecret` | Set whenever `Notifications:BaseUrl` is set. Whenever it is present, even with an empty `Notifications:BaseUrl`: `whsec_` followed by standard base64 that decodes to 32 bytes or more; not a published test key; not a patterned key; its decoded bytes differ from both `Auth` keys | All |
| `Seed:DemoData` | Not `true` | Production |
| `Seed:DemoPassword` | 12 characters or more when `Seed:DemoData` is `true` | All |
| `Cors:AllowedOrigins` | Each entry is an absolute `http` or `https` origin, `scheme://host` or `scheme://host:port`, with no path, not even a trailing `/`; `*` is refused | All |
| `Cors:AllowedOrigins` | Each entry uses `https` | Production |
| `ForwardedHeaders:KnownProxies` | Each entry is the IP address of one host. An IPv4 entry is written as four decimal numbers without leading zeros, so shorthand, octal and hexadecimal forms are refused. Unspecified addresses (`0.0.0.0`, `::`), the broadcast address `255.255.255.255` and multicast addresses are refused, also in their IPv4-mapped IPv6 form, such as `::ffff:0.0.0.0` | All |
| `ForwardedHeaders:KnownProxies` | At least one entry, because client addresses are taken only from a listed proxy | All except Development and Testing |
| `ForwardedHeaders:KnownNetworks` | Each entry parses as a network in CIDR notation | All |
| `ForwardedHeaders:KnownNetworks` | Empty | All except Development and Testing |
| `AllowedHosts` | At least one host name, and no `*` entry | Production |
| `Logs` | `ConsoleFormat` is `Text` or `Json`. When `FileEnabled` is `true`: `Directory` is set, and `RetainedFileCountLimit` and `FileSizeLimitBytes` are 1 or more. An invalid `Logs` setting is reported as one fatal line naming the key and the rule, because no configured logger exists yet | All |

- An empty or whitespace-only value counts as missing, so an empty value copied from an `.env.example` file fails as `must be set`.
- A **published test key** is one of three 32-byte runs: the bytes 0x00 to 0x1F, 0x20 to 0x3F and 0x40 to 0x5F. The test vector files under `docs/contracts/events/`, `signature-test-vector.json` and `socket-ticket-test-vector.json`, publish two of them, and anyone can compute the third. The rule compares decoded bytes, so it catches a key in plain base64 and a secret in its `whsec_` form alike.
- A **patterned key** is one whose decoded bytes are all equal, or each differ from the previous byte by the same step, modulo 256. A hand-typed value such as a run of `A` characters, whose bytes are all zero, fails this rule.
- The keys must be standard base64, with `+` and `/`. Base64url, with `-` and `_`, is refused.
- The notification service applies the same published-key and patterned-key rules to `WEBHOOK_SECRETS` and `SOCKET_TICKET_KEY` ([Notification service](#notification-service-srcfieldservicenotifications)). Neither service can see the other's values, so a mismatch between them passes both startups and shows only at run time ([Values shared with the API](#values-shared-with-the-api)).
- After these checks the API looks for pending migrations, in Development and Staging only. When one is pending, it logs `config.invalid` for `ConnectionStrings:FieldService` with the names of the pending migrations and the command `dotnet ef database update --project src/FieldService.Api`, and stops. Only then does it seed the demo data, when `Seed:DemoData` allows it ([database.md](database.md#demo-data)).

## Compose variables (`infrastructure/.env`)

`create-dev-env.mjs` writes every key, so interpolation never fails, whichever profile is active. The Compose file marks each secret as required with `${VAR:?}`, which stops Compose with "required variable … is missing a value" when `infrastructure/.env` is missing, and reads optional values with `${VAR:-default}`, which treats an empty value as unset.

| Variable | Used by | Purpose | Default | Example | Secret | Format |
|---|---|---|---|---|---|---|
| `DB_HOST_PORT` | `db` port mapping | Host port of the container's PostgreSQL | `5433` | `5434` | No | 1024 to 65535 |
| `NETWORK_SUBNET` | Network `fieldservice-net` | Fixed subnet that holds nginx's fixed address | `172.30.80.0/24` | `172.31.90.0/24` | No | IPv4 CIDR. Change it only if it overlaps an existing Docker network, and then set a matching `NETWORK_IP_RANGE` and `WEB_IPV4_ADDRESS` |
| `NETWORK_IP_RANGE` | Network `fieldservice-net` | The part of the subnet from which Docker assigns the gateway's address and every container address other than nginx's, so no other container can take nginx's fixed address | `172.30.80.128/25` | `172.31.90.128/25` | No | IPv4 CIDR inside `NETWORK_SUBNET` that leaves out `WEB_IPV4_ADDRESS`, such as the subnet's upper half. A range outside the subnet makes `docker compose up` fail; Docker does not check that it leaves the address out |
| `WEB_IPV4_ADDRESS` | `web` address; the API's only trusted proxy | nginx's fixed address | `172.30.80.10` | `172.31.90.10` | No | An IPv4 address inside `NETWORK_SUBNET` and outside `NETWORK_IP_RANGE`, with the host part `.10`. An address outside the subnet makes `docker compose up` fail |
| `POSTGRES_PASSWORD` | `db` | Superuser password, used by the database initialisation and by the test admin connection on a CI runner | Required | `<32 random base64url characters>` | Yes | 32 characters, base64url |
| `FIELDSERVICE_MIGRATOR_PASSWORD` | `db` bootstrap; `migrate` | Password of `fieldservice_migrator` | Required | `<32 random base64url characters>` | Yes | 32 characters, base64url |
| `FIELDSERVICE_APP_PASSWORD` | `db` bootstrap; `api` | Password of `fieldservice_app` | Required | `<32 random base64url characters>` | Yes | 32 characters, base64url |
| `AUTH_ACCESS_TOKEN_KEY` | `api` `Auth__AccessTokenKey` | Access-token key | Required | `<base64 of 32 random bytes>` | Yes | Standard base64 of 32 random bytes, distinct from `AUTH_SOCKET_TICKET_KEY` |
| `AUTH_SOCKET_TICKET_KEY` | `api` `Auth__SocketTicketKey`; `notifications` `SOCKET_TICKET_KEY` | Socket-ticket key, shared by both services | Required | `<base64 of 32 random bytes>` | Yes | Standard base64 of 32 random bytes, distinct from `AUTH_ACCESS_TOKEN_KEY` |
| `NOTIFICATIONS_WEBHOOK_SECRET` | `api` `Notifications__WebhookSecret`; `notifications` `WEBHOOK_SECRETS` | Event signing secret | Required | `whsec_<base64 of 32 random bytes>` | Yes | `whsec_` followed by standard base64 of 32 random bytes |
| `SEED_DEMO_PASSWORD` | `api` `Seed__DemoPassword` | Demo account password | Required | `<xxxx-xxxx-xxxx-xxxx>` | Yes, local only | `xxxx-xxxx-xxxx-xxxx`, 19 characters |

- An `infrastructure/.env` written before `NETWORK_IP_RANGE` existed has no such key, and Compose uses the default. The next run of `create-dev-env.mjs` adds the key with an empty value and keeps every other value.
- The `db` service fixes its superuser and maintenance database at `postgres`. The bootstrap script creates `fieldservice`, owned by `fieldservice_migrator`.
- The bootstrap script runs once, on an empty volume, with the two role passwords from the container's environment. After `create-dev-env.mjs --force`, reset the volume with `down -v`.
- A variable set in the shell overrides `infrastructure/.env` for that one command, for example `DB_HOST_PORT=5434 docker compose -f infrastructure/docker-compose.yml up -d --wait db`.

## Staging in Compose

`infrastructure/docker-compose.yml` holds the whole Staging configuration of the containers. There is no `appsettings.Staging.json`, and no service uses an `env_file`, so each container receives only the variables its `environment` block names. A value written `${VARIABLE}` comes from [`infrastructure/.env`](#compose-variables-infrastructureenv); the Compose file refuses to start when a required one is empty. How the services start, their hardening and their network are described in [deployment.md](deployment.md#running-the-staging-stack-with-compose).

**`api`**

| Variable | Value | Notes |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Staging` | The Production code paths, with demo data allowed |
| `AllowedHosts` | `localhost;127.0.0.1;10.0.2.2` | The host names of browsers, nginx's `Host` header, scripts and the Android emulator |
| `ConnectionStrings__FieldService` | `Host=db;Port=5432;Database=fieldservice;Username=fieldservice_app;Password=${FIELDSERVICE_APP_PASSWORD};Maximum Pool Size=20;Application Name=fieldservice-api;GSS Encryption Mode=Disable` | The app role on the `db` service. `GSS Encryption Mode=Disable` turns off Npgsql's attempt to negotiate GSS (Kerberos) encryption, which the containers do not use |
| `Auth__AccessTokenKey` | `${AUTH_ACCESS_TOKEN_KEY}` | |
| `Auth__SocketTicketKey` | `${AUTH_SOCKET_TICKET_KEY}` | The same value as the notification service's `SOCKET_TICKET_KEY` |
| `Notifications__BaseUrl` | `http://notifications:5090` | The notification service on the Compose network |
| `Notifications__WebhookSecret` | `${NOTIFICATIONS_WEBHOOK_SECRET}` | The same value as the notification service's `WEBHOOK_SECRETS` |
| `Cors__AllowedOrigins__0` | `http://localhost:8081` | nginx's origins |
| `Cors__AllowedOrigins__1` | `http://127.0.0.1:8081` | |
| `Cors__AllowedOrigins__2` | `http://localhost:5173` | The dev server's origins, for the `backend` profile |
| `Cors__AllowedOrigins__3` | `http://127.0.0.1:5173` | |
| `ForwardedHeaders__KnownProxies__0` | `${WEB_IPV4_ADDRESS:-172.30.80.10}` | nginx's fixed address, from the same variable as `web`'s address, and the API's only trusted proxy. No `ForwardedHeaders__KnownNetworks__0` is set, and the API would refuse one in Staging |
| `Seed__DemoData` | `true` | |
| `Seed__DemoPassword` | `${SEED_DEMO_PASSWORD}` | |
| `Logs__Directory` | `/app/logs` | Bind-mounted from the host's `logs/api` |
| `Logs__ConsoleFormat` | `Json` | Compact JSON events on the console, for Docker's log driver |

Every other setting comes from `appsettings.json`: the `Information` level with the `Warning` overrides, the file sink with 14 files of 50 MB, and the request body limit. The base image sets `ASPNETCORE_HTTP_PORTS=8080`.

**`migrate`**

| Variable | Value | Notes |
|---|---|---|
| `ConnectionStrings__Migrations` | `Host=db;Port=5432;Database=fieldservice;Username=fieldservice_migrator;Password=${FIELDSERVICE_MIGRATOR_PASSWORD};Application Name=fieldservice-migrator;GSS Encryption Mode=Disable` | The migrator role, read by the migrations bundle through the design-time database context |
| `DOTNET_BUNDLE_EXTRACT_BASE_DIR` | `/tmp` | Where a self-contained bundle may unpack files, because the root filesystem is read-only |

**`notifications`**

| Variable | Value |
|---|---|
| `NODE_ENV` | `production` |
| `HOST` | `0.0.0.0` |
| `PORT` | `5090` |
| `LOG_LEVEL` | `info` |
| `LOG_DIR` | `/app/logs`, bind-mounted from the host's `logs/notifications` |
| `LOG_PRETTY` | `false` |
| `WEBHOOK_SECRETS` | `${NOTIFICATIONS_WEBHOOK_SECRET}` |
| `SOCKET_TICKET_KEY` | `${AUTH_SOCKET_TICKET_KEY}` |
| `SOCKET_ALLOWED_ORIGINS` | `http://localhost:8081 http://127.0.0.1:8081 http://localhost:5173 http://127.0.0.1:5173` |

`WEBHOOK_TOLERANCE_SECONDS`, `SOCKET_TICKET_ISSUER`, `SOCKET_TICKET_AUDIENCE` and `SOCKET_SESSION_MAX_SECONDS` are not set, so their defaults apply.

**`db`.** `POSTGRES_USER` and `POSTGRES_DB` are `postgres`, `POSTGRES_PASSWORD` is `${POSTGRES_PASSWORD}` and `POSTGRES_INITDB_ARGS` is `--data-checksums`. `FIELDSERVICE_MIGRATOR_PASSWORD` and `FIELDSERVICE_APP_PASSWORD` reach the bootstrap script on the first start of an empty volume. The service starts PostgreSQL with two server settings, `log_min_error_statement=panic` and `log_error_verbosity=terse`, so the server log never quotes a failed statement or its row values ([logging.md](logging.md#containers)).

**`web`.** No variables. The baked `nginx.conf` holds the upstream names, the response headers and the cache policy ([deployment.md](deployment.md#the-web-server)).

**Startup checks in a container.** The API runs the [startup checks](#startup-checks) of Staging and, when one fails, logs a `config.invalid` event for each failed key, then exits with code 134; Compose restarts it until the value is fixed. The notification service exits with code 1 and its `config.invalid:` line, and its health check never passes. A value can be tried for one start without editing `infrastructure/.env`, for example `docker compose -f infrastructure/docker-compose.yml --profile full run --rm --no-deps -e Logs__ConsoleFormat=Text api`; `run` publishes no port, so it does not collide with the running `api`.

## Notification service (`src/FieldService.Notifications`)

`src/config.ts` reads the environment once at startup and returns a frozen configuration.

- Each value is trimmed, and an empty value counts as unset, so an empty line copied from `.env.example` selects the default or fails as `is required`.
- List values are split on whitespace.
- The service checks every variable before it stops. It then writes one line to stderr, `config.invalid: <variable> <rule>`, with one `<variable> <rule>` pair per invalid variable, separated by `; `, and exits with code 1. The line names the variable and the rule, never the value. It is written before the logger exists, so it appears on the console and not in the log files.

| Variable | Purpose | Development (`.env`) | Test | Staging (Compose) | Production | Secret | Rules and default |
|---|---|---|---|---|---|---|---|
| `HOST` | Listen address | `127.0.0.1` | `127.0.0.1` | `0.0.0.0` | `0.0.0.0` | No | An IP address or a host name; default `127.0.0.1` |
| `PORT` | Listen port | `5090` | `0` (any free port) | `5090` | `5090` | No | An integer from 0 to 65535; default `5090` |
| `NODE_ENV` | Runtime mode | `development` | `test` | `production` | `production` | No | `development`, `test` or `production`; default `production` |
| `LOG_LEVEL` | pino level of every log target | `debug` | `silent`, or per test | `info` | `info` | No | `fatal`, `error`, `warn`, `info`, `debug`, `trace` or `silent`; default `info` |
| `LOG_DIR` | Folder of the log files, relative to the working directory | `../../logs/notifications` | A temporary folder | `/app/logs` | `/app/logs`, on a volume | No | Required. A missing folder is created |
| `LOG_PRETTY` | Readable console output through pino-pretty | `true` | `false` | `false` | `false` | No | `true` or `false`; default `false`. Honoured only when `NODE_ENV` is `development`, and ignored otherwise; the production path never loads pino-pretty |
| `WEBHOOK_SECRETS` | Secrets that verify the job events the API posts to `/internal/job-events`, newest first | The API's `Notifications:WebhookSecret` | Per test | `${NOTIFICATIONS_WEBHOOK_SECRET}` | Secret store; two values during a rotation | Yes | Required. 1 or 2 entries, each `whsec_` followed by standard base64, padding optional, that decodes to 32 bytes or more; no entry may be a published test key or a patterned key, and no entry may decode to the same bytes as `SOCKET_TICKET_KEY`. An event passes when its signature matches any entry |
| `WEBHOOK_TOLERANCE_SECONDS` | Largest difference allowed between an event's `webhook-timestamp` and the service's clock, in either direction | Empty (300) | Per test | Empty (300) | `300` | No | An integer from 1 to 300; default `300`. Accepted event ids are kept for 2 × this value + 60 seconds, so a replayed event is recognised for as long as its timestamp would pass |
| `SOCKET_TICKET_KEY` | HS256 key that verifies socket tickets | The API's `Auth:SocketTicketKey` | Per test | `${AUTH_SOCKET_TICKET_KEY}` | Secret store | Yes | Required. Standard base64, padding optional, that decodes to 32 bytes or more; not a published test key and not a patterned key. The service uses the decoded bytes, never the text |
| `SOCKET_TICKET_ISSUER` | Expected ticket issuer (`iss`) | Empty | Default | Default | Default | No | Any text; default `fieldservice-api`. The API always issues tickets as `fieldservice-api`, so another value refuses every ticket |
| `SOCKET_TICKET_AUDIENCE` | Expected ticket audience (`aud`) | Empty | Default | Default | Default | No | Any text; default `fieldservice-notifications`, the audience the API always writes |
| `SOCKET_ALLOWED_ORIGINS` | Browser origins that may open a socket | `http://localhost:5173 http://127.0.0.1:5173` | Per test | `http://localhost:8081 http://127.0.0.1:8081 http://localhost:5173 http://127.0.0.1:5173` | The public web origin | No | Required. 1 to 10 entries, each an absolute `http` or `https` origin written exactly as a browser sends it in the `Origin` header: a lower-case host, no default port, no path, no trailing `/` and no `*` |
| `SOCKET_SESSION_MAX_SECONDS` | Lifetime of one socket connection | Empty (900) | Small, in the session-cap test | Empty (900) | `900` | No | An integer from 1 to 3600; default `900`. The service disconnects each socket this long after it connected, and the dashboard reconnects with a new ticket |

- **Published and patterned keys.** The service refuses the same values as the API ([Startup checks](#startup-checks)). A published test key is one of the three 32-byte runs 0x00 to 0x1F, 0x20 to 0x3F and 0x40 to 0x5F; a patterned key has decoded bytes that are all equal or each differ from the previous byte by the same step, modulo 256. `docs/contracts/events/signature-test-vector.json` publishes a `whsec_` secret and `docs/contracts/events/socket-ticket-test-vector.json` a ticket key, so either value, copied into `WEBHOOK_SECRETS` or `SOCKET_TICKET_KEY`, stops the service with `must not be a published test key`. A hand-typed value fails as `must not be a patterned key` or as a format error.
- **Webhook secret and ticket key.** Like the API, which refuses a `Notifications:WebhookSecret` equal to `Auth:SocketTicketKey`, the service refuses any `WEBHOOK_SECRETS` entry, the older one during a rotation included, whose decoded bytes equal those of `SOCKET_TICKET_KEY`: `config.invalid: WEBHOOK_SECRETS entries must differ from SOCKET_TICKET_KEY`.
- **Rule texts.** `is required`; `must be an IP literal or a host name`; `must be an integer from <minimum> to <maximum>`; `must be development, test or production`; `must be fatal, error, warn, info, debug, trace or silent`; `must be true or false`; `must hold 1 or 2 space-separated entries`; `entries must be whsec_ followed by base64 of at least 32 bytes`; `must be base64 of at least 32 bytes`; `entries must differ from SOCKET_TICKET_KEY`; `must not be a published test key`; `must not be a patterned key`; `must hold 1 to 10 space-separated origins`; and `entries must be absolute http or https origins with no path and no *`. For example: `config.invalid: SOCKET_TICKET_KEY must not be a published test key`.
- **Clients without an `Origin` header.** The origin check applies to browsers. A client that sends no `Origin` header, such as a test client, passes it and still needs a valid ticket.
- `WEBHOOK_SECRETS` holds one secret normally and two during a rotation (see [Secrets](#secrets)).
- The service logs the event `server.listening`, with `host` and `port`, once it accepts connections ([logging.md](logging.md#notification-service-events)).

### Values shared with the API

Each value below must agree between the two services. Each service validates only its own values, so a mismatch passes both startups and shows in the logs at run time ([logging.md](logging.md)). `create-dev-env.mjs` writes one value per secret into every output, so the local pairs always agree.

| Value | API | Notification service | Compose variable | When they disagree |
|---|---|---|---|---|
| Socket-ticket key | `Auth:SocketTicketKey` | `SOCKET_TICKET_KEY`, the same decoded bytes | `AUTH_SOCKET_TICKET_KEY` | Every socket is refused with `socket.rejected`, reason `signature`, and the dashboard never goes live |
| Webhook secret | `Notifications:WebhookSecret` | One of the `WEBHOOK_SECRETS` entries | `NOTIFICATIONS_WEBHOOK_SECRET` | Every event is refused with 401: the notification service logs `job-event.rejected` with the reason `signature-mismatch`, and the API logs `job-event.dispatch-failed` with the reason `rejected` and the status 401. The job changes themselves are saved |
| Ticket issuer and audience | Fixed: `fieldservice-api` and `fieldservice-notifications` | `SOCKET_TICKET_ISSUER` and `SOCKET_TICKET_AUDIENCE` | None | Every socket is refused with `socket.rejected`, reason `claims` |
| Address of the notification service | `Notifications:BaseUrl` | `HOST` and `PORT` | None | The API logs `job-event.dispatch-failed` for each change, with the reason `unreachable`, or `circuit-open` while the circuit breaker is open |
| Dashboard origin | None: the address the browser opens | `SOCKET_ALLOWED_ORIGINS` | None | The socket upgrade is refused, and the notification service logs `socket.rejected` with the reason `origin-not-allowed` |

## Web dashboard and end-to-end tests (`src/FieldService.Web`)

| Variable | Read by | Purpose | Default | Example | Secret |
|---|---|---|---|---|---|
| `API_PROXY_TARGET` | `vite.config.ts` | Target of the `/api` proxy of the dev server and of `vite preview` | `http://127.0.0.1:5080` | `http://127.0.0.1:5081`, when the API runs on another port | No |
| `REALTIME_PROXY_TARGET` | `vite.config.ts` | Target of the `/socket.io` proxy, WebSocket included, of the dev server and of `vite preview` | `http://127.0.0.1:5090` | `http://127.0.0.1:5091` | No |
| `E2E_BASE_URL` | `playwright.config.ts` | Overrides the base URL of the Playwright project that runs | The project's own base URL: `compose` http://127.0.0.1:8081, `host` http://127.0.0.1:5173 | `http://127.0.0.1:8081`, as CI sets it | No |
| `E2E_DEMO_PASSWORD` | Playwright tests | Demo account password for sign-in; one value serves the host stack and the Compose stack | Locally, `playwright.config.ts` loads it from `src/FieldService.Web/e2e/.env` when it is unset. CI exports it | `<demo password>` | Yes, local only |
| `E2E_LOGS_DIR` | Playwright global teardown | The folder whose `api` and `notifications` logs are scanned for leaked secrets after a run | `../../logs`, the repository's `logs/` seen from `src/FieldService.Web` | `../../logs`, as CI sets it | No |
| `E2E_RUN_ID` | Playwright global setup | Marks every record a run creates | Created per run from the time and four random characters | Leave unset | No |
| `PLAYWRIGHT_HTML_OUTPUT_DIR`, `PLAYWRIGHT_HTML_REPORT` | Playwright, and the report scan that runs after each test run | Playwright's own variables for the HTML report folder; the report scan reads the same folder, so it also scans a moved report | Unset: the configured `playwright-report` | Leave unset | No |
| `CI` | `playwright.config.ts`, Vitest | Set by the CI runner. Turns on `forbidOnly`, one retry, failure on flaky tests and the CI reporters | Unset locally | `true`, on the runner | No |

- No `VITE_` variable exists, and none may hold a secret, because Vite inlines such variables into the bundle.
- The dev server and `vite preview` both bind 127.0.0.1:5173 with `strictPort`. Both proxy `/api` with `changeOrigin` and `xfwd`, so the API sees the browser's address through its forwarded-headers handling, and `/socket.io` with WebSocket support. Only `vite preview` sends the production security headers: `Content-Security-Policy`, `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Permissions-Policy`, `Cross-Origin-Opener-Policy` and `Cross-Origin-Resource-Policy`, with the values nginx sends.

**Playwright projects.** Each package script runs one project:

| Project | Stack under test | Script |
|---|---|---|
| `compose` | The Compose `full` profile: nginx with the production Content Security Policy, the API in Staging | `npm run test:e2e` |
| `host` | The host processes, with the dashboard on 127.0.0.1:5173 served by the Vite dev server or by `vite preview` | `npm run test:e2e:host` |

Specs tagged `@compose-only` run only in `compose`, and specs tagged `@quarantine` are excluded from both scripts.

## Technician app (`src/FieldService.Mobile`)

| Property | Purpose | Debug | Release |
|---|---|---|---|
| `ApplicationId`, `ApplicationTitle` | Android package name and Windows identity; the display name | `io.github.ranielquirante.fieldservice.tech`, `FieldService Tech` | Same |
| `ApiBaseUrl` | API root, embedded as assembly metadata | `http://10.0.2.2:5080` on Android, `http://localhost:5080` on Windows; no default on the plain `net10.0` target, which only the tests build | Required on the Android and Windows targets, and it must use `https`; the build fails otherwise, and a runtime check enforces the same rule. CI passes `https://fieldservice.example` |
| Log level | Minimum log level, fixed at compile time | Debug | Information |
| `RuntimeIdentifierOverride` | Windows publish architecture | Host default | `win-x64` in CI |
| `WindowsPackageType` | Unpackaged Windows app | `None` | `None` |
| `AndroidPackageFormats` | Package format of the CI Release build | Not set | `apk` in CI |
| `AndroidSdkDirectory`, `JavaSdkDirectory`, `AcceptAndroidSDKLicenses` | One-time `InstallAndroidDependencies` on a developer machine | Absolute paths | Not set |
| `AndroidKeyStore`, `AndroidSigningKeyStore`, `AndroidSigningKeyAlias`, `AndroidSigningStorePass`, `AndroidSigningKeyPass` | Signing of a store build ([deployment.md](deployment.md#production-guidance)) | Not set | Not set in this repository's builds; a store build passes the two passwords as `env:<VARIABLE>` references, never as values |

## Database bootstrap and API test runs

| Variable | Read by | Purpose | Local | CI | Secret |
|---|---|---|---|---|---|
| `FIELDSERVICE_MIGRATOR_PASSWORD` | `bootstrap.sql` (`\getenv`), the `db` container, the test fixture | Password set on, and used for, `fieldservice_migrator` | Created by `create-dev-env.mjs`; stored in `infrastructure/.env` and in the test user-secrets store | Exported by `create-dev-env.mjs --ci` | Yes |
| `FIELDSERVICE_APP_PASSWORD` | As above | The same, for `fieldservice_app` | As above | As above | Yes |
| `FIELDSERVICE_TEST_ADMIN_CONNECTION` | API test fixture | Superuser connection that runs `bootstrap.sql` and creates and drops `fieldservice_test_<run>` databases | macOS: `Host=127.0.0.1;Port=5432;Username=<macOS account>;Database=postgres`, with trust authentication. Windows: `Username=postgres` with the installer password. Stored in the test user-secrets store | `Host=127.0.0.1;Port=5433;Username=postgres;Password=…;Database=postgres` | Yes, when it holds a password |
| `FIELDSERVICE_UPDATE_CONTRACTS` | OpenAPI snapshot test | `1` rewrites `docs/contracts/openapi.v1.json` instead of comparing | Unset; set by hand for a reviewed contract update | Never set | No |

- The fixture reads each `FIELDSERVICE_*` value from the environment first, then from the user-secrets store `fieldservice-api-tests`. It never reads `infrastructure/.env` and never creates a password.
- `bootstrap.sql` sets both role passwords on every run, so running it again re-synchronises them. It refuses a password shorter than 16 characters.
- The database name comes from the psql variable `db_name`, default `fieldservice`; the fixture passes `-v db_name=fieldservice_test_<run>`.
- The fixture runs `bootstrap.sql` through `psql`, and `\getenv` needs psql 15 or later. On macOS Homebrew's `postgresql@17` provides it; on Windows, add the PostgreSQL `bin` folder to `PATH`.

## Toolchain and CI variables

| Variable | Where | Value |
|---|---|---|
| `DOTNET_NOLOGO`, `DOTNET_CLI_TELEMETRY_OPTOUT`, `TESTINGPLATFORM_TELEMETRY_OPTOUT` | CI; optional on developer machines | `true` |
| `DOTNET_WATCH_RESTART_ON_RUDE_EDIT` | Set by `start-dev.mjs` for the API's `dotnet watch` | `true`: an edit that hot reload cannot apply restarts the API instead of prompting |
| `CI` | Set by the CI runner | `true`. `Directory.Build.props` then turns on locked restores for every .NET project with a lock file |
| `GITHUB_ENV` | Set by the CI runner | `create-dev-env.mjs --ci` appends the test and end-to-end values here |
| `JAVA_HOME` | Developer machines | macOS: `$(/usr/libexec/java_home -v 21)`. Windows: the Microsoft OpenJDK 21 folder |
| `ANDROID_HOME` | Developer machines | macOS: `$HOME/Library/Android/sdk`. Windows: `%LOCALAPPDATA%\Android\Sdk` |
| `DOTNET_ROOT` | Developer machines | Unset. It must not point into `/opt/homebrew/Cellar/dotnet/`; the Microsoft SDK installs to `/usr/local/share/dotnet` |
| `PGPASSWORD` | Optional, for `create-dev-env.mjs --bootstrap` and `dropdb` on a password-protected server (Windows) | The superuser password, for one terminal session; never written to disk by the scripts |

## Secrets

### Formats, stores and rotation

| Secret | Format | Development | Staging | CI | Production | Rotation |
|---|---|---|---|---|---|---|
| Compose superuser password | 24 random bytes, base64url (32 characters) | `infrastructure/.env` | Same | Throwaway | Managed by the database service | Reset the Compose volume, or `ALTER ROLE postgres` |
| `fieldservice_migrator` and `fieldservice_app` passwords | Same | `infrastructure/.env`, API user-secrets, test user-secrets | `infrastructure/.env` | Throwaway | Secret store | psql's `\password fieldservice_app` or `\password fieldservice_migrator`, which sends a SCRAM verifier and keeps the plain text out of psql's history, or the bootstrap script with the new values in its two environment variables; then update the connection string and restart ([deployment.md](deployment.md#production-guidance)) |
| `Auth:AccessTokenKey` | 32 random bytes, standard base64 | API user-secrets | `infrastructure/.env` | Throwaway | Secret store | Replace it and restart the API; clients refresh their sessions silently |
| `Auth:SocketTicketKey` and `SOCKET_TICKET_KEY` | 32 random bytes, standard base64 | API user-secrets and the notification service's `.env` | `infrastructure/.env` | Throwaway | Secret store | Replace it in both services together. Tickets last 60 seconds, and open sockets reconnect within the 900-second session cap |
| `Notifications:WebhookSecret` and `WEBHOOK_SECRETS` | `whsec_` followed by standard base64 of 32 random bytes | API user-secrets and the notification service's `.env` | `infrastructure/.env` | Throwaway | Secret store | Without downtime: (1) the notification service gets the new and the old secret, newest first; (2) the API switches to the new secret; (3) the notification service drops the old one |
| `Seed:DemoPassword` and `E2E_DEMO_PASSWORD` | 16 characters from `abcdefghjkmnpqrstuvwxyz23456789`, grouped `xxxx-xxxx-xxxx-xxxx` | API user-secrets and `src/FieldService.Web/e2e/.env` | `infrastructure/.env` | Throwaway (`E2E_DEMO_PASSWORD`) | None | `create-dev-env.mjs --force`, then reset the database, because existing password hashes are not rewritten |

- The values `create-dev-env.mjs` creates never contain `$`, `#`, quotes, spaces or `;`, so they are safe in env files, in Compose interpolation and inside connection strings.
- Values come only from Node's `crypto.randomBytes`.
- One value per secret feeds every output: `AUTH_SOCKET_TICKET_KEY`, `Auth:SocketTicketKey` and `SOCKET_TICKET_KEY` are identical, and so are `SEED_DEMO_PASSWORD`, `Seed:DemoPassword` and `E2E_DEMO_PASSWORD`.

### Local files and stores

The two `.env.example` files list the variable names with empty values, in a stable order:

- `infrastructure/.env.example`: `DB_HOST_PORT`, `NETWORK_SUBNET`, `NETWORK_IP_RANGE`, `WEB_IPV4_ADDRESS`, `POSTGRES_PASSWORD`, `FIELDSERVICE_MIGRATOR_PASSWORD`, `FIELDSERVICE_APP_PASSWORD`, `AUTH_ACCESS_TOKEN_KEY`, `AUTH_SOCKET_TICKET_KEY`, `NOTIFICATIONS_WEBHOOK_SECRET`, `SEED_DEMO_PASSWORD`.
- `src/FieldService.Notifications/.env.example`: `HOST`, `PORT`, `NODE_ENV`, `LOG_LEVEL`, `LOG_DIR`, `LOG_PRETTY`, `WEBHOOK_SECRETS`, `WEBHOOK_TOLERANCE_SECONDS`, `SOCKET_TICKET_KEY`, `SOCKET_TICKET_ISSUER`, `SOCKET_TICKET_AUDIENCE`, `SOCKET_ALLOWED_ORIGINS`, `SOCKET_SESSION_MAX_SECONDS`.

`create-dev-env.mjs` writes the real files, which git ignores, with mode 0600 on macOS and Linux:

| File | Holds |
|---|---|
| `infrastructure/.env` | Every Compose variable. The four settings `DB_HOST_PORT`, `NETWORK_SUBNET`, `NETWORK_IP_RANGE` and `WEB_IPV4_ADDRESS` stay empty, so the Compose defaults apply; a value set by hand is kept |
| `src/FieldService.Notifications/.env` | Every notification service variable, with the development values of the table above; the two secrets are created |
| `src/FieldService.Web/e2e/.env` | One line, `E2E_DEMO_PASSWORD`, the same value as `SEED_DEMO_PASSWORD` |

The user-secrets stores live outside the repository, at `~/.microsoft/usersecrets/<id>/secrets.json` on macOS and Linux and `%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json` on Windows:

| Store id | Declared by | Keys |
|---|---|---|
| `fieldservice-api` | `src/FieldService.Api/FieldService.Api.csproj` | `ConnectionStrings:FieldService`, `ConnectionStrings:Migrations`, `Auth:AccessTokenKey`, `Auth:SocketTicketKey`, `Notifications:WebhookSecret`, `Seed:DemoPassword` |
| `fieldservice-api-tests` | `tests/FieldService.Api.Tests/FieldService.Api.Tests.csproj` | `FIELDSERVICE_TEST_ADMIN_CONNECTION` (written automatically on macOS and Linux, or whenever `PGPASSWORD` is set), `FIELDSERVICE_MIGRATOR_PASSWORD`, `FIELDSERVICE_APP_PASSWORD` |

`create-dev-env.mjs` addresses each store by its id, so it can fill a store before its project exists.

### Production

Every production secret reaches the services as an environment variable, injected from the platform's secret store: `ConnectionStrings__FieldService`, `Auth__AccessTokenKey`, `Auth__SocketTicketKey`, `Notifications__WebhookSecret`, the notification service's `WEBHOOK_SECRETS` and `SOCKET_TICKET_KEY`, and `ConnectionStrings__Migrations` for the release step only. There is no file-based secret provider and no `*_FILE` variable. Create each secret in the format of the table above, for example with `openssl rand -base64 32`, with `whsec_` in front for the webhook secret. Create every key and secret separately: the API refuses equal keys, a published test key and a patterned key ([Startup checks](#startup-checks)), and the notification service refuses a webhook secret equal to its ticket key ([Notification service](#notification-service-srcfieldservicenotifications)), so a value copied from documentation or a test fails at startup.
