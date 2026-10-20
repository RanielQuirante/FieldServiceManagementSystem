# 0001. Project structure and count

## Status

Accepted, 2026-10-20.

## Context

FieldService has four applications: an ASP.NET Core API, a .NET MAUI technician app for Android and Windows, a Vue 3 administrator dashboard and a Node.js real-time notification service, with PostgreSQL behind the API. Each application needs its own automated tests. The repository should stay easy to read: someone opening it should find an application and its tests without first learning a set of shared libraries.

The technician app's ViewModels need unit tests that run without a device or an emulator, on macOS and on Linux build runners.

## Decision

- The repository holds exactly seven projects:

  | Project | Purpose |
  |---|---|
  | `src/FieldService.Api` | REST API, persistence and event dispatch |
  | `src/FieldService.Mobile` | Technician app for Android and Windows |
  | `src/fieldservice-web` | Administrator dashboard |
  | `src/fieldservice-notifications` | Real-time notification service |
  | `tests/FieldService.Api.Tests` | API unit, integration, contract and security tests |
  | `tests/FieldService.Mobile.Tests` | Technician app ViewModel and contract tests |
  | `tests/notification-tests` | Notification service unit, integration and contract tests |

- There are no shared libraries and no shared contracts assembly. Each client depends on the API's published contract (ADR 0011), not on the API's types.
- The dashboard keeps its unit tests next to the code and its end-to-end tests in `src/fieldservice-web/e2e`.
- `tests/notification-tests` is a private npm package that imports the service's source by relative path.
- The technician app adds a plain `net10.0` target beside its platform targets, and its test project references that target (ADR 0015). A separate core library for the app was considered and rejected: it would split every feature across two projects only to avoid installing the MAUI workload, which building the app needs anyway.

## Consequences

- Each application builds and tests on its own. A checkout without the MAUI workload still builds and tests the API, the dashboard and the notification service, project by project.
- Each technician app page stays next to its ViewModel in one project.
- Types that cross the wire are written once per client and kept in step by contract tests instead of a shared assembly.
- Adding an eighth project is a deliberate change that needs a new decision record.
