# 0006. Optimistic concurrency with xmin

## Status

Accepted, 2026-10-20.

## Context

An administrator and a technician can change the same job from different devices: the administrator reassigns or cancels it while the technician updates its status or saves its inspection. Two kinds of conflict matter: two requests racing on the server, and a lost update when someone saves changes made against a page loaded minutes earlier.

## Decision

- PostgreSQL's system column `xmin` on `jobs` is mapped as a `uint Version` property with `IsRowVersion()`. No extra column is maintained by hand.
- Job responses expose it as `version`.
- Every command that changes a job sends the `version` it last read. The handler sets it as the original value before saving, so a stale write fails.
- A stale write returns `409 Conflict` as Problem Details with the code `concurrency.conflict`. A unique-constraint violation or a lock timeout maps to the same answer.
- Saving an inspection also updates the job's `updated_at`, so the job's version covers its inspection. Two devices saving the same checklist conflict as expected.

## Consequences

- One mechanism covers both in-request races and lost updates over think-time.
- Clients handle `409` by reloading the job and showing the current state before the user retries.
- `xmin` changes on every update of the row, including updates made by the database itself, so any server-side change also invalidates older versions.
- `xmin` is specific to PostgreSQL, which is the only supported database.
