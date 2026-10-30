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
| M2 | Extract Ordering | Real strangler-fig extraction, not a rewrite | Order aggregate + state machine, CQRS/MediatR, legacy route strangled behind the new implementation | ✅ |
| M3 | Extract Inventory/Stock | A genuine cross-module consistency boundary | Stock aggregate, reservation invariant; stock reservation stays synchronous at checkout, the outbox publishes the *result* after, never initiates it | ✅ |
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

## M2 — completed

Ordering extraction, built as the domain model plus two independently committed vertical slices:

- `CommerceFlow.Modules.Ordering.Domain`: `Order`/`OrderItem` aggregate. `Order.Create(lines)`
  enforces the two invariants proven in `usp_CreateOrder`/[`acceptance-criteria.md`](acceptance-criteria.md)
  (at least one item; quantity > 0 per line, matching the exact `THROW` messages), computes
  `TotalAmount` from items rather than storing it independently, and exposes `Items` as a
  read-only view that cannot be mutated through any interface it happens to implement (10 tests).
- Read path: `GET /api/orders/{id}` → `IOrderQueries` (Application) → `OrderQueries`
  (Infrastructure, direct ADO.NET against `usp_GetOrderById`) → a dedicated `OrderDetailsDto`,
  deliberately independent of the write-side `Order` aggregate — legacy `OrderId` is `int`,
  `Order.Id` is `Guid`, and the two are not convertible. Returns `404` when absent.
- Write path: `POST /api/orders` → `CreateOrderUseCase` → `IOrderWriter` (Application) →
  `OrderWriter` (Infrastructure, direct ADO.NET against `usp_CreateOrder`, the same structured-
  parameter technique `Legacy.DAL.OrderDataAccess` uses, but its own code — no reference to
  `Legacy.DAL`/`BLL`/`Web`). The request accepts only `CustomerId` and `{ProductId, Quantity}`
  lines; there is no `UnitPrice` field anywhere on it, so a client cannot supply or influence
  price. Pricing, stock reservation, customer/product validation, the transaction, and canonical
  lock ordering all stay inside `usp_CreateOrder`, unchanged. Its five business errors
  (51000–51004) come back as `400` Problem Details carrying the procedure's own message; any
  other failure (deadlock, connection loss, etc.) is not caught here and reaches
  `GlobalExceptionHandler` as a `500`.
- `ControllerDependencyTests` (NetArchTest, IL-based): `OrdersController` must not depend on
  `CommerceFlow.Modules.Ordering.Infrastructure` — the rule M1 deferred until a real controller
  existed.
- 62 tests total: `CommerceFlow.Modules.Ordering.Domain.Tests` (10), `CommerceFlow.ArchitectureTests`
  (6), `CommerceFlow.Api.Tests` (30), `CommerceFlow.Legacy.CharacterizationTests` (16, unchanged —
  proving no regression in the system M2 extracts from).

**Deliberately deferred, not gaps:**
- **No order-status state machine.** `Legacy.Models.OrderStatus` documents, in its own comment,
  that `Confirmed`/`Cancelled` are "reserved values reachable only by a direct, out-of-band
  database update" — no application code anywhere ever transitions an order's status. There is no
  evidence to extract a state machine from; inventing one now would be new functionality, not
  extraction.
- **No CQRS/MediatR.** Two use cases (one read, one write) do not yet justify a dispatch pipeline;
  `CreateOrderUseCase`/`IOrderQueries`/`IOrderWriter` already give Application its own request/
  response shapes and a one-way dependency from Infrastructure without it. Revisit once a third
  use case, or a real cross-cutting concern, actually needs one.
- **No literal traffic cutover for "legacy route strangled."** `CommerceFlow.Api` and `Legacy.Web`
  are separate processes with no shared reverse proxy or deployed environment in this repo; the
  new endpoints are capability-equivalent against the same stored procedures (proven by
  `OrderWriteIntegrationTests`/`OrderReadIntegrationTests` running the real procedures, not
  mocks) — the demonstrable unit for a portfolio project. Legacy is still not modified: the
  milestone is "a working replacement exists," not "legacy was edited to redirect to it."
- The write path intentionally does not route through the `Order` aggregate (see
  `CreateOrderUseCase`'s own comment) — `Order` has no role on the production write path yet. It
  gains one once pricing/customer/stock modeling genuinely moves into this module, which is M3's
  job, not this one's.

The legacy M0 projects remain untouched — extraction continues alongside them, not in place of
them, through M3 onward.

## M3 — completed (verified, not yet production-wired)

Inventory extraction, deliberately scoped to a domain model plus a proof of behavioral
equivalence — not a production cutover:

- `CommerceFlow.Modules.Inventory.Domain`: `Stock` aggregate. `Stock.Create` rejects negative
  quantity-on-hand (`CK_Products_StockQuantity CHECK (StockQuantity >= 0)`);
  `TryReserve(quantity)` returns `false` rather than throwing on insufficient stock — the same
  "expected business outcome, not a caller error" reasoning that kept `CreateOrderResult` a
  value, not an exception, on the Ordering write path. Mirrors `usp_CreateOrder`'s own
  conditional `UPDATE ... WHERE StockQuantity >= @Qty` / `THROW 51002` check (8 tests).
- `CommerceFlow.Modules.Inventory.Application`: `IStockReader` port; `ReservationPlanner`, a
  pure, in-memory decision over a snapshot of `Stock` instances — groups duplicate `ProductId`
  lines by sum (matching AC8), orders them canonically ascending by `ProductId` (the same
  deadlock-avoidance convention `usp_CreateOrder` uses across products in one transaction, though
  this coordinator does not itself own a transaction or undo an already-succeeded reservation on
  a later line's failure — it is a prediction, never persisted).
- `CommerceFlow.Modules.Inventory.Infrastructure`: `StockReader`, direct read-only ADO.NET
  against the same `dbo.Products` table `usp_CreateOrder` already owns — no reference to
  `Legacy.DAL`/`BLL`/`Web`.
- **Shadow-mode verification** (`ReservationPlannerShadowTests`): for the same order lines, the
  C# `ReservationPlanner` decision is compared against the real, deployed `usp_CreateOrder`
  decision — both the sufficient-stock/duplicate-line case and the insufficient-stock
  all-or-nothing case (mirroring `Legacy.CharacterizationTests.OrderApiTests.
  CreateOrder_InsufficientStock_IsAllOrNothing`). Both agree, proven against a real Testcontainers
  SQL Server, not mocks.
- `Inventory_Domain_*`/`Inventory_Application_*`/`Inventory_Infrastructure_*` reference-graph
  tests in `CommerceFlow.ArchitectureTests`, mirroring the Ordering set exactly.
- 76 tests total, 14 new since M2's 62: `ArchitectureTests` 6 → 10, plus the new
  `Inventory.Domain.Tests` (8) and `Inventory.Infrastructure.Tests` (2) projects.

**Deliberately deferred, not gaps:**
- **Not wired into the production create-order flow.** `CreateOrderUseCase`/`OrderWriter` never
  construct or call a `Stock` instance. The actual authority protecting stock in production
  today is still `usp_CreateOrder`'s conditional `UPDATE` and its surrounding transaction — this
  module proves the new decision logic *agrees* with that authority, it does not replace it yet.
- **Cross-module transaction ownership is an open design question, not decided.** How a future
  C# extraction would own a single transaction spanning Ordering and Inventory, and coordinate
  multiple `Stock` instances atomically, was evaluated and deliberately not attempted this
  milestone: it is not a small extraction step but a wholesale rewrite of transaction, locking,
  rollback, and concurrency behavior currently proven correct inside `usp_CreateOrder`. Revisiting
  it is a dedicated future initiative, not a continuation of M3's small-step cadence.
- **Canonical `ProductId`-ascending ordering is a convention inside `ReservationPlanner`, not an
  enforced, reusable invariant** — it reduces one specific deadlock pattern (two orders touching
  the same products in different submission order), not a blanket guarantee.
- **No outbox or event publication.** Publishing the *result* of a reservation is M4's job, and
  presupposes a real reservation to publish the result of.

The legacy M0 projects remain untouched. The real cross-module write path — the actual subject of
"a genuine cross-module consistency boundary" — remains open and is explicitly not claimed as
solved by this milestone.
