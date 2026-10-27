# Architecture

FieldService is a field service management system made of four applications and one database:

- **FieldService.Api**, an ASP.NET Core 10 API, owns every business rule and the only database connection.
- **FieldService.Web**, a Vue 3 dashboard for administrators (shown as dispatchers in the interface).
- **FieldService.Mobile**, a .NET MAUI app for technicians on Android and Windows.
- **FieldService.Notifications**, a Node.js service that relays job changes to the dashboard over Socket.IO.
- **PostgreSQL 17**, which only the API and its migrations reach.

This document covers the system and its containers, the trust zones, what each application is responsible for, the patterns used and why, and how the applications communicate in the main request flows. The security controls on each connection are in [security.md](security.md), the wire formats in [contracts/README.md](contracts/README.md), and the settings in [configuration.md](configuration.md).

## Contents

- [System](#system)
- [Containers](#containers)
- [Trust zones](#trust-zones)
- [Responsibilities](#responsibilities)
- [Patterns and why](#patterns-and-why)
- [Communication](#communication)
  - [Channels](#channels)
  - [Rules common to every flow](#rules-common-to-every-flow)
  - [Create a job](#create-a-job)
  - [Assign a job](#assign-a-job)
  - [Technician status update](#technician-status-update)
  - [Inspection save and submit](#inspection-save-and-submit)
  - [Cancel a job](#cancel-a-job)
  - [Dashboard summary](#dashboard-summary)
  - [Client error report](#client-error-report)
  - [Real-time broadcast](#real-time-broadcast)
  - [Reconnect and refetch](#reconnect-and-refetch)

## System

```mermaid
flowchart LR
    Admin([Administrator]) -->|browser| Web["Dashboard<br/>Vue 3"]
    Tech([Technician]) -->|Android or Windows| Mobile["Technician app<br/>.NET MAUI"]
    Web -->|REST, refresh cookie| Api["API<br/>ASP.NET Core"]
    Mobile -->|REST, bearer token| Api
    Api -->|SQL| Db[("PostgreSQL 17")]
    Api -->|signed job events| Notif["Notification service<br/>Node.js"]
    Web <-->|Socket.IO with a ticket| Notif
```

The API is the only writer and the only source of truth. The notification service carries change notices, never data: a client that hears of a change fetches it from the API.

## Containers

```mermaid
flowchart LR
    Browser([Browser]) -->|":8081"| WebC["web<br/>nginx: dashboard files,<br/>/api/ and /socket.io/ proxy"]
    WebC -->|/api/| ApiC["api<br/>:8080"]
    WebC -->|/socket.io/| NotifC["notifications<br/>:5090"]
    ApiC -->|signed events| NotifC
    ApiC --> DbC[("db<br/>PostgreSQL :5432")]
    MigrateC["migrate<br/>runs once"] --> DbC
    Device([Technician app]) -->|":5080"| ApiC
```

Docker Compose runs the database alone by default, the back end with the `backend` profile, and everything with `full`. Every published port listens on `127.0.0.1` only: 8081 for the dashboard, 5080 for the API, 5090 for the notification service and 5433 for the database. The containers run as non-root users on read-only file systems with every capability dropped ([deployment.md](deployment.md)).

## Trust zones

```mermaid
flowchart LR
    subgraph Clients["Untrusted clients"]
        Browser2([Dashboard in a browser])
        Device2([Technician app])
    end
    subgraph Edge["Edge"]
        Proxy["TLS proxy and nginx"]
    end
    subgraph Services["Services"]
        Api2[API]
        Notif2[Notification service]
    end
    subgraph Data["Data"]
        Db2[(PostgreSQL)]
    end
    Browser2 --> Proxy
    Device2 --> Proxy
    Proxy --> Api2
    Proxy --> Notif2
    Api2 -->|"signed, internal network only"| Notif2
    Api2 -->|"least-privilege role"| Db2
```

Each crossing and its controls are listed in [security.md](security.md#trust-boundaries).

## Responsibilities

| Application | Owns | Does not own | Interfaces | Built with |
|---|---|---|---|---|
| API | Every business rule; jobs, assignments, status changes, inspections and their history; sign-in, sessions and roles; the database and its migrations; issuing socket tickets; sending job events | Real-time delivery; any user interface | REST, described by `contracts/openapi.v1.json`; signed webhooks to the notification service; SQL | ASP.NET Core Minimal APIs for the endpoints, Entity Framework Core with Npgsql for data, Serilog for logs |
| Dashboard | The administrators' screens; the session in the browser; live states and refetching after events | Business rules: every change is the API's decision | REST with types generated from the contract; Socket.IO | Vue 3, Pinia for state, Vue Router, Vite, plain CSS custom properties |
| Technician app | The technicians' screens on Android and Windows; the session in secure storage; reading loaded jobs offline; checklist answers until they are saved | Business rules and real-time updates | REST through typed clients | .NET MAUI with XAML, CommunityToolkit.Mvvm for view models, Serilog for logs |
| Notification service | Verifying signed job events, dropping duplicates, broadcasting to signed-in administrators, verifying socket tickets | Data: it has no database and sends only ids and statuses | An internal webhook endpoint; Socket.IO | Node.js with `node:http`, Socket.IO, zod for configuration, pino for logs |
| PostgreSQL | Durable data, constraints and the concurrency token | Business logic | SQL for the API's role and the migrator role | PostgreSQL 17 |

## Patterns and why

- **Vertical slices.** Each use case keeps its endpoint, validation, data access and response together, so a change touches one folder ([ADR 0003](decisions/0003-vertical-slice-architecture.md)). The API maps each slice as a Minimal API endpoint without a mediator, because a direct call is easier to read and test ([ADR 0004](decisions/0004-minimal-api-slices-without-mediator.md)).
- **MVVM in the technician app.** XAML pages bind to view models built with CommunityToolkit.Mvvm, so screen logic is tested without a device and pages hold no logic in code-behind.
- **Constructor injection, one composition root per application.** Every dependency arrives through a constructor and is registered in one place per application, so each can be replaced in tests.
- **Options with validation at start-up.** Settings bind to typed options validated when the application starts, so a missing or weak secret stops the process instead of failing later ([configuration.md](configuration.md)).
- **Problem Details.** Every API error is an RFC 9457 Problem Details response with a stable error code and a correlation id, so both clients handle errors the same way ([api.md](api.md)).
- **Optimistic concurrency.** Writes carry PostgreSQL's `xmin` as a concurrency token, so two people changing the same job never silently overwrite each other ([ADR 0006](decisions/0006-optimistic-concurrency-with-xmin.md)).
- **Refetch on every event and reconnect.** Clients treat an event as a hint and fetch the current state from the API, so a missed or repeated event never leaves wrong data on screen ([ADR 0010](decisions/0010-refetch-on-event-and-reconnect.md)).

## Communication

### Channels

```mermaid
flowchart LR
    Browser["Browser<br/>FieldService.Web"]
    Mobile["FieldService.Mobile<br/>Android or Windows"]
    Proxy["Same-origin proxy<br/>Vite dev server or vite preview<br/>forwards /api and /socket.io only"]
    Api["FieldService.Api<br/>port 5080"]
    Notif["FieldService.Notifications<br/>port 5090"]
    Db[("PostgreSQL 17<br/>port 5432")]
    Browser -->|"HTTPS in production<br/>REST and WebSocket"| Proxy
    Proxy -->|"/api, REST/JSON<br/>Bearer access token"| Api
    Proxy -->|"/socket.io, Socket.IO over WebSocket<br/>socket ticket in the handshake"| Notif
    Mobile -->|"REST/JSON<br/>Bearer access token"| Api
    Api -->|"POST /internal/job-events<br/>Standard Webhooks signature"| Notif
    Api -->|"Npgsql and EF Core<br/>fieldservice_app role"| Db
```

| From | To | Protocol | Authentication | Development address | Settings |
|---|---|---|---|---|---|
| Browser | Dashboard | HTTP | None | `http://localhost:5173` (Vite) | None |
| Dashboard | API | REST/JSON on the same origin, under `/api`, through the dev server's proxy | Bearer access token; on `/api/auth` only, the `fs_refresh` cookie with the header `X-FieldService-Csrf: 1` | Proxied to `http://127.0.0.1:5080` | `API_PROXY_TARGET` |
| Dashboard | Notification service | Socket.IO 4 over WebSocket only, under `/socket.io`, through the same proxy | A 60-second, single-use socket ticket in the handshake's `auth` member, fetched from the API for every handshake | Proxied to `http://127.0.0.1:5090` | `REALTIME_PROXY_TARGET`; the service's `SOCKET_ALLOWED_ORIGINS` and `SOCKET_TICKET_KEY` |
| Technician app | API | REST/JSON | Bearer access token; the refresh token in the body of the refresh and sign-out requests | `http://10.0.2.2:5080` from the Android emulator, `http://localhost:5080` on Windows; Release builds require `https` | `ApiBaseUrl` |
| API | Database | PostgreSQL protocol through Npgsql and EF Core | The `fieldservice_app` role, data changes only | `127.0.0.1:5432` | `ConnectionStrings:FieldService` |
| Migrations | Database | PostgreSQL protocol through `dotnet ef` | The `fieldservice_migrator` role, which owns the schema | `127.0.0.1:5432` | `ConnectionStrings:Migrations` |
| API | Notification service | HTTP `POST /internal/job-events` | Standard Webhooks HMAC-SHA256 signature | `http://127.0.0.1:5090` | `Notifications:BaseUrl` and `Notifications:WebhookSecret`; the service's `WEBHOOK_SECRETS` |

The `/internal` path and the health endpoints are never proxied to browsers. The technician app has no real-time channel: it reloads a list when its page appears and on pull to refresh, and the version check on every command catches a job that changed in the meantime.

### Rules common to every flow

- Every client request carries a new `X-Correlation-ID`. The API validates it, returns it, writes it to its logs and puts it in the job event, so one id links a user action, the API request, the signed event and the broadcast ([logging.md](logging.md)).
- Every error response from the API is Problem Details with a stable `code`, the correlation id and a trace id ([api.md](api.md)).
- Every job command carries the `version` the client last read. A stale version gets `409 concurrency.conflict`, and the client reloads the job. Repeating a command whose result already holds returns `200` with no new history entry and no event.
- Job events are queued only after the change commits, and they are hints: receivers refetch from the API and never copy data from an event ([ADR 0010](decisions/0010-refetch-on-event-and-reconnect.md)).

### Create a job

```mermaid
sequenceDiagram
    autonumber
    actor Admin as Administrator
    participant Web as Dashboard, creating tab
    participant Api as API
    participant Db as PostgreSQL
    participant Notif as Notification service
    participant Dash as Dashboard, every connected tab
    Admin->>Web: Submit the New job form
    Web->>Api: POST /api/jobs with the Bearer token and a new X-Correlation-ID
    Note over Api: Authentication, the rate limit, the Administrator policy, then validation
    alt a field is invalid, or the template or technician is unknown
        Api-->>Web: 400 with errors keyed by field
    else the chosen technician is inactive or off shift
        Api-->>Web: 409 technician.unavailable
    else accepted
        Api->>Db: One transaction: the job with the next number, its Created entry, and Assigned when a technician was chosen
        Api->>Api: Queue job.created after the commit
        Api-->>Web: 201 with Location and the job's number, status and version
        Api-)Notif: Signed job.created, see Real-time broadcast
        Notif-)Dash: job.created with ids and status only
        Dash->>Api: GET /api/jobs/{jobId}, then a debounced refetch of the job list and its status counts
    end
```

### Assign a job

```mermaid
sequenceDiagram
    autonumber
    actor Admin as Administrator
    participant Web as Dashboard
    participant Api as API
    participant Db as PostgreSQL
    participant Notif as Notification service
    participant Dash as Dashboard, every connected tab
    participant App as Technician app
    Admin->>Web: Choose a technician on the job page
    Web->>Api: PUT /api/jobs/{jobId}/assignment with technicianId and version
    alt the job is already assigned to that technician
        Api-->>Web: 200 with the current job, no history entry, no event
    else the job cannot be assigned, or the technician is unavailable
        Api-->>Web: 409 job.not-assignable or technician.unavailable
    else accepted
        Api->>Db: Update the job where its version matches, and add the Assigned entry
        alt the version was stale
            Api-->>Web: 409 concurrency.conflict
            Web->>Api: GET /api/jobs/{jobId}
        else committed
            Api-->>Web: 200 with the new version
            Api-)Notif: Signed job.assigned
            Notif-)Dash: job.assigned
            Dash->>Api: Refetch the job and the visible lists
        end
    end
    Note over App: No push to the technician app
    App->>Api: GET /api/technicians/me/jobs?view=active when My jobs appears or on pull to refresh
```

### Technician status update

```mermaid
sequenceDiagram
    autonumber
    actor Tech as Technician
    participant App as Technician app
    participant Api as API
    participant Db as PostgreSQL
    participant Notif as Notification service
    participant Dash as Dashboard, every connected tab
    Tech->>App: Start job, Put on hold with a reason, or Resume
    App->>Api: PUT /api/jobs/{jobId}/status with status, note and version
    opt the access token has expired
        Api-->>App: 401
        App->>Api: One shared POST /api/auth/refresh, then the same request once more
    end
    alt the job is not the technician's
        Api-->>App: 404 job.not-found
    else the change is not allowed from the current status
        Api-->>App: 409 job.invalid-transition
    else accepted
        Api->>Db: Update the status where the version matches, and add Started, PutOnHold or Resumed
        alt the version was stale
            Api-->>App: 409 concurrency.conflict, and the app reloads the job
        else committed
            Api-->>App: 200 with the new version and the allowed next changes
            Api-)Notif: Signed job.status-changed
            Notif-)Dash: job.status-changed
            Dash->>Api: GET /api/jobs/{jobId} for the toast and the row flash
            Dash->>Api: Debounced refetch of the visible lists
        end
    end
```

### Inspection save and submit

```mermaid
sequenceDiagram
    autonumber
    actor Tech as Technician
    participant App as Technician app
    participant Api as API
    participant Db as PostgreSQL
    participant Notif as Notification service
    participant Web as Dashboard
    Note over App,Api: Every call checks its role policy and ownership, another technician's job is 404
    Tech->>App: Continue inspection
    App->>Api: GET /api/jobs/{jobId}/inspection
    Api-->>App: 200 with the template items, saved answers, progress and version, also before the first save
    Tech->>App: Answer Pass, Fail or N/A, enter readings and notes
    Note over App: Unsaved answers stay in memory and are saved on leaving and before Review and submit
    App->>Api: PUT /api/jobs/{jobId}/inspection with answers and version
    alt a field breaks a rule, for example a Fail without a note or a third decimal
        Api-->>App: 400 with errors keyed like answers[2].note
    else every answer equals the saved one
        Api-->>App: 200 with the current state, nothing written
    else the job is not In progress, or the inspection was submitted
        Api-->>App: 409 inspection.locked
    else accepted
        Api->>Db: One transaction: the inspection, its answers and readings, and the job's version
        alt the version was stale, for example a second device
            Api-->>App: 409 concurrency.conflict, and the app reloads
        else committed
            Api-->>App: 200 with the progress and the new version, no real-time event
        end
    end
    Tech->>App: Review and submit, add notes, confirm the site is safe
    App->>Api: POST /api/jobs/{jobId}/submission with technicianNotes, siteSafetyConfirmed and version
    alt the site is not confirmed safe
        Api-->>App: 400 keyed siteSafetyConfirmed
    else the job is already Completed
        Api-->>App: 200 with the current job, no event
    else the job is not In progress
        Api-->>App: 409 job.invalid-transition
    else an item is unanswered
        Api-->>App: 409 inspection.incomplete
    else complete
        Api->>Db: One transaction: the job Completed, the inspection submitted, the Completed history entry
        alt the version was stale, or a racing request locked the inspection first
            Api-->>App: 409 concurrency.conflict or inspection.locked
        else committed
            Api-->>App: 200, and the job and its inspection are read-only from now on
            Api-)Notif: Signed job.status-changed, In progress to Completed
            Notif-)Web: job.status-changed
            Web->>Api: GET /api/jobs/{jobId}, and GET /api/jobs/{jobId}/inspection when its report is open
        end
    end
```

Saves send no event, because only the technician sees a draft. Every save that writes also updates the job row, so the job's version guards the checklist too, and two devices saving the same checklist cannot both succeed. Once submitted, the inspection cannot change: the API refuses the write, and a database trigger refuses any update or delete of a submitted inspection's rows. The administrator's read-only report is described in [ui.md](ui.md#inspection-report).

### Cancel a job

```mermaid
sequenceDiagram
    autonumber
    actor Admin as Administrator
    participant Web as Dashboard
    participant Api as API
    participant Db as PostgreSQL
    participant Notif as Notification service
    participant Dash as Dashboard, every connected tab
    participant App as Technician app
    Admin->>Web: Cancel job on the job page, enter a reason and confirm
    Web->>Api: POST /api/jobs/{jobId}/cancellation with reason and version
    Note over Api: The Administrator policy runs before validation, so a technician gets 403 for any job
    alt the reason is missing, blank or longer than 500 characters
        Api-->>Web: 400 keyed reason
    else the job does not exist
        Api-->>Web: 404 job.not-found
    else the job is already Cancelled
        Api-->>Web: 200 with the current job, no history entry, no event
    else the job is Completed
        Api-->>Web: 409 job.invalid-transition
    else accepted
        Api->>Db: One batch: the Cancelled history entry with the reason as its note, and the job update where its version matches
        alt the version was stale, or the row was locked too long
            Api-->>Web: 409 concurrency.conflict, and the dialog reloads the job
            Note over Web: Still cancellable: the dialog stays open with the refreshed status.<br/>Otherwise it closes and says the job can no longer be cancelled
        else committed
            Api-->>Web: 200 with the read-only job, canCancel false
            Api-)Notif: Signed job.status-changed to Cancelled
            Notif-)Dash: job.status-changed
            Dash->>Api: Refetch the job and the visible lists
        end
    end
    Note over App: No push to the technician app
    App->>Api: GET /api/technicians/me/jobs?view=completed lists the cancelled job under Completed
    App->>Api: GET /api/jobs/{jobId} when it is opened, read-only, with the reason from its history
```

A job can be cancelled from Unassigned, Assigned, In progress or On hold, and it keeps its assignee. A draft inspection stays unsubmitted and can no longer be saved, because saves need an In progress job. A cancellation racing an assignment, a status change, a submission or an inspection save ends with one success and `409` for the other request.

### Dashboard summary

```mermaid
sequenceDiagram
    autonumber
    participant Web as Dashboard
    participant Api as API
    participant Db as PostgreSQL
    Web->>Api: GET /api/dashboard/summary?from&to, the local day as UTC instants
    Web->>Api: GET /api/technicians for the workload
    Note over Api: The Administrator policy, then the window rule: from before to, at most 31 days
    Api->>Db: One read-only REPEATABLE READ transaction with six reads
    Note over Db: Open jobs by status, the day's completed and cancelled jobs, the day's created jobs,<br/>the ten unassigned jobs that start first, technician counts, the six newest history entries
    Api-->>Web: 200 with generatedAt, the figures and the lists
    Note over Web: Refetched on every live event, debounced, on every reconnect, and at local midnight
```

All six reads see one snapshot of the database, so the figures agree with each other. The job and history reads use the indexes on the job status and the history time, so their cost follows the open jobs and the window's history entries rather than every job ever stored.

### Client error report

```mermaid
sequenceDiagram
    autonumber
    participant Page as Dashboard page
    participant Rep as Error reporter
    participant Api as API
    participant Log as API log file
    Page->>Rep: An uncaught error, an unhandled rejection or a router error
    Rep-->>Page: An error toast at once, with the failed request's reference when there is one
    opt an administrator is signed in, the error is new, fewer than 10 were sent in the last 60 seconds, and no pause after a 429 is running
        Note over Rep: A failed request is described by its status and code only. Quoted text, query strings,<br/>tokens and email addresses are removed, the stack is reduced to its frames, and the route is the path only
        Rep->>Api: POST /api/client-errors with a new X-Correlation-ID
        Note over Api: Administrator policy, 10 a minute per user (429), 8 KB (413), the fixed members (400)
        Api->>Api: Scrub the text members again, ignore unknown members
        Api->>Log: Warning client-error.reported with Source web, the user id and the correlation id
        Api-->>Rep: 202 with no body
        Rep-->>Page: When the error carried no reference of its own and its toast is still shown, the toast gains the report's reference
    end
```

The reference on the toast is always one the API's log holds, so support can find the entry from what the administrator reads out. The log entry and its properties are described in [logging.md](logging.md), and the controls in [security.md](security.md#client-error-reports).

### Real-time broadcast

```mermaid
sequenceDiagram
    autonumber
    box API process
        participant H as Request handler
        participant Q as Event queue
        participant D as Event dispatcher
    end
    box Notification service process
        participant R as POST /internal/job-events
        participant IO as Socket.IO server
    end
    participant B as Dashboard tabs in the administrators room
    H->>Q: Queue the event after the commit, never waiting
    Note over Q: At most 1,000 events, the oldest dropped with a warning
    D->>Q: Read the next event, in order
    D->>D: Serialize the envelope once and sign id.timestamp.body with HMAC-SHA256
    D->>R: POST with webhook-id, webhook-timestamp, webhook-signature and X-Correlation-ID
    Note over D,R: 2 s per attempt, up to 3 jittered retries, 10 s in total, a circuit breaker
    Note over R: Route 404 or 405, media type 415, size 413,<br/>timestamp and signature 401, schema and id match 400
    alt the service is unreachable or the budget is spent
        D->>D: Log a warning with the event id and move on, the change stays committed
    else the id was already accepted
        R-->>D: 202 duplicate, no broadcast
    else an unknown type or version
        R-->>D: 202 ignored, logged as a warning
    else verified
        R->>IO: Emit the event to the administrators room
        IO-)B: eventId, jobId, jobNumber, status, previousStatus and occurredAt
        R-->>D: 202 broadcast
        B->>B: Type guard, and drop an event id already handled
        B->>H: GET /api/jobs/{jobId}, at most one in flight and one a second per job
        B->>H: Debounced refetch of the visible lists
    end
```

Requests never wait for the notification service, and a delivery failure never undoes a change. The service verifies, deduplicates and relays thin events; the dashboard treats each one as a hint and refetches from the API, with at most 60 event-driven requests a minute per tab. The design is recorded in [ADR 0009](decisions/0009-signed-webhooks-from-api-to-notifications.md).

### Reconnect and refetch

```mermaid
sequenceDiagram
    autonumber
    actor Admin as Administrator
    participant Web as Dashboard
    participant Api as API
    participant Notif as Notification service
    alt network loss or a service restart
        Note over Web,Notif: Socket.IO reconnects by itself, from 1 s, capped at 5 s, randomization 0.5
    else the session cap ends the socket
        Notif->>Web: Disconnect, and the dashboard connects again while signed in
    end
    Web-->>Admin: The pill shows Reconnecting at once, the banner after 3 s
    loop every connection attempt
        Web->>Notif: WebSocket upgrade on /socket.io with the Origin header
        Note over Notif: Origin allowlist, before any ticket is read
        Web->>Api: POST /api/auth/socket-tickets with the Bearer token
        opt the access token has expired
            Api-->>Web: 401
            Web->>Api: POST /api/auth/refresh with the cookie and the CSRF header, then the ticket request again
        end
        Api-->>Web: 200 with a 60-second ticket
        Web->>Notif: Socket.IO handshake with the ticket in auth
        Note over Notif: Signature, issuer, audience, age,<br/>Administrator role, and a ticket id not seen before
        alt accepted
            Notif-->>Web: Connected, and joined to the administrators room
            Web->>Api: Refetch what the page shows: the job list with its status counts, and any open job
            Web-->>Admin: The Live pill, after Back online, Jobs refreshed at the current time when a banner was shown
        else refused with ticket_invalid or forbidden
            Web->>Api: Refresh the session once, then try again
        else refused with ticket_missing, or the ticket request failed
            Web->>Web: Wait 1 s, doubling to 30 s with jitter, then try again
        end
    end
    opt 30 s without a connection
        Web-->>Admin: Live updates are paused, with Refresh now, while retries continue
    end
```

Every handshake uses a fresh ticket, and every successful connection refetches what the page shows, so events missed while disconnected never leave stale data on screen. The ticket design is recorded in [ADR 0008](decisions/0008-socket-tickets-for-the-notification-service.md), and the connection states in [ui.md](ui.md#live-updates).
