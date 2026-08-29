# M0 Legacy Technical Debt

Every item below is **intentional** — a realistic shortcut a real enterprise .NET Framework
team would plausibly have taken, reproduced on purpose so the eventual migration (M1+) has
something genuine to migrate *from*. None of these are oversights; each is called out explicitly
so it's never mistaken for one. Security vulnerabilities (e.g. SQL injection) are never part of
this list on principle — architectural messiness is realistic, actual vulnerabilities are not a
lesson worth teaching even in a "legacy" demo.

## Business rules living in the stored procedure

`usp_CreateOrder` owns essentially all real invariants (stock, all-or-nothing, quantity
validation, customer/product existence) — not `CommerceFlow.Legacy.BLL`, which is nominally the
"business logic layer" but is, in practice, a thin pass-through. This is a very common real-world
drift: business logic migrates into the database over time because it's the easiest place to make
a change land reliably. **Addressed in:** M2, when Ordering is extracted and the invariants move
into a real domain model.

## Anemic models

`Customer`, `Product`, `Order`, `OrderItem` in `CommerceFlow.Legacy.Models` are plain
get/set POCOs with no behavior — nothing stops constructing an `Order` in an invalid state outside
the sproc's own checks. **Addressed in:** M2, with real aggregates enforcing their own invariants.

## Raw ADO.NET in the DAL and the export job

`OrderDataAccess` and `ErpExportJob` both hand-roll `SqlConnection`/`SqlCommand` code directly —
no ORM, no query builder, no repository abstraction over it. **Addressed in:** M1+ introduces
`Infrastructure` implementations behind interfaces defined in `Application`.

## ErpExportJob does not share the DAL

`ErpExportJob` deliberately does not reference `CommerceFlow.Legacy.DAL` or `.BLL`, and does not
share a model type with the rest of the app — its own hand-rolled query/update code, independently
maintained. This simulates a very real pattern: a web app and a batch job growing apart because no
shared library was ever built between them. **Addressed in:** the modular monolith's shared kernel,
introduced progressively from M2 onward.

## HTTP 200 with `success:false`

Every business outcome — success or failure — returns HTTP 200 with a `{success, data, message}`
envelope; only structurally invalid JSON gets a framework-level 400. Real HTTP status semantics are
not used. **Addressed in:** a versioned API contract change, explicitly called out with an ADR
when it happens (not before M2 at the earliest — this is a breaking change for any consumer).

## No authentication or authorization

Any caller can create an order for any customer ID. There is no login, no token, no ownership
check anywhere in the M0 API surface. **Addressed in:** whenever the target API gets a real
identity story — not scoped to a specific milestone yet.

## ERP dual-write risk

`ErpExportJob` and `usp_CreateOrder` both write to the same `Orders` row (export flag vs. order
data) with no coordination beyond the polling query itself — there is no outbox, no transactional
guarantee linking "order created" to "export will eventually happen" beyond "the job will notice
the flag next time it runs." **Addressed in:** M4, with a real transactional outbox.

## No atomic job claim

`ErpExportJob` selects all orders with `IsExportedToErp = 0` and processes them without ever
marking a row as "claimed" first. If two instances of the job ran concurrently (they don't today,
but nothing prevents it), both could select and export the same order. **Addressed in:** M4/M5,
once there's a real dispatcher with delivery-attempt tracking.

## No health endpoint

Neither `Legacy.Web` nor `FakeErp` exposes a `/health` route. Verifying the API is up today means
calling a real business endpoint and checking it responds sensibly. **Addressed in:** M1's target
API skeleton.

## No observability or correlation ID

No structured logging, no request correlation ID, no tracing anywhere in M0. Debugging today means
reading whichever console window happens to be open. **Addressed in:** M1 (Serilog + correlation
ID is explicitly one of M1's first slices — deliberately *not* bundled into M0, so the contrast
between "before" and "after" stays visible).

## No inventory/stock read API

There is no endpoint to query current stock — the only way to see `Products.StockQuantity` today
is a direct SQL query. **Addressed in:** M3, alongside the Inventory module extraction.

## No UI

CommerceFlow is API-only. There is no frontend anywhere in this repository, and none is planned —
out of scope for what this project is meant to demonstrate.
