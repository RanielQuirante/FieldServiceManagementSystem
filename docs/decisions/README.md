# Architecture decision records

Each record states one decision, why it was made and what follows from it. Records are numbered in the order they were taken and are never renumbered. A record that is replaced keeps its file, and its status names the record that supersedes it.

## Index

| Number | Decision | Status |
|---|---|---|
| [0001](0001-project-structure-and-count.md) | Project structure and count | Accepted |
| [0002](0002-solution-file-format-slnx.md) | Solution file format: slnx | Accepted |
| [0003](0003-vertical-slice-architecture.md) | Vertical slice architecture | Accepted |
| [0004](0004-minimal-api-slices-without-mediator.md) | Minimal API slices without a mediator | Accepted |
| [0005](0005-postgresql-17-with-two-database-roles.md) | PostgreSQL 17 with two database roles | Accepted |
| [0006](0006-optimistic-concurrency-with-xmin.md) | Optimistic concurrency with xmin | Accepted |
| [0007](0007-token-authentication-with-rotating-refresh-tokens.md) | Token authentication with rotating refresh tokens | Accepted |
| [0011](0011-openapi-snapshot-as-the-rest-contract.md) | OpenAPI snapshot as the REST contract | Accepted |
| [0012](0012-log-file-naming-and-retention.md) | Log file naming and retention | Accepted |
| [0013](0013-native-vue-components-and-design-tokens.md) | Native Vue components and design tokens | Accepted |
| [0014](0014-comment-free-source-files.md) | Comment-free source files | Accepted |
| [0015](0015-maui-net10-test-target.md) | Technician app tests on a plain net10.0 target | Accepted |
| [0016](0016-phased-docker-compose.md) | Phased Docker Compose | Accepted |
| [0017](0017-api-explorer-in-development.md) | API explorer in Development | Accepted |

## Template

```markdown
# NNNN. Title in sentence case

## Status

Proposed | Accepted, YYYY-MM-DD | Superseded by NNNN

## Context

The forces at play: the problem, the constraints and what was known when the decision was made.

## Decision

What was decided, stated in the active voice, with the names of files, settings and versions where they matter.

## Consequences

What becomes easier, what becomes harder, and what must now be done or checked.
```
