# Roadmap

CommerceFlow simulates a legacy .NET Framework → Modular Monolith migration for an Order
Management & Fulfillment domain (Order → Stock Reservation → Warehouse Routing → ERP Export →
Shipment). Each milestone below is an epic worked through as a backlog, not a calendar slot.

## Status legend

- ✅ Done
- 🚧 In progress
- ⬜ Not started

## Milestones

| # | Milestone | Learning goal | Technical goal | Status |
|---|---|---|---|---|
| M0 | Legacy seed | Recognize and reproduce realistic legacy debt on purpose, not by accident | Working 3-tier order-creation + ERP export flow; ADO.NET; invariants owned by a stored procedure; a characterization-test safety net | ✅ |
| M1 | Target skeleton | Prove Clean Architecture boundaries with code and enforced tests, not just words | `Domain`/`Application`/`Infrastructure`/`Api` projects, architecture tests, Serilog structured logging, correlation ID, central exception handling | ✅ |
| M2 | Extract Ordering | Real strangler-fig extraction, not a rewrite | Order aggregate + state machine, CQRS/MediatR, legacy route strangled behind the new implementation | ⬜ |
| M3 | Extract Inventory/Stock | A genuine cross-module consistency boundary | Stock aggregate, reservation invariant; stock reservation stays synchronous at checkout, the outbox publishes the *result* after, never initiates it | ⬜ |
| M4 | Outbox + Hangfire | At-least-once delivery proven, not assumed | End-to-end outbox dispatch, idempotent consumers, a dispatcher-kill resilience test | ⬜ |
| M5 | ERP integration + resilience | The reliability story a real ERP boundary actually needs | Anti-corruption layer, a deliberately flaky FakeErp, Polly retry/circuit breaker, reconciliation job | ⬜ |
| M6 | Fulfillment/Warehouse | Workflow complexity beyond a single aggregate | Routing, partial shipments | ⬜ |
| M7 | Search | Eventual consistency, made visible instead of assumed | Elasticsearch read model fed by the same event stream | ⬜ |
| M8 | Observability hardening | Debuggability of everything M2–M7 introduced | Tracing, dashboards, deeper structured logging | ⬜ |
| M9 | Microservice extraction | The JD's "future microservice extraction" line, actually demonstrated | RabbitMQ swap behind the same messaging contract, one module extracted to its own deployable | ⬜ |

**M0–M5 are the mandatory portfolio core; M6–M8 are the next phase; M9 is the final capstone.**
M0–M5 quality is never traded down to make room for later milestones.

## M0 — completed

- DbUp migration runner (`migrate` / `reset-data` / `list-scripts`), gated `reset-data` via
  `CommerceFlowEnvironment`, verified against a real SQL Server container.
- Database schema + `usp_CreateOrder` / `usp_GetOrderById`, deterministic seed data.
- `Legacy.Models` / `DAL` / `BLL` / `Web` / `FakeErp` / `ErpExportJob` — full order-creation and
  ERP-export flow, verified end-to-end over real HTTP against a real database.
- Testcontainers-backed characterization test suite (16 tests) covering most of AC1–AC11 — see
  [`acceptance-criteria.md`](acceptance-criteria.md) for exactly which ones, and which are not yet
  under automated coverage.
- `RetryPolicy` extracted from `OrderDataAccess` with deterministic unit tests for the retry
  mechanics (not for the real deadlock scenario itself — see the acceptance-criteria doc).

## M0 — known, accepted gaps (not blocking M1)

- AC4 (duplicate-submission idempotency), AC6 (ERP export success path), and AC7 (unbounded
  `ErpExportAttempts` growth) have no automated regression test — verified manually only.
- No test proves a real deadlock occurred or that canonical lock ordering prevents one at
  runtime — that specific claim is a structural property of `usp_CreateOrder`, verified by
  inspection and by `StoredProcedureContractTests`, not by a race-timing test.
- No test verifies the client receives a sanitized message once the 1205 retry budget is
  exhausted — building one requires either a large, fragile deadlock-forcing harness or DI/
  interface seams that conflict with the deliberate no-DI legacy design of M0.

## M1 — completed

Target skeleton for the Ordering module, built incrementally and each slice committed separately:

- `src/CommerceFlow.Api` plus `CommerceFlow.Modules.Ordering.{Domain,Application,Infrastructure}`,
  with a one-way dependency graph (`Domain` has zero project/package references; `Application` →
  `Domain`; `Infrastructure` → `Domain`+`Application`; `Api` → `Application`+`Infrastructure`, the
  latter only for composition-root DI wiring, never from controllers).
- `CommerceFlow.ArchitectureTests`: deterministic `.csproj`-file-based reference-graph checks (not
  reflection/IL-based) — this specifically catches an unused-but-present `<ProjectReference>` that
  NetArchTest's type-usage analysis would miss. Verified against a real violation (added, watched
  fail, reverted, watched pass again), not just asserted.
- Serilog structured console logging, configured at the composition root.
- `CorrelationIdMiddleware`: validates client-supplied `X-Correlation-ID` (non-empty, ≤64 chars,
  `[A-Za-z0-9_-]`, single header value), falls back to a generated GUID on any violation without
  ever failing the request; rejections logged at Information with structured `Reason`/
  `ProvidedLength`/`ValueCount` fields only — never the raw value or a hash. Registered first in
  the pipeline, before Swagger/HTTPS redirection, since both can short-circuit a request.
- `GlobalExceptionHandler` (`IExceptionHandler` + `AddProblemDetails()`): any unhandled exception
  returns `500` + `application/problem+json`, with a generic title/detail and the correlation ID
  as a `correlationId` extension field — never the exception message, type, or stack trace. Logged
  server-side at Error exactly once. Correlation ID matches across the response header, the
  Problem Details body, and the error log, proven by tests, not assumed.
- 20 automated tests across `CommerceFlow.Api.Tests` (correlation ID behavior, pipeline ordering,
  exception-handler behavior and DI wiring) and `CommerceFlow.ArchitectureTests` (reference graph).

**Deliberately deferred to later milestones, not gaps:**
- NetArchTest type-dependency rules (e.g. "Ordering must not reference Inventory") wait until
  real types exist in the modules — writing them against today's empty assemblies would either
  vacuously pass or require a "found zero types" guard that fails immediately either way,
  protecting nothing.
- The `Api`-controllers-must-not-reference-`Infrastructure` rule waits until a real controller
  exists, for the same reason.
- No `ICorrelationContext` abstraction for `Application`/`Domain` yet — no real cross-module need
  for it exists before M4's outbox work.

The legacy M0 projects were not touched or extended during M1 — it stands alongside them until
extraction begins in M2.
