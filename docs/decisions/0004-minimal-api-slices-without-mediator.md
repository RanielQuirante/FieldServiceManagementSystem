# 0004. Minimal API slices without a mediator

## Status

Accepted, 2026-10-20.

## Context

The API is organized as vertical slices (ADR 0003). Each slice needs an HTTP endpoint, input validation, authorization, data access and a documented set of outcomes. Common .NET choices add layers between the endpoint and the work: MVC controllers, a mediator, an object mapper and repository abstractions over the database context.

## Decision

- **Minimal APIs.** Each slice has a static `<Slice>Endpoint` class with:
  - `Map(RouteGroupBuilder)`, which sets the endpoint name, summary, description, authorization policy, rate-limit policy and request size limit;
  - a static `HandleAsync` that returns a `Results<...>` union of `TypedResults`, for example `Results<Ok<JobDetailsResponse>, NotFound<ProblemDetails>, Conflict<ProblemDetails>, ValidationProblem>`.
- **Validation.** Request types are sealed records validated through DataAnnotations and `IValidatableObject`.
- **Data access.** Handlers query the Entity Framework Core context directly, with hand-written projections.
- **Business rules.** Entity methods enforce them and return a `DomainError` value on failure. Expected failures are never thrown as exceptions.
- **Explicit registration.** `Features/<Feature>/<Feature>Endpoints.cs` builds the feature's route group and calls each slice's `Map`. `Features/FeatureEndpoints.cs` maps every feature, and `Program.cs` calls it once. There is no reflection-based discovery.
- **Authorization.** The fallback policy requires an authenticated user. An endpoint inventory test fails for any endpoint that has no explicit policy and is not on a short anonymous allowlist (sign-in, refresh, sign-out, the two health routes, and the OpenAPI document in Development and Testing).
- Not used: MVC controllers, a mediator library, an object mapper, generic repositories or a unit of work, and third-party endpoint frameworks.

## Consequences

- The OpenAPI document lists every outcome of every endpoint from the `TypedResults` unions, without XML documentation comments, and the compiler checks those unions.
- A reader follows one request from the route to the database inside one file.
- Cross-cutting behaviour runs as middleware or endpoint filters instead of mediator pipeline behaviours.
- Handlers are a little longer than mediator handlers, because mapping code sits inline.
