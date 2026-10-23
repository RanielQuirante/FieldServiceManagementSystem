# 0010. Refetch on event and reconnect

## Status

Accepted, 2026-10-23.

## Context

The dashboard shows jobs, their statuses and status counts that change while an administrator watches. Real-time events tell it that something changed, but events can be lost (a full queue, an API restart, the notification service or the socket being down), delivered twice (retries) or arrive out of order. The dashboard must still show the API's current data after any of these, and a faulty or compromised relay must not be able to change what the dashboard shows or push it past the API's rate limits.

## Decision

- **Events are hints.** An event payload carries only `eventId`, `jobId`, `jobNumber`, `status`, `previousStatus` and `occurredAt`. The dashboard never patches a cached row from it; it asks the API.
- **On each event** the dashboard:
  1. drops a payload that fails its type guard, and an event id among the last 200 it handled;
  2. refetches the affected job, with at most one request in flight and one per second for each job, and uses the job's latest history entry for the toast and the row flash; a 404 is ignored;
  3. refetches the visible lists, the job list with its status counts, debounced by 300 ms;
  4. stops per-event requests above 60 a minute and instead runs one full refetch of the open page, at most once a minute.
- **On every connect**, the first one included, the dashboard refetches everything the open page shows. Socket.IO's connection-state recovery stays off, because it is best effort and skips the handshake checks.
- **Reconnection.** After a network loss or a service restart, Socket.IO reconnects by itself, 1 to 5 seconds apart with randomization 0.5. After a refused handshake or a failed ticket request, the dashboard retries with its own backoff, 1 second doubling to 30 seconds with ±25 % jitter; `ticket_invalid` and `forbidden` first refresh the session once. After the session cap's disconnect, it connects again while the user is signed in, and on sign-out it disconnects.
- **Visible states.** The top bar shows a Connecting, Live, Reconnecting or Paused pill. The Reconnecting banner appears after 3 seconds without a connection, "Live updates are paused" after 30 seconds with a Refresh now button, and "Back online. Jobs refreshed at …" for 8 seconds after a reconnect that followed a shown banner. The connection states and toasts are described in [ui.md](../ui.md#live-updates).

Rejected alternatives: patching rows from event data, which needs version bookkeeping and still misses changes to list membership; connection-state recovery; per-job rooms; and live updates in the technician app for this release, which would need a .NET Socket.IO client and background connection handling. The technician app reloads when a page appears and on pull to refresh, and its version check catches a stale job on every command.

## Consequences

- A missed, duplicated or reordered event cannot leave wrong data on screen, and no client-side version tracking is needed.
- Each event costs one or two API reads per open tab, bounded by the per-job and per-minute caps; a relay that floods events causes at most one full refetch a minute of real data.
- After an outage the dashboard catches up in one refetch, so the notification service never stores or replays job state.
- The technician app has no push channel; a technician sees a new assignment on the next refresh.
