# 0016. Phased Docker Compose

## Status

Accepted, 2026-10-20.

## Context

Most features can be developed with the applications running directly on the developer machine against a local PostgreSQL. A complete container setup is still needed for production-like end-to-end tests and as a deployment reference, and the Compose file should be validated from the first build rather than written at the end.

## Decision

- `infrastructure/docker-compose.yml`, project name `fieldservice`, starts with one service:
  - `db`: the `postgres:17.11-trixie` image on `127.0.0.1:${DB_HOST_PORT:-5433}:5432`, a named volume, data checksums, the database bootstrap script mounted into the image's init folder, and a health check.
- The application services join later, behind profiles:
  - `migrate`, `api` and `notifications` with the profiles `backend` and `full`;
  - `web`, served by an unprivileged nginx image on `127.0.0.1:8081`, with the profile `full`.
- Values come from an untracked `infrastructure/.env`, which `infrastructure/scripts/create-dev-env.mjs` creates; every secret is marked required with `${VAR:?}`, so Compose refuses to start without it.
- Developers use either a local PostgreSQL 17 on port 5432 or the Compose `db` on port 5433.
- Application ports are the same on the host and in Compose (5080 for the API, 5090 for notifications, 5173 for the dashboard on the host), so one or the other runs at a time.

## Consequences

- Local development needs no container runtime.
- The build pipeline starts the real Compose database from the first commit, which also validates the file.
- Full-stack containers and end-to-end runs against Compose arrive with the end-to-end test suite.
- Two database options must be documented in the development guide.
