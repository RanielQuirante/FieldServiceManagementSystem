# Vertical slices

Every application in FieldService is organised by feature and use case rather than by technical layer ([ADR 0003](decisions/0003-vertical-slice-architecture.md)). A use case keeps its entry point, validation, data access and response shape together, and code moves to a shared folder only when two or more features need it. This page walks through one slice in each application.

## API: create a job

`src/FieldService.Api/Features/Jobs/CreateJob/`

| File | Role |
|---|---|
| `CreateJobRequest.cs` | The request body, a sealed record with its validation attributes |
| `AfterScheduledStartAttribute.cs` | A validation rule only this slice uses: the scheduled end must follow the start |
| `CreateJobEndpoint.cs` | `Map` adds `POST /api/jobs` to the jobs group, requires the Administrator policy and declares its responses; `HandleAsync` validates, creates the job, saves it, queues the `job.created` event and returns `201 Created` with the job's details |

`Features/Jobs/JobsEndpoints.cs` maps the jobs group and calls `CreateJobEndpoint.Map`. The handler's return type, `Results<Created<JobDetailsResponse>, ValidationProblem, ProblemHttpResult>`, lists every outcome, so the OpenAPI document describes them without extra annotations. The slice has no mediator, service layer or repository: the handler uses the database context directly ([ADR 0004](decisions/0004-minimal-api-slices-without-mediator.md)). Code shared across the job slices, such as `JobDetailsQuery` and `JobDetailsResponse`, sits one level up in `Features/Jobs/`.

Tests: `tests/FieldService.Api.Tests/Features/Jobs/CreateJobTests.cs` runs the endpoint against a real PostgreSQL database, and the contract and security suites check the same operation against the committed OpenAPI document.

## Dashboard: the jobs feature

`src/FieldService.Web/src/features/jobs/`

| Part | Role |
|---|---|
| `pages/` | One component per route: `JobsPage.vue`, `JobDetailPage.vue`, `CreateJobPage.vue`, `InspectionReportPage.vue` |
| `components/` | The feature's building blocks, such as `CreateJobForm.vue`, `JobsTable.vue`, `AssignTechnicianCard.vue` and `CancelJobDialog.vue` |
| `jobs-api.ts` | The feature's calls to the API, typed from the generated contract types |
| `jobs-store.ts` | The feature's Pinia store: the job list, its filters and its refetch after a live event |
| `__tests__/` | Vitest specs next to the code they test |

The router imports the pages; other features import nothing from inside `jobs/`. Shared pieces, such as the authenticated fetch, the toast host and the design tokens, live in `src/shared/`.

## Technician app: the inspection

`src/FieldService.Mobile/Features/Inspection/`

| File | Role |
|---|---|
| `InspectionPage.xaml`, `ReviewAndSubmitPage.xaml` | The two screens, bound to their view models with compiled bindings |
| `InspectionViewModel.cs`, `InspectionItemViewModel.cs`, `ReadingFieldViewModel.cs`, `ReviewAndSubmitViewModel.cs` | Screen state and commands, built with CommunityToolkit.Mvvm |
| `InspectionApiClient.cs`, `InspectionDtos.cs` | The typed client and the request and response records for the inspection endpoints |
| `InspectionStore.cs` | Answers kept in memory until they are saved |
| `InspectionFeatureRegistration.cs` | Registers the feature's pages, view models and client with the app's container |

Each feature registers itself, so `MauiProgram.cs` only calls the registrations. Controls used by several features, such as the bottom sheet, the form field and the banner, live in `Shared/Controls/`. Tests: `tests/FieldService.Mobile.Tests/Features/Inspection/`.

## Notification service: job events

`src/FieldService.Notifications/src/features/job-events/`

| File | Role |
|---|---|
| `job-events-route.ts` | `POST /internal/job-events`: checks the size and media type, verifies the event, drops a duplicate event id, and answers |
| `webhook-signature.ts` | Verifies the Standard Webhooks signature and timestamp |
| `recent-event-ids.ts` | Remembers recent event ids, so a repeated event is not broadcast twice |
| `job-event-envelope.ts` | The event's schema, checked with zod |
| `job-event-broadcast.ts` | Sends the accepted event to the administrators' Socket.IO room |

The neighbouring `admin-connections/` feature verifies socket tickets and manages connections, and `health/` serves the health routes. `server.ts` wires the features onto one `node:http` server. Tests: `tests/FieldService.Notifications.Tests/`.
