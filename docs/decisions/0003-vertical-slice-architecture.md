# 0003. Vertical slice architecture

## Status

Accepted, 2026-10-20.

## Context

The system's work is a set of use cases: create a job, assign it, update its status, save and submit an inspection, show the dashboard. In a layered design one such change spreads across controllers, services, repositories and mappers, and each layer grows abstractions that only one use case needs.

## Decision

Every application is organized by feature first, then by use case.

| Application | Layout |
|---|---|
| API | `Features/<Feature>/<Slice>/` holds the endpoint, the request record and, when the shape is unique to the slice, the response record. `Domain/` holds entities and the job transition table; `Persistence/` the database context, configurations and migrations; `Shared/` the cross-cutting code (security, errors, observability, real-time dispatch, HTTP, health, OpenAPI). |
| Dashboard | `src/features/<feature>/` holds pages, components, composables, one Pinia store, an API module and colocated tests. `src/shared/` holds the UI components and the API client; `src/app/` the entry point, router, styles and global error handling. |
| Technician app | One folder per feature, with each page next to its ViewModel; shared services and resources sit in shared folders. |
| Notification service | `src/features/<feature>/` (health, job events, sockets) and `src/shared/` for the HTTP and logging plumbing. |

- Tests mirror the slice paths, for example `tests/FieldService.Api.Tests/Features/Jobs/AssignJobTests.cs`.
- Shared code holds no business rules. Business rules live on the domain entities and in the slices that call them.

## Consequences

- A change to one use case usually touches one folder and its tests.
- Removing a slice removes one registration line in its feature's endpoint mapper.
- Some duplication between slices is accepted instead of early abstractions.
- Cross-cutting concerns stay in `Shared/` and are reviewed for business logic creeping in.
