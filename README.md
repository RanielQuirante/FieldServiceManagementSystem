# FieldService

FieldService is a field service management system. Administrators create service jobs, assign them to technicians and follow their progress on a web dashboard. Technicians see their jobs on a mobile app, update each job's status as they work, and complete a checklist inspection on site. Changes reach the dashboard in real time.

**Status:** the four applications are scaffolded with logging, error handling, health checks, test suites, the database schema and the local tooling. The features described above are being built on this foundation.

## Applications

| Application | Folder | Technology | Role |
|---|---|---|---|
| API | `src/FieldService.Api` | ASP.NET Core 10 Minimal APIs, Entity Framework Core 10 | Jobs, assignments, status changes, inspections, authentication and authorization; sends job events to the notification service |
| Technician app | `src/FieldService.Mobile` | .NET MAUI 10, Android and Windows | Assigned jobs, status updates and inspections in the field |
| Administrator dashboard | `src/fieldservice-web` | Vue 3, TypeScript, Vite | Job creation and assignment, job list and details, dashboard, technicians |
| Notification service | `src/fieldservice-notifications` | Node.js, TypeScript, Socket.IO | Verifies signed job events from the API and broadcasts them to the dashboard |
| Database | `infrastructure/postgres` | PostgreSQL 17 | The API's data, with separate roles for migrations and for the running API |

Each application has its own test suite: `tests/FieldService.Api.Tests`, `tests/FieldService.Mobile.Tests` and `tests/notification-tests` for the API, the technician app and the notification service, and colocated unit tests plus Playwright end-to-end tests for the dashboard.

## Stack

| Area | Versions |
|---|---|
| .NET | SDK 10.0.402; ASP.NET Core and Entity Framework Core 10.0.13; Npgsql 10.0.3; Serilog 4.4; xUnit v3 |
| Mobile | .NET MAUI 10.0.110; CommunityToolkit.Mvvm 8.4.2 |
| Web | Node 26.12.0; TypeScript 6.0; Vue 3.5; Vite 8; Pinia; Vue Router; Vitest; Playwright |
| Notifications | Node 26.12.0; TypeScript 6.0; Socket.IO 4.8; pino 10; zod |
| Database | PostgreSQL 17.11 |

## Architecture in brief

- Every application is organized by feature and use case ([ADR 0003](docs/decisions/0003-vertical-slice-architecture.md)); the API uses Minimal API slices without a mediator ([ADR 0004](docs/decisions/0004-minimal-api-slices-without-mediator.md)).
- The REST contract is the API's committed OpenAPI 3.1 document, `docs/contracts/openapi.v1.json`, and both clients are tested against it ([ADR 0011](docs/decisions/0011-openapi-snapshot-as-the-rest-contract.md)).
- The API connects to PostgreSQL with a role that cannot change the schema; migrations run with a separate role ([ADR 0005](docs/decisions/0005-postgresql-17-with-two-database-roles.md)).
- Job updates are protected by optimistic concurrency ([ADR 0006](docs/decisions/0006-optimistic-concurrency-with-xmin.md)).
- Each application writes structured log files with bounded retention, and one correlation id links a request across the applications ([ADR 0012](docs/decisions/0012-log-file-naming-and-retention.md)).

## Getting started

[docs/development.md](docs/development.md) describes the prerequisites, the one-time creation of local secrets, the database setup and how to run the four applications on macOS and Windows.

## Documentation

The [documentation index](docs/README.md) lists every document, and the [decision records](docs/decisions/README.md) explain the main design choices.
