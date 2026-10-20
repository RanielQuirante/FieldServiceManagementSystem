# 0005. PostgreSQL 17 with two database roles

## Status

Accepted, 2026-10-20.

## Context

The API needs a relational database with strong constraints, transactional updates and good .NET support. The running API should be unable to change the schema or rewrite its own audit history, and permission mistakes should show up in tests rather than in production. The same database version should run on developer machines, in the build pipeline and in containers.

## Decision

- **PostgreSQL 17.11 everywhere:** a local installation on `127.0.0.1:5432`, or the Compose `db` service (`postgres:17.11-trixie`) on `127.0.0.1:5433`, and the same image in the build pipeline. Entity Framework Core 10 with the Npgsql provider targets version 17, with snake_case names and the schema `fieldservice`.
- **Two roles, in every environment:**

  | Role | Rights |
  |---|---|
  | `fieldservice_migrator` | Owns the database, the `fieldservice` schema and every object in it. Applies migrations. No superuser, database-creation or role-creation rights. |
  | `fieldservice_app` | The role the API connects as. Uses the schema and has data rights through default privileges, with statement, lock and idle-transaction timeouts. No rights on the job-number sequence. |

- `infrastructure/postgres/bootstrap.sql` creates both roles, the database and the schema. It is idempotent, reads the role passwords from the environment, and revokes `CONNECT` from `PUBLIC`. The same file runs from the Compose image's init hook, from `infrastructure/scripts/create-dev-env.mjs` against a local server, and from the API test fixture.
- **The API never migrates.** Migrations run as `fieldservice_migrator`: `dotnet ef database update` on a developer machine, a self-contained migration bundle in containers. In Development and Staging the API refuses to start while a migration is pending, and names it.
- **Rights the app role does not get,** set by the single migration `InitialSchema`:
  - no insert, update or delete on the checklist reference tables or on the migration history table;
  - no update or delete on `job_history`, so the audit trail is append-only.
- **Job numbers.** Jobs carry a human-readable number from an identity column starting at 1001. Instead of giving the app role rights on the sequence, a migrator-owned `SECURITY DEFINER` function, `fieldservice.advance_job_number()`, can only move the identity past numbers that already exist, never backwards. The app role may execute it; `PUBLIC` may not.
- **Submitted inspections are locked in the database.** A `SECURITY INVOKER` trigger function on `inspections`, `inspection_answers` and `inspection_readings` raises SQLSTATE `55000` on any update or delete of a submitted inspection's rows unless the caller is a member of `fieldservice_migrator`.
- Connection strings come only from configuration: user secrets in Development, environment variables elsewhere.

## Consequences

- A defect in the API cannot drop a table, alter the schema, rewrite history or move the job-number sequence backwards.
- Integration tests run as the app role against a fresh database per run, so a missing or excessive grant fails a test.
- Every environment needs two connection strings, which the local setup script and the Compose configuration provide.
- Each migration's hand-written SQL is reviewed for its grants and functions before it is applied.
