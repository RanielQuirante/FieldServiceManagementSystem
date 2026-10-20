# 0011. OpenAPI snapshot as the REST contract

## Status

Accepted, 2026-10-20.

## Context

Two clients consume the REST API: the dashboard, written in TypeScript, and the technician app, written in C#. Both must notice when the API changes shape. Committing generated client code would put generator headers and comments into the repository, and a shared C# assembly would do nothing for TypeScript.

## Decision

- **The contract.** The API's OpenAPI 3.1 document, produced by Microsoft.AspNetCore.OpenApi from endpoint metadata and `TypedResults`, is committed as `docs/contracts/openapi.v1.json`.
- **The guard.** A snapshot test starts the API in the Testing environment, compares the document with the committed file as normalized text and fails on any difference. Setting `FIELDSERVICE_UPDATE_CONTRACTS=1` rewrites the file locally; the test refuses to rewrite it when `CI` is set.
- **Review.** Every change to the snapshot is a reviewed diff, approved before either client changes. REST changes under `/api` are additive; a breaking change needs a new decision record and a parallel `/api/v2` route group.
- **Dashboard.** openapi-typescript generates `src/shared/api/schema.gen.ts` from the committed snapshot, and openapi-fetch consumes the generated types. The generated file is ignored by git. An npm `overrides` entry aligns the generator's TypeScript peer with the project's TypeScript 6.
- **Technician app.** Hand-written DTO records, read through a source-generated `JsonSerializerContext`; drift tests compare property names, JSON types, formats, enum values and nullability with the snapshot.
- **No build-time generation.** Producing the document during the build would run the API's startup, with its configuration validation, on every build.
- **Date-time schemas.** The API writes UTC instants with a fixed `…000Z` format through a custom JSON converter. Without help, the document then describes those members with a `format` but no `type`, and the generator emits `unknown`. A narrow schema transformer declares `DateTimeOffset` as `type: string, format: date-time`, and a nullable one as `type: ["null", "string"]`. The wire format does not change. A contract test fails if any schema in the snapshot has a `format` without a `type`.

### Evidence

An early spike ran openapi-typescript 7.13.0 on TypeScript 6.0.3 with vue-tsc 3.3.13 against a document from Microsoft.AspNetCore.OpenApi 10.0.13 (OpenAPI 3.1.1). Types were produced deterministically; a typed openapi-fetch call passed `vue-tsc --build`; renaming a response property in the document made type-checking fail; nullable members became `T | null`; enums became string-literal unions; and, with the schema transformer, date-time members stayed `string`. ESLint and Prettier both ignore the generated file.

## Consequences

- A breaking API change fails the dashboard's type-check and the technician app's drift tests before it reaches a running app.
- Generated code never enters the repository.
- The snapshot diff is the place to review every contract change.
- If the generator stopped working with a future TypeScript release, the fallback is hand-written per-feature types over a small fetch wrapper, with drift caught by the snapshot review and the end-to-end tests.
