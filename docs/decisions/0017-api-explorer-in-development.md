# 0017. API explorer in Development

## Status

Accepted, 2026-10-20.

## Context

A browsable API reference helps while developing the clients. The preferred option was Swagger UI, referenced by the API project only in Debug builds and served only in the Development environment, reading the built-in OpenAPI document, so that nothing of it would reach a Release build.

The repository restores .NET packages in locked mode, with one `packages.lock.json` per project, on developer machines, in the build pipeline and in container builds. A lock file records the direct references per target framework, not per build configuration. A package reference conditioned on `'$(Configuration)' == 'Debug'` therefore cannot pass a locked restore in both configurations: the Debug restore passes, and the Release restore fails with `NU1004` ("The package references have changed").

## Decision

- The API ships no API explorer interface. It references no Swagger UI package and maps no `/swagger` route.
- The OpenAPI 3.1 document stays available at `/openapi/v1.json` in the Development and Testing environments only. It is anonymous, outside rate limiting, and absent in Staging and Production.
- To browse the API, developers open that document in an OpenAPI viewer of their choice, outside the repository, or read `docs/api.md`.

### Alternatives considered

- **An asset exclusion instead of a conditional reference.** Referencing the package in every configuration while excluding its assets outside Debug passes both locked restores and keeps it out of Release output, but every Release restore, including the pipeline and container builds, still downloads the package. Not taken, to keep the dependency set and the reviewed surface small.
- **An unconditional reference.** It would ship the interface's static files in Release builds.

## Consequences

- The API has one fewer dependency and no interface-specific security settings.
- `/swagger` answers `404` in every environment.
- Contract tests check that the OpenAPI document is absent outside Development and Testing.
