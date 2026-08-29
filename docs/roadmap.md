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
| M1 | Target skeleton | Prove Clean Architecture boundaries with code and enforced tests, not just words | `Domain`/`Application`/`Infrastructure`/`Api` projects, architecture tests, Serilog structured logging, correlation ID, central exception handling | ⬜ |
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

## M1 — not started

Target skeleton only: `Domain`, `Application`, `Infrastructure`, `Api` projects with enforced
one-way dependencies, architecture tests, Serilog, correlation ID, central exception handling,
configuration/DI conventions. Broken into small (15–30 minute) slices; the legacy M0 projects are
not touched or extended during M1 — M1 stands alongside them until extraction begins in M2.
