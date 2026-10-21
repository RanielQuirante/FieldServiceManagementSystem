# Contracts

This folder holds the contracts between the FieldService applications. Each contract is a committed file that every party tests against, so a change on one side fails a test on the other before it reaches a running system.

| Contract | Between | Guarded by |
|---|---|---|
| `openapi.v1.json` | API and both clients | The API's snapshot test, the dashboard's type-check and the technician app's drift tests |
| Event envelopes and client payloads, with their signature and socket-ticket test vectors | API, notification service and dashboard | Fixture and vector tests in each of the three applications |

The decision behind this approach is [ADR 0011](../decisions/0011-openapi-snapshot-as-the-rest-contract.md).

## REST contract

### What the snapshot is

`openapi.v1.json` is the OpenAPI 3.1 document of the API, produced by Microsoft.AspNetCore.OpenApi from endpoint metadata and typed results. Nobody edits it by hand. A test writes it.

### Changing it

1. Change the API: endpoints, request or response records, policies or problem types.
2. Run the snapshot test with `FIELDSERVICE_UPDATE_CONTRACTS=1` set. It rewrites `openapi.v1.json`. With `CI` set, the test refuses to rewrite and fails instead.
3. Review the diff of `openapi.v1.json` together with the code change. The contract change is approved before either client is changed to use it.
4. Update the clients: the dashboard regenerates its types with `npm run generate:api` in `src/fieldservice-web`, and the technician app updates its DTO records until its drift tests pass.
5. Run the snapshot test again without the variable. It must pass with no difference.

### Rules

- **Additive only under `/api`.** New endpoints, new optional request members, new response members and new problem codes are allowed. Removing or renaming a member, changing its type or meaning, tightening validation on an existing member, or removing an endpoint is a breaking change.
- **Breaking changes** need a new architecture decision record and a parallel `/api/v2` route group. The `/api` group keeps its behaviour until no client uses it.
- **Unique type names.** Every request and response record has a name that is unique across the API, because the names become schema ids in the document and type names in the generated client.
- **Errors** are Problem Details (`application/problem+json`) with the extension members `code`, `correlationId` and `traceId`.
- **Enums** travel as strings. Clients treat an unknown value safely: the technician app maps it to an `Unknown` member.
- **Date-time values** travel as UTC strings with millisecond precision and a `Z` suffix, for example `2026-10-20T09:12:00.000Z`.

### Date-time schema transformer

The API writes UTC instants through a custom JSON converter, which fixes the wire format above. With the converter in place, the generated document described date-time members with `format: date-time` but no `type`, and the dashboard's type generator turned them into `unknown`. A narrow schema transformer in `src/FieldService.Api/Shared/OpenApi` therefore declares:

- `DateTimeOffset` as `type: string`, `format: date-time`;
- a nullable `DateTimeOffset` as `type: ["null", "string"]`, `format: date-time`.

It changes only the document, never the wire format. A contract test fails if any schema in the snapshot has a `format` without a `type`.

### Nullability schema transformer

The first snapshot with endpoints showed two nullability gaps in the generated document:

- A required request member of a nullable value type, such as `Guid?`, `DateTimeOffset?` or a nullable enum, was documented as nullable. These members are nullable in C# only so that a missing member becomes a field error; the contract requires a value.
- An enumeration schema first built from a nullable member listed `null` among its values. Every non-nullable use of `JobStatus`, `JobPriority` or `SessionClient` would then have read as nullable.

A second narrow schema transformer in `src/FieldService.Api/Shared/OpenApi` closes both gaps. It removes `null` from every enumeration schema, so a nullable enum member is expressed as `oneOf` with `null` at the member, and it makes required members of nullable value types non-nullable. It changes only the document, never validation or the wire format. The snapshot test shows any change in its output after a package update.

## Event contracts

The API sends job events to the notification service as signed webhooks, and the notification service forwards client payloads to the dashboard over Socket.IO.

- **Envelopes carry a `version`.** A breaking change ships as a new version or a new event type, in parallel with the old one. The notification service acknowledges an unknown type or version with a warning and does not broadcast it.
- **Fixtures** hold one envelope per event type and version, and one client payload per event type. The API's serializer must reproduce the envelopes exactly for fixed inputs, the notification service must accept them and reject mutated copies, and the dashboard's type guard must accept the client payloads.
- **Signatures** follow Standard Webhooks. One signature test vector holds an id, a timestamp, a body, a secret and the expected signature; the API's signer and the notification service's verifier must both match it.
- **Socket tickets** are short-lived signed tokens. One ticket test vector holds a key, fixed claims and the expected token; the API's issuer and the notification service's verifier must both agree with it.

## Published test values

The signature and socket-ticket test vectors contain a webhook secret and a signing key that are published on purpose, so that every implementation can be checked against the same bytes.

**These values must never be configured in any environment.** The API and the notification service refuse to start when a configured secret or key equals a published test value, and the repository's tracked-file check allows these values only inside the two vector files.
