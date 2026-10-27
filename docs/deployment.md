# Deployment

This document describes the container images of FieldService, how to build and run them with Docker Compose as the Staging stack on one machine, the release step that applies the database migrations, and guidance for a production deployment. FieldService has no continuous delivery: images are built on a workstation or by the build pipeline, and a release is a deliberate set of steps. Every setting named here is listed in [configuration.md](configuration.md), the log files in [logging.md](logging.md), and the database roles and the bootstrap script in [database.md](database.md).

Run every command from the repository root.

## Images

| Image (local tag) | Dockerfile and target | Build stage | Runtime stage | User | Port | Contents |
|---|---|---|---|---|---|---|
| `fieldservice/api:local` | `src/FieldService.Api/Dockerfile`, `runtime` (the default target) | `mcr.microsoft.com/dotnet/sdk:10.0.402-noble` | `mcr.microsoft.com/dotnet/aspnet:10.0.13-noble-chiseled-extra` | 1654 | 8080 | The framework-dependent Release publish of the API, started with `dotnet FieldService.Api.dll`; `appsettings.json` as its only settings file; `/app/logs`, owned by 1654. The chiseled base has ICU and time zone data, and no shell or package manager |
| `fieldservice/migrate:local` | `src/FieldService.Api/Dockerfile`, `migrator` | The same SDK image | The same runtime image | 1654 | None | `/app/efbundle`, a self-contained Entity Framework Core migrations bundle for the architecture of the build machine |
| `fieldservice/notifications:local` | `src/FieldService.Notifications/Dockerfile` | `node:26.12.0-alpine3.24` | `node:26.12.0-alpine3.24` | `node` (1000) | 5090 | The compiled service in `dist/` and its production dependencies only. The files belong to root and are read-only for `node`; only `/app/logs` is writable |
| `fieldservice/web:local` | `src/FieldService.Web/Dockerfile` | `node:26.12.0-alpine3.24` | `nginxinc/nginx-unprivileged:1.30.6-alpine3.24` (nginx 1.30.6) | 101 | 8080 | The production build of the dashboard, served by nginx with `src/FieldService.Web/nginx/nginx.conf` |

Measured on an Apple Silicon Mac (arm64), as disk use and compressed content size: `api` 256 MB and 71 MB, `migrate` 446 MB and 123 MB, `notifications` 309 MB and 72 MB, `web` 92 MB and 26 MB.

**How the images are built**

- **From the repository root, with an allowlist context.** Each Dockerfile has a `Dockerfile.dockerignore` beside it, which Docker reads in place of a root `.dockerignore`. It ignores everything, re-includes only the paths its image needs, and then excludes env files, logs and build output. Env files, `logs/`, `node_modules`, `bin`, `obj` and local tool folders never enter a build context.
- **API.** `nuget.config` is copied before the restore, so nuget.org is the only package source. The restore runs in locked mode against the committed `packages.lock.json` and in the Release configuration, like the build pipeline, with high and critical package advisories as errors. The build context leaves out `appsettings.Development.json` and `appsettings.Testing.json`, so the image holds `appsettings.json` only. The image holds no API explorer assembly ([ADR 0017](decisions/0017-api-explorer-in-development.md)).
- **Migrations bundle.** The `bundle` stage runs `dotnet ef migrations bundle --self-contained` for `linux-arm64` or `linux-x64`, from the build's target architecture. Creating a bundle constructs the design-time database context, which requires `ConnectionStrings:Migrations`, so that stage sets a value without a password for the build alone; the bundle opens no connection while it is built, and it reads the real `ConnectionStrings__Migrations` when it runs.
- **Notification service.** `npm ci --ignore-scripts` and `tsc` in the build stage; `npm ci --omit=dev --ignore-scripts` in the runtime stage, which then deletes npm's folder `/root/.npm`, with its package cache and debug logs, and npm's compile cache in `/tmp` in the same step, so the image holds neither. The process starts as `node dist/main.js`, never through `npm start`.
- **Web dashboard.** The build stage mirrors the repository layout, so `npm run generate:api` reads `docs/contracts/openapi.v1.json`, then runs `vite build` only; type checking runs in the build pipeline. The context includes `e2e/tsconfig.json`, which the dashboard's root `tsconfig.json` references, and nothing else of `e2e/`. nginx's own default site configuration is never loaded.
- **No parser directive and no `HEALTHCHECK`** in any Dockerfile. Health checks live in the Compose file.
- **Architectures.** The images build natively, arm64 on Apple Silicon and amd64 on the build pipeline's runners. The pipeline's `e2e` job builds all four images for every pull request and every push to `main`, starts the `full` profile from them and runs the end-to-end tests against nginx; it neither uploads nor pushes the images. Release images come from the pipeline.

## Running the Staging stack with Compose

The Compose project `fieldservice` (`infrastructure/docker-compose.yml`) runs the Staging environment on one machine, bound to loopback only. Staging runs the Production code paths, such as Secure cookies, JSON console logs and no OpenAPI document, and differs from Production in two ways: it seeds the demo data, and it is reachable only from the machine itself.

**Prerequisites**

- Docker Desktop 4.95 or later, with Compose 5.6 or later.
- Node 26.12.0, to run `create-dev-env.mjs`.
- `infrastructure/.env` with every Compose variable, and the folders `logs/api` and `logs/notifications`. `create-dev-env.mjs` creates all of them; on a machine without the .NET SDK, run `node infrastructure/scripts/create-dev-env.mjs --only compose`. It prints the paths it writes and the names of the values, never a value.

**Commands**

| Goal | Command |
|---|---|
| Start PostgreSQL only | `docker compose -f infrastructure/docker-compose.yml up -d --wait db` |
| Backend in containers, dashboard on the host | `docker compose -f infrastructure/docker-compose.yml --profile backend up -d --build --wait`, then `npm run dev` in `src/FieldService.Web` |
| Full Staging stack | `docker compose -f infrastructure/docker-compose.yml --profile full up -d --build --wait` |
| Wait until the API is ready | `curl --fail --silent --show-error --retry 30 --retry-delay 2 --retry-all-errors http://127.0.0.1:5080/health/ready` |
| Follow the service logs | `docker compose -f infrastructure/docker-compose.yml --profile full logs -f api notifications web` |
| Stop everything | `docker compose -f infrastructure/docker-compose.yml --profile full down` |
| Stop and delete the database volume | `docker compose -f infrastructure/docker-compose.yml --profile full down -v` |
| Validate the file without starting anything | `docker compose -f infrastructure/docker-compose.yml --profile full config --quiet` |

- `down` handles only the services of the active profiles, so the stop commands name `--profile full`, which includes everything in `backend`.
- `config` without `--quiet` prints the whole configuration with every secret filled in; keep `--quiet`.
- Run each service either on the host or in Compose, never both: they use the same host ports.

**Using the stack.** Open http://localhost:8081 or http://127.0.0.1:8081 in Chrome, Firefox or Edge. Safari does not keep the Secure refresh cookie on plain http, so a reload signs it out. Sign in as the demo administrator, `dana.whitfield@fieldservice.example`, with the demo password that `node infrastructure/scripts/create-dev-env.mjs --show-demo-password` prints in your own terminal. The technician app's Debug build reaches the containerized API at `http://10.0.2.2:5080` from the Android emulator and at `http://localhost:5080` on Windows.

### Services

| Service | Profiles | Image | Host binding | Health | Starts after | Writable paths |
|---|---|---|---|---|---|---|
| `db` | Always | `postgres:17.11-trixie` | 127.0.0.1:5433 (`DB_HOST_PORT`) to 5432 | `pg_isready` over TCP | — | The volume `fieldservice-pgdata` |
| `migrate` | `backend`, `full` | `fieldservice/migrate:local` | None | Runs once and exits 0 | `db` healthy | `/tmp` |
| `api` | `backend`, `full` | `fieldservice/api:local` | 127.0.0.1:5080 to 8080 | None in the container; poll `/health/ready` from the host | `migrate` completed with exit code 0 | `logs/api` as `/app/logs`; `/tmp` |
| `notifications` | `backend`, `full` | `fieldservice/notifications:local` | 127.0.0.1:5090 to 5090 | `node` requests `/health/live` through `node:http`; a 2xx status passes | — | `logs/notifications` as `/app/logs`; `/tmp` |
| `web` | `full` | `fieldservice/web:local` | 127.0.0.1:8081 to 8080 | `wget` of `/` | `api` started and `notifications` healthy | `/tmp` |

The environment of each service is listed in [configuration.md](configuration.md#staging-in-compose).

### Start order and readiness

`db` becomes healthy, `migrate` applies any pending migration and exits 0, `api` and `notifications` start, and `web` starts once `notifications` is healthy. `up --wait` returns when every service with a health check is healthy and `migrate` has completed.

The API's chiseled image has no shell and no HTTP client, so the `api` service has no health check, and `up --wait` counts it as ready as soon as its container runs. When the API refuses its configuration, it stops at once with exit code 134 and Compose restarts it, but `up --wait` can still report success. Poll `/health/ready` from the host after every start, as in the table above; on a refused start it never answers, and `docker compose -f infrastructure/docker-compose.yml --profile full logs api` shows the `config.invalid` events, each naming a key and a rule.

`db`, `api`, `notifications` and `web` restart with Docker unless they were stopped; `migrate` never restarts, and the API's own check for pending migrations stops it if a migration was skipped.

### Container hardening

| Control | `migrate`, `api`, `notifications`, `web` | `db` |
|---|---|---|
| Root filesystem | Read-only | Writable, as the PostgreSQL image requires |
| `/tmp` | tmpfs, mounted `nosuid`, `nodev` and `noexec` | Image default |
| Linux capabilities | All dropped | The image's defaults: its entrypoint changes file ownership, then runs as `postgres` |
| `no-new-privileges` | Yes | Yes |
| User | 1654 (`migrate`, `api`), 1000 (`notifications`), 101 (`web`) | `postgres` after the entrypoint |
| Init process | Docker's init as PID 1 for `migrate`, `api` and `notifications`; nginx manages its own processes | The image's entrypoint |
| Console logs | `json-file`, 3 files of 10 MB per service | The same |

A process that runs as PID 1 in a container receives no signal it has no handler for, so a .NET process in that position cannot end itself after a fatal error. With Docker's init as PID 1, the API and the migrations bundle exit with their real exit code, and Compose sees the failure.

### Network and the trusted proxy

The services share one network, `fieldservice-net`, with the subnet `${NETWORK_SUBNET:-172.30.80.0/24}`. `web` has the fixed address `${WEB_IPV4_ADDRESS:-172.30.80.10}`, and the API trusts that address, and nothing else, as its proxy (`ForwardedHeaders__KnownProxies__0`). Docker takes the gateway's address and every address it assigns to a container only from the range `${NETWORK_IP_RANGE:-172.30.80.128/25}`, the upper half of the subnet. The fixed address lies outside that range, so no container that Docker assigns an address to, a one-off `docker compose run` container included, can take it before `web` starts; only a container given that address explicitly, which needs Docker access on the host, can. No network range is trusted: callers of the published ports reach the containers from the network's gateway inside the subnet, so trusting the subnet would let any local caller choose its own client address.

- Through nginx, the API takes the client address from the `X-Forwarded-For` header that nginx writes, which holds the address nginx saw. A header sent by the client is replaced, never extended.
- Directly on 5080, the API ignores a client's `X-Forwarded-For` and uses the connection's address.
- On Docker Desktop every local client reaches the containers from the gateway address, which Docker takes from the start of the range, 172.30.80.128 by default, so in Staging all local clients share one partition of the per-address rate limits.
- Docker refuses an `ipv4_address` or a range outside the subnet, so a changed `NETWORK_SUBNET` without a matching `NETWORK_IP_RANGE` and `WEB_IPV4_ADDRESS` fails at `up` instead of trusting the wrong address. Docker does not check that the fixed address lies outside the range: keep `WEB_IPV4_ADDRESS` out of `NETWORK_IP_RANGE`.

### The web server

nginx serves the dashboard and proxies two paths:

| Path | Handled by |
|---|---|
| `/api/` | The API at `api:8080`, with `Host`, `X-Forwarded-For` set to the address nginx saw, and `X-Forwarded-Proto` set to the request's own scheme |
| `/socket.io/` | The notification service at `notifications:5090`, with WebSocket upgrade and the same forwarded headers |
| `/internal` and `/health`, and everything below them | `404` from nginx itself; nothing is proxied |
| `/assets/` | The hashed build files; a missing file is `404` |
| Every other path | The file if it exists, otherwise `index.html`, so a deep link such as `/jobs/<id>` opens the dashboard |

- The OpenAPI document and `/swagger` do not exist behind nginx; those paths open the dashboard like any other unknown path.
- nginx looks up `api` and `notifications` in Docker's DNS and keeps each answer for 10 seconds, so a restarted container is reached again without restarting nginx.
- `client_max_body_size` is 1 MB, the API's own limit. `server_tokens` is off, so the `Server` header carries no version. Redirects that nginx makes itself, such as `/assets` to `/assets/`, are relative.
- Text files are compressed with gzip, with `Vary: Accept-Encoding`.

**Response headers.** Every response from nginx carries these headers, on errors and `304 Not Modified` responses too:

| Header | Value |
|---|---|
| `Content-Security-Policy` | `default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'` |
| `X-Content-Type-Options` | `nosniff` |
| `X-Frame-Options` | `DENY` |
| `Referrer-Policy` | `no-referrer` |
| `Permissions-Policy` | `camera=(), microphone=(), geolocation=(), accelerometer=(), gyroscope=(), magnetometer=(), payment=(), usb=()` |
| `Cross-Origin-Opener-Policy` | `same-origin` |
| `Cross-Origin-Resource-Policy` | `same-origin` |

`vite preview` sends the same seven headers with the same values. Responses from the API keep the API's own headers as well, among them its stricter `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'`; a browser applies both policies.

**Caching**

| Response | `Cache-Control` |
|---|---|
| `index.html`, the deep links that serve it, and other files outside `/assets/` | `no-cache`: the browser revalidates with the `ETag` and gets `304` while the file is unchanged |
| A hashed file under `/assets/`, with status 200 or 304 | `public, max-age=31536000, immutable` |
| A missing file under `/assets/`, and nginx's own error pages | `no-cache` |
| `/api/` and `/socket.io/` | None from nginx; the API sends `no-store` on every API response |

**Logs.** The access log is one JSON object per line on stdout, with the time, method, path without its query string, status, bytes, duration and correlation id; it records no client address, no body, and no header apart from the correlation id. The correlation id is the `X-Correlation-ID` response header of the API or the notification service when nginx proxied the request and the service returned one, and otherwise the request's `X-Correlation-ID` header. nginx writes either value only when it is 1 to 64 letters, digits, `.`, `_` or `-`, the rule both services apply, and leaves the field empty otherwise. A proxied API request whose header the API refused is therefore logged with the id the API created for it, and a request nginx answers itself, such as a file, the dashboard fallback or its own `404`, with the caller's id when that is valid. A path served through the dashboard fallback is logged as `/index.html`. Errors go to stderr at the `crit` level only: nginx's error lines quote the full request line, query string included, the client address and the referrer, so lower levels would write search terms and addresses to the container logs. An unreachable upstream still shows in the access log as status `502`.

### Data and volumes

The database lives in the named volume `fieldservice-pgdata`. On its first start with an empty volume, the `db` container runs `infrastructure/postgres/bootstrap.sql` with the two role passwords from `infrastructure/.env`, and `migrate` creates the schema. `down -v` deletes the volume; the next `up` bootstraps a new database, and the API seeds the demo data again. After `create-dev-env.mjs --force`, delete the volume this way, because the bootstrap script runs only on an empty volume.

### Logs

The API writes to `/app/logs`, bind-mounted from `logs/api`, and the notification service to `/app/logs`, bind-mounted from `logs/notifications`; both also write JSON lines to the console, which Docker keeps in 3 files of 10 MB per service. The mounts never create a missing host folder, so a missing `logs/api` or `logs/notifications` stops `up` with "bind source path does not exist" instead of creating a folder the container cannot write. On a Linux host, `logs/api` must be writable by UID 1654 and `logs/notifications` by UID 1000; Docker Desktop on macOS and Windows maps the ownership through its file sharing. Details are in [logging.md](logging.md#containers).

## Release step

A release applies the database changes before the new services start.

1. **Database roles, once per environment.** A database administrator runs `infrastructure/postgres/bootstrap.sql` with `psql`, with `FIELDSERVICE_MIGRATOR_PASSWORD` and `FIELDSERVICE_APP_PASSWORD` in the environment. It creates `fieldservice_migrator`, which owns the schema, and `fieldservice_app`, which the API uses, and the `fieldservice` database owned by the migrator role ([database.md](database.md)). On a managed service, confirm that the administrator role can create roles and give database ownership: PostgreSQL 16 and later require membership in a role to make it an owner.
2. **Migrations.** Run the `migrate` image once, with `ConnectionStrings__Migrations` set to the migrator role's connection string; it applies every pending migration and exits 0, or stops at the first migration that fails and exits with a non-zero code. Alternatively, apply a reviewed script from `dotnet ef migrations script --idempotent --project src/FieldService.Api`. REST changes are additive only, so the running API version keeps working on the new schema.
3. **API.**
4. **Notification service.**
5. **Web server.**

A rollback deploys the previous images again. Migrations are never rolled back automatically. In Development and Staging the API refuses to start while a migration is pending; in Production that check does not run, so the migration step always comes first.

## Production guidance

**Topology**

- The internet reaches a TLS load balancer or ingress, and behind it the web image (nginx).
- nginx serves the dashboard and proxies `/api/` to the API and `/socket.io/` to the notification service, both on a private network.
- The API connects to a managed PostgreSQL 17 server over TLS, and posts job events to the notification service's `/internal/job-events` over the private network with TLS.
- The technician app calls `https://<public host>/api/…`.
- Only nginx is public. `/internal`, `/health` and `/openapi` are never routed from outside.

**Artifacts**

- The images `fieldservice-api`, `fieldservice-migrate`, `fieldservice-notifications` and `fieldservice-web`, built by the pipeline on amd64 from the same Dockerfiles, tagged with the git commit, pushed to a registry such as GitHub Container Registry, and never tagged `latest`.
- The technician app: an Android package signed with a store keystore, and the Windows app as an unpackaged folder or an MSIX package signed with a code-signing certificate.

**Configuration**

| Service | Values |
|---|---|
| API | `ASPNETCORE_ENVIRONMENT=Production`; `ConnectionStrings__FieldService` (app role, with `Maximum Pool Size=20;Application Name=fieldservice-api;SSL Mode=VerifyFull;Root Certificate=<CA path>`); `Auth__AccessTokenKey`; `Auth__SocketTicketKey`; `Notifications__BaseUrl`, the private `https` URL of the notification service; `Notifications__WebhookSecret`; `Cors__AllowedOrigins__0=https://<public host>`; `ForwardedHeaders__KnownProxies__0` and up, one entry per nginx instance and never the TLS proxy, with no `ForwardedHeaders__KnownNetworks__0`; `AllowedHosts=<public host>`; `Seed__DemoData=false` |
| Migrations | `ConnectionStrings__Migrations` (migrator role, with `Application Name=fieldservice-migrator` and TLS), for the release step only |
| Notification service | `NODE_ENV=production`, `HOST=0.0.0.0`, `PORT=5090`, `LOG_LEVEL=info`, `LOG_DIR=/app/logs`, `WEBHOOK_SECRETS`, `SOCKET_TICKET_KEY`, `SOCKET_ALLOWED_ORIGINS=https://<public host>` |
| Web server | Its own `nginx.conf` mounted over `/etc/nginx/nginx.conf`: the file of this repository with the upstream names and the resolver adapted to the platform, and the client-address chain below in place of the `map $scheme $forwarded_proto` block |
| Technician app | `-p:ApiBaseUrl=https://<public host>`; Android signing with `AndroidKeyStore=true` and the `AndroidSigning*` properties, with the passwords passed as `env:` references from the pipeline's secrets |

The API refuses to start in Production with `Seed:DemoData` set to `true`, with `*` in `AllowedHosts`, with an `http` URL in `Notifications:BaseUrl` or `Cors:AllowedOrigins`, or without a `ForwardedHeaders:KnownProxies` entry, which Staging requires as well. In every environment it refuses a `ForwardedHeaders:KnownProxies` entry that is not the IP address of one host ([configuration.md](configuration.md#startup-checks)).

The API uses no ASP.NET Core Data Protection: it registers none of its services, so it creates no key ring, and no key-ring volume is needed. A later feature that needs Data Protection must persist its keys to a volume first.

**Client address in production.** The TLS proxy appends the client's address to `X-Forwarded-For`. nginx's realip module, trusting only the TLS proxy's network, takes that address and passes it on as the single `X-Forwarded-For` entry, and the API trusts only nginx. At `http` level, with `10.20.0.0/24` standing for the TLS proxy's network:

```nginx
set_real_ip_from 10.20.0.0/24;
real_ip_header X-Forwarded-For;
real_ip_recursive on;

geo $realip_remote_addr $from_tls_proxy {
    default 0;
    10.20.0.0/24 1;
}

map $from_tls_proxy $forwarded_proto {
    default $scheme;
    1 $http_x_forwarded_proto;
}
```

- With `real_ip_recursive on`, the client address becomes the right-most `X-Forwarded-For` entry that is not a trusted proxy, the address the TLS proxy saw; entries that a client added are ignored.
- `X-Forwarded-Proto` is taken from the request only when it comes from the TLS proxy; otherwise nginx sends its own scheme.
- The proxied locations stay as in this repository's file: `proxy_set_header X-Forwarded-For $remote_addr` and `proxy_set_header X-Forwarded-Proto $forwarded_proto`.
- The image's nginx is built with the realip module (`nginx -V` lists `--with-http_realip_module`).

**Secrets.** Every secret reaches the services as an environment variable, typically from the platform's secret store: `ConnectionStrings__FieldService`, `Auth__AccessTokenKey`, `Auth__SocketTicketKey`, `Notifications__WebhookSecret`, the notification service's `WEBHOOK_SECRETS` and `SOCKET_TICKET_KEY`, and `ConnectionStrings__Migrations` for the release step only. There is no file-based secret provider and no `*_FILE` variable. Create each value in the format of [configuration.md](configuration.md#secrets), for example with `openssl rand -base64 32`, with `whsec_` in front for the webhook secret, and create each one separately: both services refuse at startup an empty value, a short value, a published test key and a patterned key, and the API refuses equal keys. Rotate them as described in [configuration.md](configuration.md#formats-stores-and-rotation); the webhook secret rotates without downtime through the two entries of `WEBHOOK_SECRETS`.

**TLS.** Terminate TLS at the load balancer or ingress. The API connects to PostgreSQL with `SSL Mode=VerifyFull` and the provider's CA certificate, and calls the notification service over `https`, which Production requires. Outside Development, the API adds `Strict-Transport-Security` with a one-year lifetime to responses of requests that reach it as `https`.

**Operations**

- **Health probes.** Liveness at `/health/live` and readiness at `/health/ready` on the API (port 8080) and on the notification service (port 5090), from inside the private network only. The API's readiness includes its database.
- **Networks and limits.** Split the network into an edge network (web, API, notification service) and a data network (database, migrations, API), so that nginx cannot reach the database. Set memory and process limits, for example 512 MB and 200 processes for the API, 256 MB and 100 for the migrations and the notification service, and 128 MB and 100 for nginx.
- **Logs.** Collect the JSON console output centrally and search it by correlation id. Keep file logs on a volume at `/app/logs` only where the platform has no log collector.
- **Database server log.** PostgreSQL's defaults write a failed statement's text and its `DETAIL` line to the server log, such as a rejected row's names, email address and phone number, an operator's literal query, or a role password from a failed `CREATE ROLE` or `ALTER ROLE`. Set `log_min_error_statement=panic` and `log_error_verbosity=terse` on the managed server, as the Compose `db` service does ([logging.md](logging.md#containers)); where the provider does not allow it, treat the database server log as personal data, with restricted access and a short retention. Keep `log_statement` at `none` and `log_min_duration_statement` at `-1`, the defaults: with `ddl` or `all`, every bootstrap run writes the role passwords of its `ALTER ROLE` statements to the log, and a duration threshold writes slow statements with their parameter values. Look rows up by id in operational queries.
- **Backups.** Daily backups with point-in-time recovery on the managed database, and a restore test every quarter. A later PostgreSQL major version is reached with `pg_upgrade` or the provider's major-version upgrade.
- **Scaling limits.** Run one instance of the notification service, whose duplicate detection, used-ticket record and socket rooms live in memory, and one instance of the API, so that rate limits stay global and job events keep their order. Running more instances needs a shared Socket.IO adapter, shared rate limiting and a transactional outbox for job events.

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| "required variable POSTGRES_PASSWORD is missing a value" | `infrastructure/.env` does not exist | Run `node infrastructure/scripts/create-dev-env.mjs`, or `--only compose` |
| "bind source path does not exist" | `logs/api` or `logs/notifications` is missing | Run `create-dev-env.mjs`, which creates both |
| `up --wait` succeeds, but `/health/ready` never answers | The API refused its configuration and restarts | `docker compose -f infrastructure/docker-compose.yml --profile full logs api` shows a `config.invalid` event per key with its rule; fix the value in `infrastructure/.env`, usually by running `create-dev-env.mjs` without flags |
| `migrate` exits with a non-zero code, and `api` does not start | The bundle could not apply the migrations, for example after a password change | `docker compose -f infrastructure/docker-compose.yml --profile full logs migrate`. For `28P01 password authentication failed`, the volume was bootstrapped with other role passwords: run `down -v`, then `up` again |
| "Pool overlaps with other one on this address space" | The subnet is already used by another Docker network | Set `NETWORK_SUBNET`, its upper half as `NETWORK_IP_RANGE`, and a `WEB_IPV4_ADDRESS` inside the subnet with the host part `.10`, in `infrastructure/.env` |
| "invalid ip-range …: parent subnet … doesn't contain ip-range" | `NETWORK_SUBNET` changed, and `NETWORK_IP_RANGE` still holds the default range | Set `NETWORK_IP_RANGE` to the upper half of the new subnet |
| `up` fails for `web` with an invalid-address error | `WEB_IPV4_ADDRESS` lies outside `NETWORK_SUBNET` | Set both, with the address inside the subnet |
| `up` fails for `web` with "Address already in use" | Another container holds `WEB_IPV4_ADDRESS`, because `NETWORK_IP_RANGE` contains it | Set a range that leaves the address out, such as the subnet's upper half, then `down` and `up` again |
| No log files under Compose on a Linux host | The log folders are not writable by the container users | `sudo chown 1654:1654 logs/api` and `sudo chown 1000:1000 logs/notifications` |
| "address already in use" on 5080, 5090, 5433 or 8081 | The same service runs on the host, or another stack holds the port | `lsof -nP -iTCP:5080 -sTCP:LISTEN` on macOS, or `Get-NetTCPConnection -LocalPort 5080` on Windows; stop the duplicate |
| Safari signs out on every reload at http://localhost:8081 | The Secure refresh cookie on plain http | Use Chrome, Firefox or Edge for Staging |
| The dashboard stays on "Reconnecting…", and the notification service logs `socket.rejected` | The dashboard was opened at an origin that `SOCKET_ALLOWED_ORIGINS` does not list, or the two services hold different ticket keys | Open http://localhost:8081 or http://127.0.0.1:8081; run `create-dev-env.mjs` without flags, then `up` again |
| `502 Bad Gateway` under `/api/` or `/socket.io/` | The API or the notification service is not running | `docker compose -f infrastructure/docker-compose.yml --profile full ps`; nginx reaches a restarted container again within 10 seconds |
