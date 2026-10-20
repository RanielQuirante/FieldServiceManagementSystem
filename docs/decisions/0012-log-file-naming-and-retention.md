# 0012. Log file naming and retention

## Status

Accepted, 2026-10-20.

## Context

Each application writes file logs, named by date and kept for a bounded time. The logging libraries name rolled files differently: Serilog inserts the date before the extension, while pino-roll appends a date and a sequence number. Logs must be searchable, must never hold secrets or personal data, and must never be committed.

## Decision

| Application | File | Format | Rolling and retention |
|---|---|---|---|
| API | `logs/api/api-YYYYMMDD.log` | Compact log event format (CLEF), one JSON object per line | Daily; 14 files; each capped at 50 MB and rolled on size |
| Notification service | `logs/notifications/notifications.YYYYMMDD.N.log` | pino JSON lines | Daily; 14 files; each capped at 50 MB; older files removed |
| Technician app | `<app data directory>/logs/mobile-YYYYMMDD.log` | CLEF | Daily; 7 files of 5 MB |
| Dashboard | none | — | Client errors are reported to the API, which logs them |

- Each application writes to its own folder, so one library's retention never deletes another's files.
- The API's directory comes from configuration (`Logs:Directory`) and resolves against the content root: `../../logs/api` on a developer machine, `/app/logs` in a container with a bind mount from the host's `logs/api`.
- Levels come from configuration per environment; request logs record the method, the path without its query string, the status, the elapsed time and the correlation id.
- Every application logs identifiers only: never passwords, tokens, tickets, signatures, Authorization or Cookie headers, bodies, query strings, emails, names, phone numbers or addresses.
- A correlation id, accepted from `X-Correlation-ID` when it is well-formed and otherwise taken from the W3C trace id, links a client action, the API request, the event dispatch and the notification broadcast.
- Only `logs/.gitkeep` is tracked; `logs/*` and `*.log` are ignored.

## Consequences

- Every file is plain JSON lines and can be searched with standard tools.
- The API and notification file names differ slightly, because pino-roll adds a sequence number, but they share the date stamp, the retention and the folder layout.
- Disk use is bounded by configuration, not by manual clean-up.
- Tests prove the redaction rules by scanning captured log events for sentinel secrets.
